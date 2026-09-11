using MesDataManager.Application.Production;
using MesDataManager.Application.Security;
using MesDataManager.Domain.Entities;
using MesDataManager.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MesDataManager.Infrastructure.Production;

/// <summary>
/// <see cref="BatchService"/>, i comandi dell'elenco dati di produzione: marcatura per la
/// riconciliazione, creazione, eliminazione e chiusura forzata.
/// </summary>
public sealed partial class BatchService
{
    public async Task<BatchMarkResult> MarkForErpAsync(
        IReadOnlyCollection<string> batchIds,
        bool marked,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batchIds);

        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!permissions.CanEditProduction)
        {
            throw ProductionException.Forbidden();
        }

        if (batchIds.Count == 0)
        {
            return new BatchMarkResult(0, []);
        }

        var ids = batchIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var batches = await context.Batches
            .Where(b => ids.Contains(b.BatchId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var skipped = new List<BatchMarkSkip>();
        var count = 0;

        foreach (var id in ids)
        {
            var batch = batches.FirstOrDefault(b => b.BatchId == id);

            if (batch is null)
            {
                skipped.Add(new BatchMarkSkip(id, BatchMarkSkipReason.NotFound));
                continue;
            }

            if (Skip(batch, marked) is { } reason)
            {
                skipped.Add(new BatchMarkSkip(id, reason));
                continue;
            }

            if (batch.IsErpMarked != marked)
            {
                batch.IsErpMarked = marked;
                count++;
            }
        }

        if (count > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Lotti: {User} ha {Action} {Count} lotti per la riconciliazione, {Skipped} saltati.",
            permissions.UserName,
            marked ? "segnato" : "annullato",
            count,
            skipped.Count);

        return new BatchMarkResult(count, skipped);
    }

    /// <summary>
    /// Perche' un lotto non si puo' segnare. Annullare la marcatura non ha limitazioni: togliere
    /// un lotto dalla coda dell'ERP e' sempre lecito.
    /// <para>
    /// La diagnostica <b>non</b> viene rilanciata: si legge quella che il lotto ha. Il vecchio
    /// applicativo la rieseguiva su ogni riga selezionata, cioe' una chiamata HTTP per lotto.
    /// </para>
    /// </summary>
    private static BatchMarkSkipReason? Skip(Batch batch, bool marked)
    {
        if (!marked)
        {
            return null;
        }

        if (batch.IsErpImported)
        {
            return BatchMarkSkipReason.AlreadyImported;
        }

        if (batch.IsLock)
        {
            return BatchMarkSkipReason.Locked;
        }

        // Vale l'esito corrente: quello dell'utente se la diagnostica e' stata rieseguita da qui,
        // altrimenti quello del servizio. Un lotto che ha solo l'esito del servizio e' a posto —
        // e' il caso normale, ed e' il motivo per cui la marcatura non rilancia la diagnostica.
        if (string.IsNullOrWhiteSpace(batch.CurrentDiagStatus))
        {
            return BatchMarkSkipReason.NoDiagnostics;
        }

        if (batch.CurrentDiagStatus.Trim() == DiagnosticsOutcome.Error)
        {
            return BatchMarkSkipReason.DiagnosticsError;
        }

        return null;
    }

    public async Task<string> CreateAsync(
        NewBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!permissions.CanEditProduction)
        {
            throw ProductionException.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(request.PressId))
        {
            throw ProductionException.Required(nameof(request.PressId));
        }

        if (request.StopTs <= request.StartTs)
        {
            throw ProductionException.PeriodInvalid();
        }

        // La matrice si controlla come nel cambio matrice: esistenza e stato d'uso. Il vecchio
        // applicativo qui verificava solo che il campo non fosse vuoto.
        var die = await ValidateDieAsync(request.DieCode, request.DieNumber, cancellationToken)
            .ConfigureAwait(false);

        if (!die.IsUsable)
        {
            throw ProductionException.InvalidValue(nameof(request.DieCode));
        }

        // Le billette si distribuiscono prima di scrivere qualunque cosa: se i dati sono
        // incoerenti non deve restare a database un lotto senza billette.
        var billets = request.Billets.Plan();

        var batchId = request.BatchId;

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        // Sovrapposizione con altri lotti della stessa pressa: confronto completo fra intervalli.
        // Il controllo del vecchio applicativo (IsBatchPeriodValid) guardava solo se un lotto
        // esistente conteneva l'inizio o la fine del nuovo, e non vedeva il lotto nuovo che ne
        // inghiotte uno esistente — lo stesso difetto gia' corretto sui fermi macchina.
        var overlapping = await context.Batches
            .AsNoTracking()
            .Where(b => b.PressId == request.PressId)
            .Where(b => b.StartTs < request.StopTs && b.StopTs > request.StartTs)
            .Select(b => b.BatchId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (overlapping.Count > 0)
        {
            throw ProductionException.BatchPeriodOverlap(overlapping);
        }

        var batch = new Batch
        {
            BatchId = batchId,
            BatchStatusId = 0,
            PressId = request.PressId,
            DieId = die.DieId,
            DieCode = die.DieCode,
            DieNumber = die.DieNumber,
            StartTs = request.StartTs,
            StopTs = request.StopTs,
            PressBatchClosingReasonId = request.ClosingReasonId,

            // Un lotto inserito a mano nasce chiuso: non e' lavoro in corso, e' la ricostruzione
            // di una produzione che la raccolta dati non ha registrato.
            IsPressClosed = true,
            IsSawClosed = true,
            EditStatusId = "N",
        };

        context.Batches.Add(batch);

        // I due marcatori delimitano il lotto e non sono billette: portano gli istanti degli
        // estremi e nessuna quantita'.
        context.BatchBillets.Add(Marker(batch, BatchBilletType.BatchStart, request.StartTs, null));
        context.BatchBillets.Add(Marker(
            batch,
            BatchBilletType.BatchStop,
            request.StopTs,
            (byte)request.ClosingReasonId));

        foreach (var planned in billets)
        {
            context.BatchBillets.Add(new BatchBillet
            {
                BatchId = batchId,
                PressId = request.PressId,
                DieId = die.DieId,
                TypeId = BatchBilletType.Real,
                BatchBilletRawId = -1,
                BilletNo = planned.BilletNo,
                StartTs = planned.StartTs,
                StopTs = planned.StopTs,
                SecCycle = planned.SecCycle,
                MmBarSet = planned.MmBarSet,
                MmBilletAct = planned.MmBilletAct,
                KgSheared = planned.KgSheared,
                KgExtruded = planned.KgExtruded,
                Billet1CastingId = planned.CastingId,
                Billet1AlloyId = planned.AlloyId,
                Billet1Kg = planned.KgSheared,
                ProdId = planned.ProdId,
                EditStatusId = "N",
            });
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Lotti: creazione del lotto {Batch} non riuscita.", batchId);
            throw TranslateSaveFailure(ex);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Lotti: {User} ha creato il lotto {Batch} con {Billets} billette.",
            permissions.UserName,
            batchId,
            billets.Count);

        await RecalculateAsync(batchId, cancellationToken).ConfigureAwait(false);

        return batchId;
    }

    /// <summary>Marcatore di apertura o chiusura del lotto: stesso istante come inizio e fine.</summary>
    private static BatchBillet Marker(Batch batch, byte typeId, DateTime instant, byte? closingReasonId) =>
        new()
        {
            BatchId = batch.BatchId,
            PressId = batch.PressId,
            DieId = batch.DieId ?? string.Empty,
            TypeId = typeId,
            BatchBilletRawId = -1,
            BilletNo = 0,
            StartTs = instant,
            StopTs = instant,
            SecCycle = 0,
            ClosingReasonId = closingReasonId,
            EditStatusId = "N",
        };

    public async Task DeleteAsync(string batchId, CancellationToken cancellationToken = default)
    {
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!permissions.CanEditProduction)
        {
            throw ProductionException.Forbidden();
        }

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var batch = await context.Batches
            .SingleOrDefaultAsync(b => b.BatchId == batchId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw ProductionException.NotFound();

        // Un lotto gia' passato all'ERP non si elimina: la' e' diventato un documento, e
        // cancellarlo qui lascerebbe i due sistemi a raccontare cose diverse. Il vecchio
        // applicativo lo permetteva.
        if (batch.IsErpImported)
        {
            throw ProductionException.BatchAlreadyReconciled();
        }

        if (batch.IsLock && batch.LockUsr != LockOwner(permissions))
        {
            throw ProductionException.BatchLocked(batch.LockUsr, batch.LockTs);
        }

        // La cascata e' a mano: a database non ci sono ON DELETE CASCADE, e le presenze degli
        // operatori non erano nemmeno modellate nel vecchio applicativo — restavano orfane.
        await context.BatchBilletProdOrders
            .Where(o => o.BatchId == batchId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await context.BatchProdOrders
            .Where(o => o.BatchId == batchId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await context.BatchBarQties
            .Where(a => a.BatchId == batchId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await context.BatchWorkers
            .Where(w => w.BatchId == batchId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await context.BatchBillets
            .Where(b => b.BatchId == batchId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await context.Batches
            .Where(b => b.BatchId == batchId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await NotifyErpDeletionAsync(context, batchId, cancellationToken).ConfigureAwait(false);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        logger.LogWarning(
            "Lotti: {User} ha eliminato il lotto {Batch}.",
            permissions.UserName,
            batchId);
    }

    /// <summary>
    /// Mette in coda il messaggio che avvisa l'ERP dell'eliminazione
    /// (<c>ERP.usp_SendAsyncMessage</c>, classe <c>NPOPackingManager</c>, metodo
    /// <c>processBatchDeletion</c>).
    /// <para>
    /// Sta <b>dentro</b> la transazione dell'eliminazione, e non prima: e' l'unica azione
    /// irreversibile verso l'esterno del modulo, e non deve poter partire per un lotto che poi
    /// resta a database.
    /// </para>
    /// <para>
    /// Il formato XML e' quello dei 294 messaggi gia' presenti sul database di test: nessuna
    /// dichiarazione, un nodo <c>Message</c> con tre figli. Il valore e' un numero di lotto
    /// (<c>char(15)</c> di lettere e cifre), quindi non c'e' niente da mettere in escape.
    /// </para>
    /// </summary>
    private async Task NotifyErpDeletionAsync(
        MesDbContext context,
        string batchId,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsSqlServer())
        {
            logger.LogDebug(
                "Lotti: messaggio ERP per il lotto {Batch} non inviato, il provider non e' SQL Server.",
                batchId);
            return;
        }

        // La societa' e' quella che il vecchio applicativo passava come DataAreaID: la prima
        // attiva, qui con un ordinamento perche' "la prima" senza ordine non significa niente.
        var dataAreaId = await context.Companies
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.CompanyId)
            .Select(c => c.CompanyId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(dataAreaId))
        {
            // Senza societa' attiva non si sa a chi mandarlo. Il lotto si elimina comunque, come
            // faceva il vecchio applicativo, ma il fatto va registrato.
            logger.LogError(
                "Lotti: nessuna societa' attiva, il messaggio ERP per il lotto {Batch} non e' partito.",
                batchId);
            return;
        }

        var xml = "<Message>" +
                  "<ClassName>NPOPackingManager</ClassName>" +
                  "<MethodName>processBatchDeletion</MethodName>" +
                  $"<_lotId>{batchId.Trim()}</_lotId>" +
                  "</Message>";

        await context.Database
            .ExecuteSqlAsync(
                $"EXEC ERP.usp_SendAsyncMessage @TypeID = {1}, @DataAreaID = {dataAreaId.Trim()}, @XmlMsg = {xml}",
                cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Lotti: messaggio di eliminazione del lotto {Batch} accodato per l'ERP ({DataArea}).",
            batchId,
            dataAreaId.Trim());
    }

    public async Task ForceCloseAsync(
        string batchId,
        bool press,
        bool saw,
        CancellationToken cancellationToken = default)
    {
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!permissions.CanEditProduction)
        {
            throw ProductionException.Forbidden();
        }

        if (!press && !saw)
        {
            return;
        }

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var state = await LockStateAsync(context, batchId, cancellationToken).ConfigureAwait(false);

        if (state.IsErpImported)
        {
            throw ProductionException.BatchAlreadyReconciled();
        }

        if (state.IsLock && state.LockUsr != LockOwner(permissions))
        {
            throw ProductionException.BatchLocked(state.LockUsr, state.LockTs);
        }

        if (!context.Database.IsSqlServer())
        {
            logger.LogDebug(
                "Lotti: chiusura forzata del lotto {Batch} non eseguita, il provider non e' SQL Server.",
                batchId);
            return;
        }

        // Le due chiusure sono procedure del MES: questa applicazione decide quando chiamarle,
        // non cosa facciano.
        if (press)
        {
            await context.Database
                .ExecuteSqlAsync($"EXEC Press.usp_Batch_PressClose @batchId = {batchId}", cancellationToken)
                .ConfigureAwait(false);
        }

        if (saw)
        {
            await context.Database
                .ExecuteSqlAsync($"EXEC Press.usp_Batch_SawClose @batchId = {batchId}", cancellationToken)
                .ConfigureAwait(false);
        }

        logger.LogWarning(
            "Lotti: {User} ha forzato la chiusura del lotto {Batch} (pressa={Press}, sega={Saw}).",
            permissions.UserName,
            batchId,
            press,
            saw);
    }

    /// <summary>
    /// Traduce un errore di salvataggio in un messaggio che significhi qualcosa. La chiave
    /// duplicata e' il caso reale: due lotti creati nello stesso secondo sulla stessa pressa
    /// hanno lo stesso numero.
    /// </summary>
    private static ProductionException TranslateSaveFailure(DbUpdateException exception) =>
        exception.InnerException?.Message is { } message &&
        (message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase))
            ? ProductionException.BatchAlreadyExists()
            : ProductionException.SaveFailed(exception);

    /// <summary>
    /// Testata: matrice e causale di chiusura.
    /// <para>
    /// La matrice si propaga a <b>tutte</b> le billette, marcatori compresi — il vecchio
    /// applicativo li lasciava indietro perche' passava da <c>GetBillets</c>, che filtra i tipi 0
    /// e 2. La causale si scrive anche sul marcatore di chiusura, che ne porta una copia.
    /// </para>
    /// </summary>
}
