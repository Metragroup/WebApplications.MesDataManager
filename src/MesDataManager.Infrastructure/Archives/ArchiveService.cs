using System.Globalization;

using MesDataManager.Application.Archives;
using MesDataManager.Application.Security;
using MesDataManager.Domain.Abstractions;
using MesDataManager.Infrastructure.Persistence;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MesDataManager.Infrastructure.Archives;

/// <summary>
/// Implementazione unica del CRUD sulle anagrafiche. Concentra qui le tre cose che la UI non
/// deve conoscere: i permessi dell'utente, le regole di business ereditate dall'applicazione
/// WinForms e la traduzione degli errori di SQL Server in messaggi comprensibili.
/// <para>
/// Ogni operazione crea il proprio <see cref="MesDbContext"/> dalla factory e lo smaltisce:
/// in Blazor Server un contesto con ambito vivrebbe quanto la sessione, condiviso fra tutti
/// i componenti della pagina.
/// </para>
/// </summary>
public sealed class ArchiveService(
    IDbContextFactory<MesDbContext> contextFactory,
    IArchiveCatalog catalog,
    IUserContext user,
    ILogger<ArchiveService> logger) : IArchiveService
{
    public async Task<IReadOnlyList<ArchiveRow>> GetRowsAsync(
        string archiveKey,
        ArchiveQuery query,
        CancellationToken cancellationToken = default)
    {
        var descriptor = Resolve(archiveKey);
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);

        if (!permissions.CanRead)
        {
            throw ArchiveException.Forbidden();
        }

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await EntityAccessorFactory
            .For(descriptor.EntityType)
            .ListAsync(context, descriptor, query, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ArchiveRow> CreateTemplateAsync(
        string archiveKey,
        CancellationToken cancellationToken = default)
    {
        var descriptor = Resolve(archiveKey);
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);

        if (!descriptor.AllowsInsert || !permissions.CanInsert)
        {
            throw ArchiveException.Forbidden();
        }

        var row = ArchiveRow.Empty();

        foreach (var field in descriptor.Fields)
        {
            var property = descriptor.EntityType.GetProperty(field.Name);
            row[field.Name] = property is null ? null : FieldValueConverter.DefaultFor(property.PropertyType);
        }

        // Una voce appena creata dallo stabilimento nasce attiva: e' quello che si aspetta
        // chi la sta inserendo, e risparmia un passaggio in modifica.
        if (descriptor.SupportsActiveFilter)
        {
            row[nameof(IActivatable.IsActive)] = true;
        }

        return row;
    }

    public async Task InsertAsync(
        string archiveKey,
        ArchiveRow row,
        CancellationToken cancellationToken = default)
    {
        var descriptor = Resolve(archiveKey);
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);

        if (!descriptor.AllowsInsert || !permissions.CanInsert)
        {
            throw ArchiveException.Forbidden();
        }

        Validate(descriptor, row, isNewRecord: true);

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var accessor = EntityAccessorFactory.For(descriptor.EntityType);
        var entity = accessor.CreateInstance();

        foreach (var field in descriptor.Fields)
        {
            if (descriptor.IsWritable(field, isNewRecord: true))
            {
                Assign(descriptor, entity, field.Name, row[field.Name]);
            }
        }

        accessor.Add(context, entity);
        await SaveAsync(context, descriptor, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Anagrafica {Archive}: inserito un record da parte di {User}.",
            descriptor.Key,
            permissions.UserName);
    }

    public async Task UpdateAsync(
        string archiveKey,
        ArchiveRow row,
        CancellationToken cancellationToken = default)
    {
        var descriptor = Resolve(archiveKey);
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);

        if (!descriptor.AllowsUpdate || !permissions.CanUpdate)
        {
            throw ArchiveException.Forbidden();
        }

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var accessor = EntityAccessorFactory.For(descriptor.EntityType);

        // L'entita' va caricata tracciata: la modifica si esprime mutando le proprieta' e
        // lasciando che il change tracker deduca le colonne da aggiornare.
        var entity = await accessor.FindAsync(context, descriptor, row, cancellationToken).ConfigureAwait(false)
            ?? throw ArchiveException.NotFound();

        Validate(descriptor, row, isNewRecord: false);
        EnforceMasterActivationRule(descriptor, entity, row);

        foreach (var field in descriptor.Fields)
        {
            if (descriptor.IsWritable(field, isNewRecord: false))
            {
                Assign(descriptor, entity, field.Name, row[field.Name]);
            }
        }

        await SaveAsync(context, descriptor, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Anagrafica {Archive}: modificato un record da parte di {User}.",
            descriptor.Key,
            permissions.UserName);
    }

    public async Task DeleteAsync(
        string archiveKey,
        ArchiveRow row,
        CancellationToken cancellationToken = default)
    {
        var descriptor = Resolve(archiveKey);
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);

        if (!descriptor.AllowsDelete || !permissions.CanDelete)
        {
            throw ArchiveException.Forbidden();
        }

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var accessor = EntityAccessorFactory.For(descriptor.EntityType);
        var entity = await accessor.FindAsync(context, descriptor, row, cancellationToken).ConfigureAwait(false)
            ?? throw ArchiveException.NotFound();

        accessor.Remove(context, entity);
        await SaveAsync(context, descriptor, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Anagrafica {Archive}: eliminato un record da parte di {User}.",
            descriptor.Key,
            permissions.UserName);
    }

    // ------------------------------------------------------------------ regole

    private ArchiveDescriptor Resolve(string archiveKey) =>
        catalog.Find(archiveKey)
        ?? throw new ArchiveException(ArchiveErrorKind.NotFound, "ArchiveNotFound", archiveKey);

    /// <summary>
    /// Obbligatorieta', lunghezze massime e intervalli, allineati ai vincoli delle colonne.
    /// </summary>
    private static void Validate(ArchiveDescriptor descriptor, ArchiveRow row, bool isNewRecord)
    {
        foreach (var field in descriptor.Fields)
        {
            if (!descriptor.IsWritable(field, isNewRecord))
            {
                continue;
            }

            var value = row[field.Name];

            if (field.IsRequired && IsBlank(value) && field.Kind is not ArchiveFieldKind.Boolean)
            {
                throw ArchiveException.Required(field.Name);
            }

            if (field.MaxLength is { } max && value is string text && text.Length > max)
            {
                throw ArchiveException.TooLong(field.Name, max);
            }

            EnforceIntegerRange(descriptor, field, value);
        }
    }

    /// <summary>
    /// Molte chiavi e tutte le posizioni sono <c>smallint</c>. L'editor numerico non conosce il
    /// tipo della colonna, quindi un valore fuori intervallo arriverebbe fin qui e farebbe
    /// fallire la conversione con un errore che la UI non sa tradurre.
    /// </summary>
    private static void EnforceIntegerRange(ArchiveDescriptor descriptor, ArchiveField field, object? value)
    {
        if (field.Kind is not ArchiveFieldKind.Integer || value is null)
        {
            return;
        }

        var property = descriptor.EntityType.GetProperty(field.Name);
        if (property is null || FieldValueConverter.IntegerRange(property.PropertyType) is not { } range)
        {
            return;
        }

        decimal numeric;
        try
        {
            numeric = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new ArchiveException(
                ArchiveErrorKind.Validation, "InvalidValue", field.Name, innerException: ex);
        }

        if (numeric < range.Minimum || numeric > range.Maximum)
        {
            throw ArchiveException.OutOfRange(field.Name, range.Minimum, range.Maximum);
        }
    }

    /// <summary>
    /// Sulle anagrafiche allineate dall'ERP il flag "Attivo" puo' essere alzato solo se la voce
    /// e' abilitata a monte (IsActive_Master). Regola presa dall'applicazione WinForms, dove era
    /// implementata nella griglia; qui vive nel servizio, cosi' vale per qualunque chiamante.
    /// </summary>
    private static void EnforceMasterActivationRule(
        ArchiveDescriptor descriptor,
        object entity,
        ArchiveRow row)
    {
        if (!descriptor.HasMasterFlag || entity is not IMasterControlled master)
        {
            return;
        }

        var requestedActive = row[nameof(IActivatable.IsActive)] is true;

        if (requestedActive && !master.IsActiveMaster)
        {
            throw new ArchiveException(
                ArchiveErrorKind.Validation,
                "IsActiveLockedHint",
                nameof(IActivatable.IsActive));
        }
    }

    private static void Assign(ArchiveDescriptor descriptor, object entity, string fieldName, object? value)
    {
        var property = descriptor.EntityType.GetProperty(fieldName);
        if (property is null || !property.CanWrite)
        {
            return;
        }

        try
        {
            property.SetValue(entity, FieldValueConverter.Coerce(value, property.PropertyType));
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            // Rete di sicurezza per i chiamanti che non passano dalla UI: un valore non
            // convertibile e' un errore di validazione, non un guasto dell'applicazione.
            throw new ArchiveException(
                ArchiveErrorKind.Validation, "InvalidValue", fieldName, innerException: ex);
        }
    }

    /// <summary>
    /// Salva e traduce i codici di errore di SQL Server: senza questo passaggio l'operatore
    /// vedrebbe il messaggio grezzo del provider al posto della causa reale.
    /// </summary>
    private async Task SaveAsync(
        MesDbContext context,
        ArchiveDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql)
        {
            logger.LogWarning(
                ex,
                "Anagrafica {Archive}: salvataggio rifiutato dal database (errore {Number}).",
                descriptor.Key,
                sql.Number);

            throw sql.Number switch
            {
                2601 or 2627 => new ArchiveException(
                    ArchiveErrorKind.DuplicateKey, "DuplicateKey", innerException: ex),
                547 => new ArchiveException(
                    ArchiveErrorKind.ForeignKeyViolation, "ForeignKeyViolation", innerException: ex),
                _ => new ArchiveException(
                    ArchiveErrorKind.Unknown, "SaveFailed", innerException: ex),
            };
        }
    }

    private static bool IsBlank(object? value) =>
        value is null || (value is string text && string.IsNullOrWhiteSpace(text));
}
