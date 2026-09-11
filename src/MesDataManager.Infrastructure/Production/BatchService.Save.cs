using MesDataManager.Application.Production;
using MesDataManager.Application.Security;
using MesDataManager.Domain.Entities;
using MesDataManager.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MesDataManager.Infrastructure.Production;

/// <summary>
/// <see cref="BatchService"/>, salvataggio delle modifiche in sospeso: una transazione per
/// testata, billette, marcatori e rettifiche, e il ricalcolo del MES <b>dopo</b> il commit.
/// </summary>
public sealed partial class BatchService
{
    public async Task<BatchSaveResult> SaveAsync(
        BatchEditModel edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!permissions.CanEditProduction)
        {
            throw ProductionException.Forbidden();
        }

        var owner = LockOwner(permissions) ?? throw ProductionException.Forbidden();
        var batchId = edit.BatchId;

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Una transazione sola per tutto. Se il contesto ne ha gia' una — e' il caso delle prove
        // sul database vero, che girano dentro una transazione poi annullata — ci si aggancia
        // invece di aprirne un'altra, che SQL Server rifiuterebbe.
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var batch = await context.Batches
            .SingleOrDefaultAsync(b => b.BatchId == batchId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw ProductionException.NotFound();

        // Il blocco si ricontrolla adesso e non si crede a quello che il circuito ricorda: nel
        // frattempo un amministratore puo' averlo forzato, o la scadenza puo' averlo liberato e
        // un collega preso.
        if (!batch.IsLock || batch.LockUsr != owner)
        {
            throw ProductionException.BatchLockLost(batch.LockUsr);
        }

        if (batch.IsErpImported)
        {
            throw ProductionException.BatchAlreadyReconciled();
        }

        var billets = await context.BatchBillets
            .Where(b => b.BatchId == batchId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        ApplyHeader(edit, batch, billets);
        ApplyBillets(edit, batch, billets, context);
        await ApplyAdjustmentsAsync(edit, batch, context, cancellationToken).ConfigureAwait(false);
        await ApplyDiagnosticsClosureAsync(batch, context, cancellationToken).ConfigureAwait(false);

        // Il blocco si rilascia nella stessa transazione: un salvataggio riuscito che lasciasse
        // il lotto bloccato sarebbe indistinguibile da un blocco orfano.
        batch.IsLock = false;
        batch.LockUsr = null;
        batch.LockTs = null;

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Lotti: salvataggio del lotto {Batch} non riuscito.", batchId);
            throw ProductionException.SaveFailed(ex);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Lotti: {User} ha salvato il lotto {Batch} ({Billets} billette, {Deleted} rimosse).",
            owner,
            batchId,
            edit.Billets.Count,
            edit.DeletedBilletIds.Count);

        // Il ricalcolo sta FUORI dalla transazione, dopo il commit. Misurato su MES40_RDP_TEST il
        // 9 settembre 2026: usp_Batch_Elab impiega circa 35 secondi per lotto, stabilmente e su
        // qualunque lotto. Tenere aperta una transazione di scrittura su Press.Batch e
        // Press.BatchBillet per quaranta secondi bloccherebbe la raccolta dati del MES, che su
        // quelle tabelle scrive di continuo: l'atomicita' non vale quel prezzo, ed e' il motivo
        // per cui anche il vecchio applicativo le chiamava dopo il salvataggio.
        var recalculated = await RecalculateAsync(batchId, cancellationToken).ConfigureAwait(false);

        // Rilettura obbligatoria: i valori di riepilogo li ha appena riscritti la procedura.
        var detail = await GetDetailAsync(batchId, cancellationToken).ConfigureAwait(false);

        return new BatchSaveResult(detail, recalculated);
    }

    /// <summary>
    /// Testata: matrice e causale di chiusura.
    /// <para>
    /// La matrice si propaga a <b>tutte</b> le billette, marcatori compresi — il vecchio
    /// applicativo li lasciava indietro perche' passava da <c>GetBillets</c>, che filtra i tipi 0
    /// e 2. La causale si scrive anche sul marcatore di chiusura, che ne porta una copia.
    /// </para>
    /// </summary>
    private static void ApplyHeader(BatchEditModel edit, Batch batch, List<BatchBillet> billets)
    {
        if (edit.DieChanged && edit.DieId is { Length: > 0 } dieId)
        {
            batch.DieId = dieId;
            batch.DieCode = edit.DieCode;
            batch.DieNumber = edit.DieNumber;

            foreach (var billet in billets)
            {
                billet.DieId = dieId;
            }
        }

        if (edit.ClosingReasonChanged)
        {
            batch.PressBatchClosingReasonId = edit.ClosingReasonId;

            var stopMarker = billets.FirstOrDefault(b => b.TypeId == BatchBilletType.BatchStop);
            if (stopMarker is not null)
            {
                stopMarker.ClosingReasonId = edit.ClosingReasonId is { } id ? (byte)id : null;
            }
        }
    }

