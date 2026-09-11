using MesDataManager.Application.Production;
using MesDataManager.Application.Security;
using MesDataManager.Domain.Entities;
using MesDataManager.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MesDataManager.Infrastructure.Production;

/// <summary>
/// Lotti di estrusione: lettura, blocco, modifica e comandi. La classe e' divisa in file
/// parziali per responsabilita' — blocco, controlli, salvataggio, comandi — e questo tiene le
/// letture.
/// <para>
/// Stesso impianto di <see cref="MachineDowntimeService"/>: un contesto per operazione dalla
/// factory, <c>AsNoTracking</c> e query scritte a mano.
/// </para>
/// <para>
/// Due condizioni del filtro non sono dettagli tecnici ma regole riprese dal vecchio applicativo
/// (<c>RepositoryService.GetPendingBatches</c>): il lotto deve avere almeno una billetta vera —
/// altrimenti e' un lotto fantasma — e non deve essere gia' stato importato in ERP. Rispetto al
/// vecchio codice cambiano due cose, di proposito: la paginazione e' vera al posto di un
/// <c>TOP(10)</c> cablato, e l'ordine e' per data di inizio invece che per <c>BatchID</c>, che
/// cominciando con la sigla della pressa raggruppava per pressa prima che per data.
/// </para>
/// </summary>
public sealed partial class BatchService(
    IDbContextFactory<MesDbContext> contextFactory,
    IUserContext user,
    ILogger<BatchService> logger) : IBatchService
{
    /// <summary>
    /// Lunghezza di <c>Batch.Lock_Usr</c> a database. Un UPN piu' lungo viene troncato invece di
    /// far fallire il blocco: e' un'etichetta da mostrare, non una chiave.
    /// </summary>
    private const int LockUserMaxLength = 50;

    /// <summary>
    /// Quanto si concede alle procedure di ricalcolo del MES. Il valore predefinito
    /// dell'applicazione e' 30 secondi e non basta: <c>usp_Batch_Elab</c> ne impiega circa 35 per
    /// lotto (vedi <see cref="RecalculateAsync"/>). Tre minuti lasciano margine per un lotto con
    /// molte billette senza restare appesi in eterno.
    /// </summary>
    private static readonly TimeSpan RecalculationTimeout = TimeSpan.FromMinutes(3);


    public async Task<BatchPage> GetPageAsync(
        BatchListQuery query,
        CancellationToken cancellationToken = default)
    {
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!permissions.CanRead)
        {
            throw ProductionException.Forbidden();
        }

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var filtered = OpenBatches(context, query.CloseState);

        if (query.PressId is { } pressId)
        {
            filtered = filtered.Where(b => b.PressId == pressId);
        }

        var totalCount = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await filtered
            .OrderByDescending(b => b.StartTs)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(b => new BatchRow(
                b.BatchId,
                b.PressId,
                b.DieId,
                b.BilletCount,
                b.BarCount,
                b.StartTs,
                b.StopTs,
                b.IsPressClosed,
                b.PressClosedTs,
                b.IsSawClosed,
                b.SawClosedTs,
                // L'esito che vale e' quello dell'utente se c'e', altrimenti quello del servizio:
                // i due gruppi di campi non si sovrascrivono a vicenda.
                b.UsrDiagStatus == null
                    ? (b.SvcDiagStatus == null ? null : b.SvcDiagStatus.TrimEnd())
                    : b.UsrDiagStatus.TrimEnd(),
                b.IsLock))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new BatchPage(rows, totalCount);
    }

    public async Task<BatchDetail> GetDetailAsync(
        string batchId,
        CancellationToken cancellationToken = default)
    {
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!permissions.CanRead)
        {
            throw ProductionException.Forbidden();
        }

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // La causale di chiusura e' un join a sinistra: un lotto ancora aperto non ne ha una, e
        // uno storico puo' riferire una causale poi disattivata.
        var header = await (
            from b in context.Batches.AsNoTracking().Where(b => b.BatchId == batchId)
            join r in context.PressBatchClosingReasons
                on b.PressBatchClosingReasonId equals r.PressBatchClosingReasonId into reasonJoin
            from reason in reasonJoin.DefaultIfEmpty()
            select new { Batch = b, ClosingReason = reason == null ? null : reason.Description })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw ProductionException.NotFound();

        var billets = await context.BatchBillets
            .AsNoTracking()
            .Where(x => x.BatchId == batchId && x.TypeId == BatchBilletType.Real)
            .OrderBy(x => x.BilletNo)
            .Select(x => new BatchBilletRow(
                x.BatchBilletId,
                x.BilletNo,
                x.StartTs,
                x.StopTs,
                x.ShiftId == null ? null : x.ShiftId.TrimEnd(),
                x.MmBarSet,
                x.MmBilletAct,
                x.KgExtruded,
                x.Billet1CastingId == null ? null : x.Billet1CastingId.TrimEnd(),
                x.Billet1AlloyId,
                x.Billet1Kg,
                x.Billet2CastingId == null ? null : x.Billet2CastingId.TrimEnd(),
                x.Billet2AlloyId,
                x.Billet2Kg,
                x.ProdId,
                x.EditStatusId == null ? null : x.EditStatusId.TrimEnd()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var batch = header.Batch;

        var adjustments = await AdjustmentsAsync(context, batchId, cancellationToken).ConfigureAwait(false);

        var moduleTransactions = await ModuleTransactionsAsync(
            context,
            batchId,
            batch.PressId,
            cancellationToken).ConfigureAwait(false);

        var productionOrders = await ProductionOrdersAsync(context, batchId, cancellationToken)
            .ConfigureAwait(false);

        var allowAdjustments = await AllowAdjustmentsAsync(context, cancellationToken).ConfigureAwait(false);

        return new BatchDetail(
            batch.BatchId,
            batch.PressId,
            batch.DieId,
            batch.DieCode?.TrimEnd(),
            batch.DieNumber,
            batch.PressBatchClosingReasonId,
            batch.BilletCount,
            billets.Count,
            batch.BarCount,
            header.ClosingReason,
            batch.IsSampling,
            batch.StartTs,
            batch.StopTs,
            batch.IsPressClosed,
            batch.PressClosedTs,
            batch.IsSawClosed,
            batch.SawClosedTs,
            batch.IsBatchProcessed,
            batch.IsErpMarked,
            batch.IsErpImported,
            batch.IsLock,
            batch.LockUsr,
            batch.LockTs,
            batch.EditStatusId?.TrimEnd(),
            batch.KgRaw,
            batch.KgSheared,
            batch.KgExtruded,
            batch.KgCut,
            batch.ItemMeterWeightMasterData,
            batch.ItemMeterWeightMes,
            batch.ItemMeterWeightTest,
            batch.ItemMeterWeight,
            batch.SvcDiagStatus?.TrimEnd(),
            batch.SvcDiagTs,
            batch.SvcDiagMsg,
            batch.SvcDiagJson,
            batch.UsrDiagStatus?.TrimEnd(),
            batch.UsrDiagTs,
            batch.UsrDiagMsg,
            batch.UsrDiagJson,
            billets,
            adjustments,
            moduleTransactions,
            productionOrders,
            allowAdjustments);
    }


    /// <summary>Rettifiche delle barre del lotto, dalla piu' vecchia come nel vecchio applicativo.</summary>
    private static async Task<IReadOnlyList<BatchAdjustmentRow>> AdjustmentsAsync(
        MesDbContext context,
        string batchId,
        CancellationToken cancellationToken) =>
        await context.BatchBarQties
            .AsNoTracking()
            .Where(x => x.BatchId == batchId)
            .OrderBy(x => x.CreatedTs)
            .Select(x => new BatchAdjustmentRow(
                x.BatchBarQtyId,
                x.BarLength,
                x.ProdId == null ? null : x.ProdId.TrimEnd(),
                x.Qty,
                x.CreatedTs))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Transazioni di incestamento del lotto. Numero operazione, operazione e centro di lavoro
    /// vengono dall'<b>ultimo passo lavorato</b> del ciclo della cesta e la quantita' di scarto
    /// dalla somma degli scarti: nessuno dei quattro sta su <c>Module_ModuleTrans</c>. Stessa
    /// composizione di <c>RepositoryService.GetModuleTransDtos</c>, ma in una sola query invece
    /// di tre piu' un ciclo in memoria.
    /// </summary>
    private static async Task<IReadOnlyList<ModuleTransRow>> ModuleTransactionsAsync(
        MesDbContext context,
        string batchId,
        string pressId,
        CancellationToken cancellationToken) =>
        await (
            from t in context.ModuleTransactions.AsNoTracking()
            where t.BatchId == batchId && t.PressId == pressId
            orderby t.CreatedTs, t.ModuleTransId
            let lastRoute = context.ModuleTransRoutes
                .Where(r => r.ModuleTransId == t.ModuleTransId && r.IsProcessed)
                .OrderByDescending(r => r.OprNumPriority)
                .FirstOrDefault()
            select new ModuleTransRow(
                t.ModuleTransId,
                t.ModuleId,
                t.BarLength,
                t.ProdId,
                t.Qty,
                t.CreatedTs,
                lastRoute == null ? null : lastRoute.OprNum,
                lastRoute == null ? null : lastRoute.OprId,
                lastRoute == null ? null : lastRoute.WrkCtrId,
                context.ModuleTransScraps
                    .Where(s => s.ModuleTransId == t.ModuleTransId)
                    .Sum(s => s.Qty)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Ordini di produzione del lotto, su due livelli: prima quelli associati alle billette, e
    /// solo se non ce n'e' nessuno quelli rilasciati sul lotto all'avvio dell'estrusione
    /// (<c>RepositoryService.GetBatchProductionTags</c>).
    /// <para>
    /// I dati anagrafici dell'ordine sono un join a <b>sinistra</b>: un ordine collegato al lotto
    /// ma non presente fra i cartellini dell'ERP deve comunque comparire, col solo numero. La
    /// vecchia scheda lo perdeva, perche' partiva dai cartellini.
    /// </para>
    /// </summary>
    private static async Task<IReadOnlyList<BatchProdOrderRow>> ProductionOrdersAsync(
        MesDbContext context,
        string batchId,
        CancellationToken cancellationToken)
    {
        var prodIds = await context.BatchBilletProdOrders
            .AsNoTracking()
            .Where(o => o.BatchId == batchId && o.ProdId != "")
            .Select(o => o.ProdId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (prodIds.Count == 0)
        {
            prodIds = await context.BatchProdOrders
                .AsNoTracking()
                .Where(o => o.BatchId == batchId && o.ProdId != "")
                .Select(o => o.ProdId)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (prodIds.Count == 0)
        {
            return [];
        }

        // Gli ordini di un lotto sono una manciata: i cartellini si leggono in un colpo e
        // l'abbinamento avviene qui. Un join a sinistra lato database costringerebbe a portare
        // la lista degli ordini nella query, che EF non sa fare come radice.
        var tags = await context.ProductionTags
            .AsNoTracking()
            .Where(t => prodIds.Contains(t.ProdId))
            .ToDictionaryAsync(t => t.ProdId, cancellationToken)
            .ConfigureAwait(false);

        return [.. prodIds
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Select(id => tags.TryGetValue(id, out var tag)
                ? new BatchProdOrderRow(
                    id,
                    tag.CustName,
                    tag.SalesAlloyId,
                    tag.ProdAlloyId,
                    tag.SalesHeatTreatment)
                : new BatchProdOrderRow(id, null, null, null, null))];
    }

    /// <summary>
    /// Se la sega ammette rettifiche, dalla prima societa' attiva come nel vecchio applicativo
    /// (<c>RepositoryService.GetCompany</c>). L'ordinamento e' aggiunto qui: <c>First</c> senza
    /// ordine su piu' societa' attive restituirebbe una riga a caso.
    /// </summary>
    private static async Task<bool> AllowAdjustmentsAsync(
        MesDbContext context,
        CancellationToken cancellationToken)
    {
        var allowed = await context.Companies
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.CompanyId)
            .Select(c => c.SawAllowAdjustments)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return allowed ?? false;
    }

    public async Task<ProductionBatchPage> GetProductionPageAsync(
        ProductionBatchQuery query,
        CancellationToken cancellationToken = default)
    {
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!permissions.CanRead)
        {
            throw ProductionException.Forbidden();
        }

        ProductionBatchPeriodPolicy.Validate(query.From, query.To);

        // Il periodo arriva come coppia di date e copre i giorni per intero, estremi compresi.
        var from = query.From.Date;
        var to = query.To.Date.AddDays(1).AddTicks(-1);

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return query.LengthDetail
            ? await ByLengthAsync(context, query, from, to, cancellationToken).ConfigureAwait(false)
            : await ByBatchAsync(context, query, from, to, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Una riga per lotto, dalla tabella. Sono i lotti conclusi: chiusi sia a pressa sia a sega,
    /// come nel vecchio applicativo — quelli ancora aperti stanno nella pagina "Lotti in corso".
    /// </summary>
    private static async Task<ProductionBatchPage> ByBatchAsync(
        MesDbContext context,
        ProductionBatchQuery query,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        var filtered = context.Batches
            .AsNoTracking()
            .Where(b => b.StartTs >= from && b.StopTs <= to)
            .Where(b => b.IsPressClosed && b.IsSawClosed);

        if (query.PressId is { } pressId)
        {
            filtered = filtered.Where(b => b.PressId == pressId);
        }

        filtered = query.Type switch
        {
            BatchTypeFilter.Production => filtered.Where(b => !b.IsSampling),
            BatchTypeFilter.Sampling => filtered.Where(b => b.IsSampling),
            _ => filtered,
        };

        filtered = query.Status switch
        {
            BatchReconciliationFilter.Reconciled => filtered.Where(b => b.IsErpImported),
            BatchReconciliationFilter.ToReconcile => filtered.Where(b => b.IsErpMarked && !b.IsErpImported),
            BatchReconciliationFilter.NotReconciled => filtered.Where(b => !b.IsErpImported),
            _ => filtered,
        };

        // Ricerca parziale su lotto e matrice, come nel vecchio applicativo ma con i valori
        // passati come parametri: la vecchia query li concatenava nel testo SQL.
        if (Search(query.BatchId) is { } batchSearch)
        {
            filtered = filtered.Where(b => EF.Functions.Like(b.BatchId, batchSearch));
        }

        if (Search(query.DieId) is { } dieSearch)
        {
            filtered = filtered.Where(b => b.DieId != null && EF.Functions.Like(b.DieId, dieSearch));
        }

        var totalCount = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await (
            from b in filtered
            join r in context.PressBatchClosingReasons
                on b.PressBatchClosingReasonId equals r.PressBatchClosingReasonId into reasonJoin
            from reason in reasonJoin.DefaultIfEmpty()
            orderby b.StartTs
            select new ProductionBatchRow(
                b.BatchId,
                b.PressId,
                b.DieId,
                null,
                null,
                null,
                null,
                b.BilletCount,
                b.BarCount,
                b.StartTs,
                b.StopTs,
                reason == null ? null : reason.Description,
                b.KgExtruded,
                b.KgCut,
                b.IsErpMarked,
                b.IsErpImported,
                b.IsLock,
                b.UsrDiagStatus == null ? (b.SvcDiagStatus == null ? null : b.SvcDiagStatus.TrimEnd()) : b.UsrDiagStatus.TrimEnd()))
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ProductionBatchPage(rows, totalCount);
    }

    /// <summary>
    /// Una riga per ogni lunghezza barra e turno, dalla funzione tabellare. Qui il periodo e' un
    /// parametro della funzione e non una condizione, e le due chiusure non si filtrano: le
    /// applica gia' la funzione. Il filtro su <c>StartTs</c> resta come nel vecchio applicativo.
    /// </summary>
    private static async Task<ProductionBatchPage> ByLengthAsync(
        MesDbContext context,
        ProductionBatchQuery query,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        var filtered = context.BatchesByLengthShift(from, to)
            .Where(b => b.StartTs >= from);

        if (query.PressId is { } pressId)
        {
            filtered = filtered.Where(b => b.PressId == pressId);
        }

        filtered = query.Type switch
        {
            BatchTypeFilter.Production => filtered.Where(b => !b.IsSampling),
            BatchTypeFilter.Sampling => filtered.Where(b => b.IsSampling),
            _ => filtered,
        };

        filtered = query.Status switch
        {
            BatchReconciliationFilter.Reconciled => filtered.Where(b => b.IsErpImported),
            BatchReconciliationFilter.ToReconcile => filtered.Where(b => b.IsErpMarked && !b.IsErpImported),
            BatchReconciliationFilter.NotReconciled => filtered.Where(b => !b.IsErpImported),
            _ => filtered,
        };

        if (Search(query.BatchId) is { } batchSearch)
        {
            filtered = filtered.Where(b => EF.Functions.Like(b.BatchId, batchSearch));
        }

        if (Search(query.DieId) is { } dieSearch)
        {
            filtered = filtered.Where(b => b.DieId != null && EF.Functions.Like(b.DieId, dieSearch));
        }

        var totalCount = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await (
            from b in filtered
            join r in context.PressBatchClosingReasons
                on b.PressBatchClosingReasonId equals r.PressBatchClosingReasonId into reasonJoin
            from reason in reasonJoin.DefaultIfEmpty()
            orderby b.StartTs, b.ShiftDate
            select new ProductionBatchRow(
                b.BatchId,
                b.PressId,
                b.DieId,
                b.BarLength,
                b.ShiftId == null ? null : b.ShiftId.TrimEnd(),
                b.ShiftDate,
                b.AlloyAndTreatment,
                b.BilletCount,
                b.BarCount,
                b.StartTs,
                b.StopTs,
                reason == null ? null : reason.Description,
                b.KgExtruded,
                b.KgCut,
                b.IsErpMarked,
                b.IsErpImported,
                b.IsLock,
                b.DiagnosticsStatus == null ? null : b.DiagnosticsStatus.TrimEnd()))
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ProductionBatchPage(rows, totalCount);
    }

    /// <summary>Ricerca parziale: campo vuoto significa nessun filtro, non "contiene niente".</summary>
    private static string? Search(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"%{value.Trim()}%";

    /// <summary>
    /// Lotti non ancora conclusi, con le due condizioni riprese dal vecchio applicativo e le tre
    /// varianti dello stato di chiusura.
    /// </summary>
    private static IQueryable<Batch> OpenBatches(MesDbContext context, BatchCloseState closeState)
    {
        var batches = context.Batches
            .AsNoTracking()
            .Where(b => !b.IsErpImported)
            .Where(b => context.BatchBillets.Any(x =>
                x.BatchId == b.BatchId && x.TypeId == BatchBilletType.Real));

        return closeState switch
        {
            BatchCloseState.Running => batches.Where(b => !b.IsPressClosed && !b.IsSawClosed),
            BatchCloseState.PartiallyClosed => batches.Where(b => b.IsPressClosed != b.IsSawClosed),
            _ => batches.Where(b => !b.IsPressClosed || !b.IsSawClosed),
        };
    }
}
