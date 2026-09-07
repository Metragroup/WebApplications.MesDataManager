using MesDataManager.Application.Production;
using MesDataManager.Domain.Entities;
using MesDataManager.Tests.Support;

namespace MesDataManager.Tests;

/// <summary>
/// Elenco dati di produzione: i lotti gia' chiusi a pressa e a sega, con i filtri della
/// riconciliazione ERP e il periodo massimo di una settimana.
/// <para>
/// Qui si prova la sola modalita' normale. Quella "dettaglio lunghezza" legge dalla funzione
/// tabellare <c>EF.ufn_BatchByLengthShift</c>, che SQLite non ha: si verifica sul database vero,
/// dove nella settimana dell'8 aprile 2024 i 264 lotti chiusi diventano 366 righe scomposte per
/// lunghezza e turno.
/// </para>
/// </summary>
public sealed class ProductionBatchListTests : IDisposable
{
    private static readonly DateTime Giorno = new(2026, 9, 1);

    private readonly ProductionHarness _harness = new();

    public ProductionBatchListTests()
    {
        _harness.Seed(new Company { CompanyId = "MET1", Description = "Metra" });
        _harness.Seed(Pressa("MP1"), Pressa("MP5"));
    }

    public void Dispose() => _harness.Dispose();

    // ------------------------------------------------------------------ chi entra nell'elenco

    [Fact]
    public async Task Compaiono_solo_i_lotti_chiusi_a_pressa_e_a_sega()
    {
        // Il complemento della pagina "Lotti in corso": li' i lotti con una chiusura mancante,
        // qui quelli conclusi.
        _harness.Seed(
            Lotto("MP1260901080000", Giorno.AddHours(8), pressClosed: true, sawClosed: true),
            Lotto("MP1260901090000", Giorno.AddHours(9), pressClosed: true),
            Lotto("MP1260901100000", Giorno.AddHours(10)));

        var page = await _harness.BatchServiceFor(Users.Reader).GetProductionPageAsync(Query());

        Assert.Equal("MP1260901080000", Assert.Single(page.Rows).BatchId);
    }

    [Fact]
    public async Task Il_periodo_comprende_i_giorni_per_intero()
    {
        // Un lotto che finisce alle 23 del giorno "al" deve rientrare: il filtro sulle date non
        // deve tagliare l'ultimo giorno a mezzanotte.
        _harness.Seed(Lotto("MP1260901230000", Giorno.AddHours(22), pressClosed: true, sawClosed: true));

        var page = await _harness.BatchServiceFor(Users.Reader).GetProductionPageAsync(Query());

        Assert.Equal(1, page.TotalCount);
    }

    // ------------------------------------------------------------------ filtri

    [Fact]
    public async Task Il_tipo_distingue_produzione_e_campionatura()
    {
        _harness.Seed(
            Lotto("MP1260901080000", Giorno.AddHours(8), pressClosed: true, sawClosed: true),
            Lotto("MP1260901090000", Giorno.AddHours(9), pressClosed: true, sawClosed: true, sampling: true));

        var service = _harness.BatchServiceFor(Users.Reader);

        Assert.Equal(2, (await service.GetProductionPageAsync(Query())).TotalCount);

        var produzione = await service.GetProductionPageAsync(Query(type: BatchTypeFilter.Production));
        Assert.Equal("MP1260901080000", Assert.Single(produzione.Rows).BatchId);

        var campionatura = await service.GetProductionPageAsync(Query(type: BatchTypeFilter.Sampling));
        Assert.Equal("MP1260901090000", Assert.Single(campionatura.Rows).BatchId);
    }

