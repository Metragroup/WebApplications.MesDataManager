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
/// </summary>
public sealed class ArchiveService(
    MesDbContext context,
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

        if (!user.CanRead)
        {
            throw ArchiveException.Forbidden();
        }

        return await EntityAccessorFactory
            .For(descriptor.EntityType)
            .ListAsync(context, descriptor, query, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<ArchiveRow> CreateTemplateAsync(
        string archiveKey,
        CancellationToken cancellationToken = default)
    {
        var descriptor = Resolve(archiveKey);
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

        return Task.FromResult(row);
    }

    public async Task InsertAsync(
        string archiveKey,
        ArchiveRow row,
        CancellationToken cancellationToken = default)
    {
        var descriptor = Resolve(archiveKey);

        if (!descriptor.AllowsInsert || !user.CanInsert)
        {
            throw ArchiveException.Forbidden();
        }

        Validate(descriptor, row, isNewRecord: true);

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
        await SaveAsync(descriptor, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Anagrafica {Archive}: inserito un record da parte di {User}.",
            descriptor.Key,
            user.UserName);
    }

    public async Task UpdateAsync(
        string archiveKey,
        ArchiveRow row,
        CancellationToken cancellationToken = default)
    {
        var descriptor = Resolve(archiveKey);

        if (!descriptor.AllowsUpdate || !user.CanUpdate)
        {
            throw ArchiveException.Forbidden();
        }

        var accessor = EntityAccessorFactory.For(descriptor.EntityType);
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

        await SaveAsync(descriptor, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Anagrafica {Archive}: modificato un record da parte di {User}.",
            descriptor.Key,
            user.UserName);
    }

    public async Task DeleteAsync(
        string archiveKey,
        ArchiveRow row,
        CancellationToken cancellationToken = default)
    {
        var descriptor = Resolve(archiveKey);

        if (!descriptor.AllowsDelete || !user.CanDelete)
        {
            throw ArchiveException.Forbidden();
        }

        var accessor = EntityAccessorFactory.For(descriptor.EntityType);
        var entity = await accessor.FindAsync(context, descriptor, row, cancellationToken).ConfigureAwait(false)
            ?? throw ArchiveException.NotFound();

        accessor.Remove(context, entity);
        await SaveAsync(descriptor, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Anagrafica {Archive}: eliminato un record da parte di {User}.",
            descriptor.Key,
            user.UserName);
    }

    // ------------------------------------------------------------------ regole

    private ArchiveDescriptor Resolve(string archiveKey) =>
        catalog.Find(archiveKey)
        ?? throw new ArchiveException(ArchiveErrorKind.NotFound, "ArchiveNotFound", archiveKey);

    /// <summary>Obbligatorieta' e lunghezze massime, allineate ai vincoli delle colonne.</summary>
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

        property.SetValue(entity, FieldValueConverter.Coerce(value, property.PropertyType));
    }

    /// <summary>
    /// Salva e traduce i codici di errore di SQL Server: senza questo passaggio l'operatore
    /// vedrebbe il messaggio grezzo del provider al posto della causa reale.
    /// </summary>
    private async Task SaveAsync(ArchiveDescriptor descriptor, CancellationToken cancellationToken)
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
        finally
        {
            // Il contesto e' per-richiesta ma il circuito Blazor Server e' longevo: le entita'
            // tracciate vengono liberate a ogni operazione per non far crescere il change tracker.
            context.ChangeTracker.Clear();
        }
    }

    private static bool IsBlank(object? value) =>
        value is null || (value is string text && string.IsNullOrWhiteSpace(text));
}
