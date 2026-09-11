using MesDataManager.Application.Production;
using MesDataManager.Application.Security;
using MesDataManager.Domain.Entities;
using MesDataManager.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MesDataManager.Infrastructure.Production;

/// <summary>
/// <see cref="BatchService"/>, ciclo di vita del blocco di modifica: prenderlo, rilasciarlo,
/// forzarne lo sblocco.
/// <para>
/// La presa e' <b>una sola istruzione</b> con la condizione dentro — libero, mio, o scaduto —
/// perche' leggere e poi scrivere lascia la finestra in cui due utenti passano entrambi il
/// controllo e il secondo si porta via il lotto del primo.
/// </para>
/// </summary>
public sealed partial class BatchService
{
    public async Task<BatchDetail> BeginEditAsync(
        string batchId,
        CancellationToken cancellationToken = default)
    {
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!permissions.CanEditProduction)
        {
            throw ProductionException.Forbidden();
        }

        var owner = LockOwner(permissions)
            ?? throw ProductionException.Forbidden();

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var state = await LockStateAsync(context, batchId, cancellationToken).ConfigureAwait(false);

        // Le due precondizioni del vecchio applicativo, nell'ordine in cui le controllava lui:
        // prima l'elaborazione in corso, poi la riconciliazione.
        if (!state.IsBatchProcessed)
        {
            throw ProductionException.BatchProcessing();
        }

        if (state.IsErpImported)
        {
            throw ProductionException.BatchAlreadyReconciled();
        }

        var now = DateTime.Now;
        var staleBefore = now - BatchLockPolicy.Expiry;

        // La presa del blocco e' una sola istruzione con la condizione dentro: libero, mio, o
        // scaduto. Leggere e poi scrivere lascerebbe aperta la finestra in cui due utenti
        // passano entrambi il controllo e il secondo si porta via il lotto del primo — nel
        // WinForms non capitava perche' i client erano pochi e lenti, qui due schede dello
        // stesso browser bastano.
        var claimed = await context.Batches
            .Where(b => b.BatchId == batchId)
            .Where(b => !b.IsLock || b.LockUsr == owner || b.LockTs == null || b.LockTs < staleBefore)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(b => b.IsLock, true)
                    .SetProperty(b => b.LockUsr, owner)
                    .SetProperty(b => b.LockTs, now),
                cancellationToken)
            .ConfigureAwait(false);

        if (claimed == 0)
        {
            // Nessuna riga aggiornata: il blocco e' di un altro e non e' scaduto. Si rilegge per
            // dire nel messaggio chi e da quando, che e' l'unica informazione utile.
            var current = await LockStateAsync(context, batchId, cancellationToken).ConfigureAwait(false);
            throw ProductionException.BatchLocked(current.LockUsr, current.LockTs);
        }

        if (state.IsLock && state.LockUsr != owner)
        {
            logger.LogInformation(
                "Lotti: {User} ha preso il lotto {Batch}, il cui blocco di {Previous} del {Ts} era scaduto.",
                owner,
                batchId,
                state.LockUsr,
                state.LockTs);
        }
        else
        {
            logger.LogInformation("Lotti: {User} ha preso in modifica il lotto {Batch}.", owner, batchId);
        }

        return await GetDetailAsync(batchId, cancellationToken).ConfigureAwait(false);
    }

    public async Task CancelEditAsync(string batchId, CancellationToken cancellationToken = default)
    {
        // Nessun controllo di permesso: il diritto a rilasciare deriva dal possedere il blocco,
        // e il confronto su Lock_Usr lo verifica. Un ruolo revocato mentre il lotto e' aperto non
        // deve lasciare il blocco incastrato fino alla scadenza.
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (LockOwner(permissions) is not { } owner)
        {
            return;
        }

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var released = await ReleaseAsync(context, batchId, owner, cancellationToken).ConfigureAwait(false);

        if (released > 0)
        {
            logger.LogInformation("Lotti: {User} ha rilasciato il lotto {Batch}.", owner, batchId);
        }
    }

    public async Task ForceUnlockAsync(string batchId, CancellationToken cancellationToken = default)
    {
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);

        // Non basta poter scrivere la produzione: lo sblocco forzato fa perdere il lavoro di un
        // collega, quindi vuole il ruolo Administrator e non una combinazione di permessi.
        if (!permissions.IsAdministrator)
        {
            throw ProductionException.Forbidden();
        }

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var state = await LockStateAsync(context, batchId, cancellationToken).ConfigureAwait(false);

        var released = await ReleaseAsync(context, batchId, owner: null, cancellationToken)
            .ConfigureAwait(false);

        if (released > 0)
        {
            logger.LogWarning(
                "Lotti: {User} ha forzato lo sblocco del lotto {Batch}, in modifica da {Previous} dal {Ts}.",
                permissions.UserName,
                batchId,
                state.LockUsr,
                state.LockTs);
        }
    }

    /// <summary>
    /// Azzera i tre campi del blocco. Con <paramref name="owner"/> valorizzato agisce solo sul
    /// blocco di quell'utente, senza sullo sblocco forzato.
    /// </summary>
    private static Task<int> ReleaseAsync(
        MesDbContext context,
        string batchId,
        string? owner,
        CancellationToken cancellationToken) =>
        context.Batches
            .Where(b => b.BatchId == batchId)
            .Where(b => owner == null || b.LockUsr == owner)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(b => b.IsLock, false)
                    .SetProperty(b => b.LockUsr, (string?)null)
                    .SetProperty(b => b.LockTs, (DateTime?)null),
                cancellationToken);

    /// <summary>
    /// Stato del lotto rispetto al blocco, senza trascinare la testata intera — e soprattutto
    /// senza i due messaggi di diagnostica, che sono <c>varchar(max)</c>.
    /// </summary>
    private static async Task<LockState> LockStateAsync(
        MesDbContext context,
        string batchId,
        CancellationToken cancellationToken) =>
        await context.Batches
            .AsNoTracking()
            .Where(b => b.BatchId == batchId)
            .Select(b => new LockState(
                b.IsLock,
                b.LockUsr,
                b.LockTs,
                b.IsBatchProcessed,
                b.IsErpImported))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw ProductionException.NotFound();

    /// <summary>
    /// Chi tiene il blocco: l'UPN dell'utente, come deciso l'8 settembre 2026. Nel vecchio
    /// applicativo era <c>utente\NOMEPC</c>, che nel web non significa niente.
    /// </summary>
    private static string? LockOwner(UserPermissions permissions) =>
        !permissions.IsAuthenticated || string.IsNullOrWhiteSpace(permissions.UserName)
            ? null
            : permissions.UserName.Length > LockUserMaxLength
                ? permissions.UserName[..LockUserMaxLength]
                : permissions.UserName;

    private sealed record LockState(
        bool IsLock,
        string? LockUsr,
        DateTime? LockTs,
        bool IsBatchProcessed,
        bool IsErpImported);
}