    [Fact]
    public async Task Lo_stato_distingue_le_tre_condizioni_di_riconciliazione()
    {
        _harness.Seed(
            Lotto("MP1260901080000", Giorno.AddHours(8), pressClosed: true, sawClosed: true, erpImported: true),
            Lotto("MP1260901090000", Giorno.AddHours(9), pressClosed: true, sawClosed: true, erpMarked: true),
            Lotto("MP1260901100000", Giorno.AddHours(10), pressClosed: true, sawClosed: true));

        var service = _harness.BatchServiceFor(Users.Reader);

        var riconciliate = await service.GetProductionPageAsync(Query(status: BatchReconciliationFilter.Reconciled));
        Assert.Equal("MP1260901080000", Assert.Single(riconciliate.Rows).BatchId);

        // "Da riconciliare" e' segnato ma non ancora importato: e' la coda di lavoro dell'ERP.
        var daRiconciliare = await service.GetProductionPageAsync(Query(status: BatchReconciliationFilter.ToReconcile));
        Assert.Equal("MP1260901090000", Assert.Single(daRiconciliare.Rows).BatchId);

        // "Non riconciliate" comprende anche quelle segnate: guarda solo l'importazione.
        var nonRiconciliate = await service.GetProductionPageAsync(Query(status: BatchReconciliationFilter.NotReconciled));
        Assert.Equal(2, nonRiconciliate.TotalCount);
    }

    [Fact]
    public async Task Lotto_e_matrice_si_cercano_per_frammento()
    {
        _harness.Seed(
            Lotto("MP1260901080000", Giorno.AddHours(8), pressClosed: true, sawClosed: true, dieId: "R22225/1"),
            Lotto("MP5260901090000", Giorno.AddHours(9), pressClosed: true, sawClosed: true, dieId: "AI0972/1"));

        var service = _harness.BatchServiceFor(Users.Reader);

        var perLotto = await service.GetProductionPageAsync(Query(batchId: "2609010900"));
        Assert.Equal("MP5260901090000", Assert.Single(perLotto.Rows).BatchId);

        var perMatrice = await service.GetProductionPageAsync(Query(dieId: "22225"));
        Assert.Equal("MP1260901080000", Assert.Single(perMatrice.Rows).BatchId);
    }

    [Fact]
    public async Task Un_filtro_vuoto_non_e_una_ricerca_di_stringa_vuota()
    {
        _harness.Seed(Lotto("MP1260901080000", Giorno.AddHours(8), pressClosed: true, sawClosed: true));

        var page = await _harness.BatchServiceFor(Users.Reader).GetProductionPageAsync(
            Query(batchId: "   ", dieId: ""));

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Con_una_pressa_il_filtro_esclude_le_altre()
    {
        _harness.Seed(
            Lotto("MP1260901080000", Giorno.AddHours(8), pressClosed: true, sawClosed: true),
            Lotto("MP5260901080000", Giorno.AddHours(8), pressClosed: true, sawClosed: true, pressId: "MP5"));

        var page = await _harness.BatchServiceFor(Users.Reader).GetProductionPageAsync(Query(pressId: "MP5"));

        Assert.Equal("MP5", Assert.Single(page.Rows).PressId);
    }

    // ------------------------------------------------------------------ periodo massimo

    [Fact]
    public async Task Il_periodo_arriva_a_una_settimana()
    {
        var service = _harness.BatchServiceFor(Users.Reader);

        await service.GetProductionPageAsync(Query(to: Giorno.AddDays(6)));

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => service.GetProductionPageAsync(Query(to: Giorno.AddDays(7))));

        Assert.Equal("Error.PeriodTooWide", errore.MessageKey);
        Assert.Equal([ProductionBatchPeriodPolicy.MaxDays], errore.MessageArguments);
    }