    /// <summary>
    /// Billette: cancellazioni, modifiche, inserimenti, e infine il riallineamento dei marcatori.
    /// </summary>
    private static void ApplyBillets(
        BatchEditModel edit,
        Batch batch,
        List<BatchBillet> billets,
        MesDbContext context)
    {
        // Le cancellazioni si filtrano sul lotto: una chiave arrivata dal circuito non deve poter
        // cancellare la billetta di un altro lotto.
        foreach (var id in edit.DeletedBilletIds)
        {
            var toDelete = billets.FirstOrDefault(b => b.BatchBilletId == id);
            if (toDelete is not null)
            {
                context.BatchBillets.Remove(toDelete);
                billets.Remove(toDelete);
            }
        }

        foreach (var pending in edit.Billets)
        {
            if (pending.Id is { } id)
            {
                var existing = billets.FirstOrDefault(b => b.BatchBilletId == id);
                if (existing is null || !pending.IsModified)
                {
                    continue;
                }

                Write(pending, existing);
                existing.EditStatusId = pending.EditStatusId;
                continue;
            }

            var created = new BatchBillet
            {
                BatchId = batch.BatchId,
                PressId = batch.PressId,
                DieId = edit.DieId ?? batch.DieId ?? string.Empty,
                TypeId = BatchBilletType.Real,

                // Le billette inserite a mano non vengono dalla raccolta dati: -1 e' il valore
                // con cui il vecchio applicativo le distingueva.
                BatchBilletRawId = -1,
                EditStatusId = pending.EditStatusId,
            };

            Write(pending, created);
            context.BatchBillets.Add(created);
            billets.Add(created);
        }

        RealignMarkers(billets);
    }

    /// <summary>
    /// Copia i valori in sospeso sull'entita', calcolando cio' che e' derivato: i kg cesoiati
    /// sono la somma dei due tronconi (<c>CalcBilletsKgSheared</c>) e i secondi di ciclo la
    /// durata della billetta.
    /// </summary>
    private static void Write(BatchBilletEdit pending, BatchBillet target)
    {
        target.BilletNo = pending.BilletNo;
        target.StartTs = pending.StartTs;
        target.StopTs = pending.StopTs;
        target.MmBarSet = pending.MmBarSet;
        target.MmBilletAct = pending.MmBilletAct;
        target.KgExtruded = pending.KgExtruded;
        target.Billet1CastingId = pending.Billet1CastingId;
        target.Billet1AlloyId = pending.Billet1AlloyId;
        target.Billet1Kg = pending.Billet1Kg;
        target.Billet2CastingId = pending.Billet2CastingId;
        target.Billet2AlloyId = pending.Billet2AlloyId;
        target.Billet2Kg = pending.Billet2Kg;
        target.ProdId = pending.ProdId;
        target.KgSheared = pending.KgSheared;

        target.SecCycle = pending is { StartTs: { } start, StopTs: { } stop } && stop > start
            ? (int)(stop - start).TotalSeconds
            : 0;
    }

    /// <summary>
    /// Riallinea i marcatori di apertura e chiusura del lotto agli istanti della prima e
    /// dell'ultima billetta (<c>BatchesPresenter.CheckBillets</c>).
    /// <para>
    /// I marcatori non sono billette: portano gli istanti della testata, e se una modifica
    /// allunga il lotto all'inizio o alla fine vanno seguiti — altrimenti il lotto risulta
    /// cominciato dopo la sua prima billetta.
    /// </para>
    /// <para>
    /// Due differenze dal vecchio applicativo, entrambe per robustezza: la prima billetta e'
    /// quella col numero piu' basso e non quella numerata 1, e l'allineamento avviene quando i
    /// valori differiscono invece che quando la griglia ha segnalato una modifica.
    /// </para>
    /// </summary>
    private static void RealignMarkers(List<BatchBillet> billets)
    {
        var real = billets.Where(b => b.TypeId == BatchBilletType.Real).ToList();
        if (real.Count == 0)
        {
            return;
        }

        var first = real.OrderBy(b => b.BilletNo).First();
        var last = real.OrderBy(b => b.BilletNo).Last();

        Align(billets.FirstOrDefault(b => b.TypeId == BatchBilletType.BatchStart), first.StartTs);
        Align(billets.FirstOrDefault(b => b.TypeId == BatchBilletType.BatchStop), last.StopTs);

        static void Align(BatchBillet? marker, DateTime? instant)
        {
            if (marker is null || instant is null)
            {
                return;
            }

            if (marker.StartTs == instant && marker.StopTs == instant)
            {
                return;
            }

            // Il marcatore porta lo stesso istante come inizio e come fine: non ha durata.
            marker.StartTs = instant;
            marker.StopTs = instant;
            marker.EditStatusId = "N";
        }
    }