    [Fact]
    public async Task La_fine_del_periodo_non_puo_precedere_l_inizio()
    {
        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Users.Reader).GetProductionPageAsync(Query(to: Giorno.AddDays(-1))));

        Assert.Equal("Error.PeriodInvalid", errore.MessageKey);
    }

    // ------------------------------------------------------------------ colonne calcolate

    [Fact]
    public async Task Tempo_di_estrusione_ed_efficienza_si_ricavano_dalla_riga()
    {
        _harness.Seed(Lotto(
            "MP1260901080000",
            Giorno.AddHours(8),
            pressClosed: true,
            sawClosed: true,
            stop: Giorno.AddHours(9).AddMinutes(30),
            kgExtruded: 1000m,
            kgCut: 750m));

        var row = Assert.Single((await _harness.BatchServiceFor(Users.Reader).GetProductionPageAsync(Query())).Rows);

        Assert.Equal(TimeSpan.FromMinutes(90), row.ExtrusionTime);
        Assert.Equal(0.75m, row.Efficiency);
    }

    [Fact]
    public async Task Senza_kg_estrusi_l_efficienza_non_e_zero_ma_indefinita()
    {
        // Zero per cento direbbe "nessuna barra utile", che e' una cosa diversa da "non si sa".
        _harness.Seed(Lotto(
            "MP1260901080000",
            Giorno.AddHours(8),
            pressClosed: true,
            sawClosed: true,
            kgExtruded: 0m,
            kgCut: 0m));

        var row = Assert.Single((await _harness.BatchServiceFor(Users.Reader).GetProductionPageAsync(Query())).Rows);

        Assert.Null(row.Efficiency);
    }

    [Fact]
    public async Task Nella_modalita_normale_i_campi_del_dettaglio_restano_vuoti()
    {
        _harness.Seed(Lotto("MP1260901080000", Giorno.AddHours(8), pressClosed: true, sawClosed: true));

        var row = Assert.Single((await _harness.BatchServiceFor(Users.Reader).GetProductionPageAsync(Query())).Rows);

        Assert.Null(row.BarLength);
        Assert.Null(row.ShiftId);
        Assert.Null(row.ShiftDate);
        Assert.Null(row.AlloyAndTreatment);
    }

    // ------------------------------------------------------------------ ordine, pagine, permessi

    [Fact]
    public async Task Le_righe_arrivano_in_ordine_di_data_e_paginate()
    {
        for (var ora = 0; ora < 8; ora++)
        {
            _harness.Seed(Lotto($"MP1260901{ora:00}0000", Giorno.AddHours(ora), pressClosed: true, sawClosed: true));
        }

        var page = await _harness.BatchServiceFor(Users.Reader).GetProductionPageAsync(
            Query() with { PageNumber = 2, PageSize = 3 });

        Assert.Equal(8, page.TotalCount);
        Assert.Equal(
            ["MP1260901030000", "MP1260901040000", "MP1260901050000"],
            page.Rows.Select(r => r.BatchId));
    }

    [Fact]
    public async Task Chi_non_e_autenticato_non_legge_i_dati_di_produzione()
    {
        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Users.Anonymous).GetProductionPageAsync(Query()));

        Assert.Equal(ProductionErrorKind.Forbidden, errore.Kind);
    }

    // ------------------------------------------------------------------ appoggio

    private static ProductionBatchQuery Query(
        string? pressId = null,
        DateTime? to = null,
        BatchTypeFilter type = BatchTypeFilter.Any,
        BatchReconciliationFilter status = BatchReconciliationFilter.Any,
        string? batchId = null,
        string? dieId = null) =>
        new(pressId, Giorno, to ?? Giorno, type, status, batchId, dieId);

    private static Batch Lotto(
        string batchId,
        DateTime start,
        bool pressClosed = false,
        bool sawClosed = false,
        bool erpImported = false,
        bool erpMarked = false,
        bool sampling = false,
        string pressId = "MP1",
        string? dieId = null,
        DateTime? stop = null,
        decimal? kgExtruded = null,
        decimal? kgCut = null) =>
        new()
        {
            BatchId = batchId,
            PressId = pressId,
            DieId = dieId,
            StartTs = start,
            StopTs = stop ?? start.AddHours(1),
            IsPressClosed = pressClosed,
            IsSawClosed = sawClosed,
            IsErpImported = erpImported,
            IsErpMarked = erpMarked,
            IsSampling = sampling,
            KgExtruded = kgExtruded,
            KgCut = kgCut,
        };

    private static Press Pressa(string pressId) =>
        new()
        {
            PressId = pressId,
            Description = pressId,
            CompanyId = "MET1",
            SawOprId = "SAW",
            SawWrkCtrId = "SAW",
        };
}