    /// <summary>Rettifiche delle barre: cancellazioni, modifiche e inserimenti.</summary>
    private static async Task ApplyAdjustmentsAsync(
        BatchEditModel edit,
        Batch batch,
        MesDbContext context,
        CancellationToken cancellationToken)
    {
        if (!edit.AdjustmentsChanged)
        {
            return;
        }

        var existing = await context.BatchBarQties
            .Where(a => a.BatchId == batch.BatchId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var id in edit.DeletedAdjustmentIds)
        {
            var toDelete = existing.FirstOrDefault(a => a.BatchBarQtyId == id);
            if (toDelete is not null)
            {
                context.BatchBarQties.Remove(toDelete);
            }
        }

        foreach (var pending in edit.Adjustments)
        {
            if (pending.Id is { } id)
            {
                var row = existing.FirstOrDefault(a => a.BatchBarQtyId == id);
                if (row is null || !pending.IsModified)
                {
                    continue;
                }

                row.BarLength = pending.BarLength;
                row.ProdId = pending.ProdId;
                row.Qty = pending.Qty;
                continue;
            }

            context.BatchBarQties.Add(new BatchBarQty
            {
                BatchId = batch.BatchId,
                PressId = batch.PressId,
                BarLength = pending.BarLength,
                ProdId = pending.ProdId,
                Qty = pending.Qty,
                CreatedTs = pending.CreatedTs,
            });
        }
    }

    /// <summary>
    /// Sulle presse senza MES l'esito della diagnostica decide le due chiusure
    /// (<c>RepositoryService.SetBatchDiagnosticsStatus</c>).
    /// <para>
    /// Nel vecchio applicativo capitava a ogni esecuzione della diagnostica; qui avviene al
    /// salvataggio del lotto, come deciso l'8 settembre 2026: e' il momento in cui il lotto ha la
    /// forma definitiva, e una diagnostica di prova non deve poter chiudere un lotto.
    /// </para>
    /// </summary>
    private static async Task ApplyDiagnosticsClosureAsync(
        Batch batch,
        MesDbContext context,
        CancellationToken cancellationToken)
    {
        // Conta l'esito corrente: quello dell'utente se ha rieseguito la diagnostica, altrimenti
        // quello del servizio.
        if (string.IsNullOrWhiteSpace(batch.CurrentDiagStatus))
        {
            return;
        }

        var hasMes = await context.Presses
            .AsNoTracking()
            .Where(p => p.PressId == batch.PressId)
            .Select(p => p.HasMes)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (hasMes)
        {
            return;
        }

        var closed = batch.CurrentDiagStatus.Trim() == DiagnosticsOutcome.Ok;
        batch.IsPressClosed = closed;
        batch.IsSawClosed = closed;
    }

    /// <summary>
    /// Richiama le procedure del MES che ricalcolano il lotto e riallineano i log di pesatura.
    /// Restituisce falso se il ricalcolo non e' andato a buon fine.
    /// <para>
    /// I valori di riepilogo — pesi, conteggi, tempi di ciclo — appartengono alle procedure di
    /// raccolta dati dello SCADA: questa applicazione scrive solo cio' che l'operatore ha
    /// modificato e poi le richiama, come faceva il vecchio applicativo.
    /// </para>
    /// <para>
    /// <b>Il tempo di esecuzione va dichiarato:</b> misurato su <c>MES40_RDP_TEST</c> il 9
    /// settembre 2026, <c>usp_Batch_Elab</c> impiega circa 35 secondi per lotto — 34,2s, 36,8s e
    /// 36,7s su tre esecuzioni, su lotti diversi e a cache calda. Il timeout predefinito dei
    /// comandi di questa applicazione e' 30 secondi, quindi <b>senza</b> il timeout esplicito qui
    /// sotto il ricalcolo fallirebbe sempre, e nessun test su SQLite lo avrebbe rivelato.
    /// </para>
    /// <para>
    /// Un fallimento del ricalcolo non annulla il salvataggio, che a questo punto e' gia'
    /// confermato: i dati dell'operatore sono a database e i valori di riepilogo restano quelli
    /// di prima, fino alla prossima elaborazione. E' un dato incoerente ma non perduto, e va
    /// detto a chi ha salvato invece di essere nascosto.
    /// </para>
    /// <para>
    /// Le procedure non esistono su SQLite, dove girano i test: la loro assenza non e' un errore.
    /// Cio' che fanno appartiene al database e si verifica la', con la sonda descritta in
    /// <c>docs/piano-modifica-lotto.md</c>.
    /// </para>
    /// </summary>
    private async Task<bool> RecalculateAsync(string batchId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!context.Database.IsSqlServer())
        {
            logger.LogDebug(
                "Lotti: ricalcolo del lotto {Batch} non eseguito, il provider non e' SQL Server.",
                batchId);
            return true;
        }

        context.Database.SetCommandTimeout(RecalculationTimeout);

        try
        {
            await context.Database
                .ExecuteSqlAsync($"EXEC Press.usp_Batch_Elab @BatchID = {batchId}", cancellationToken)
                .ConfigureAwait(false);

            await context.Database
                .ExecuteSqlAsync(
                    $"EXEC Press.usp_LogScaleImportUpdateByBatchID @batchId = {batchId}",
                    cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Lotti: il ricalcolo del lotto {Batch} non e' riuscito. Le modifiche sono salvate, " +
                "i valori di riepilogo restano quelli precedenti.",
                batchId);

            return false;
        }
    }

    /// <summary>
    /// Azzera i tre campi del blocco. Con <paramref name="owner"/> valorizzato agisce solo sul
    /// blocco di quell'utente, senza sullo sblocco forzato.
    /// </summary>
}
