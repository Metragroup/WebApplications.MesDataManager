using MesDataManager.Application.Production;
using MesDataManager.Domain.Entities;
using MesDataManager.Tests.Support;

namespace MesDataManager.Tests;

/// <summary>
/// Regole di lettura dei lotti aperti. Due condizioni del filtro sono regole di business riprese
/// dal vecchio applicativo — almeno una billetta vera e lotto non ancora importato in ERP — e due
/// comportamenti sono invece correzioni deliberate: paginazione al posto del <c>TOP(10)</c>
/// cablato e ordine per data invece che per chiave.
/// </summary>
public sealed class BatchServiceTests : IDisposable
{
    private static readonly DateTime Giorno = new(2026, 9, 1);

    private readonly ProductionHarness _harness = new();

    public BatchServiceTests()
    {
        // Le presse vanno seminate: la chiave esterna verso MasterData.Press esiste davvero a
        // database, e SQLite la fa rispettare come SQL Server.
        _harness.Seed(new Company { CompanyId = "MET1", Description = "Metra" });
        _harness.Seed(Pressa("MP1"), Pressa("MP5"));
    }

    public void Dispose() => _harness.Dispose();

    // ------------------------------------------------------------------ chi entra nell'elenco

    [Fact]
    public async Task Un_lotto_senza_billette_vere_non_compare()
    {
        // I marcatori di apertura e chiusura non sono billette: un lotto che ha solo quelli e'
        // un lotto fantasma, e il vecchio applicativo lo escludeva con un EXISTS.
        _harness.Seed(Lotto("MP1260901080000", "MP1", Giorno.AddHours(8)));
        _harness.Seed(
            Billetta("MP1260901080000", 0, BatchBilletType.BatchStart),
            Billetta("MP1260901080000", 0, BatchBilletType.BatchStop));

        var page = await _harness.BatchServiceFor(Users.Reader).GetPageAsync(Query());

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Un_lotto_con_una_billetta_vera_compare()
    {
        SeedLotto("MP1260901080000", "MP1", Giorno.AddHours(8));

        var page = await _harness.BatchServiceFor(Users.Reader).GetPageAsync(Query());

        Assert.Equal("MP1260901080000", Assert.Single(page.Rows).BatchId);
    }

    [Fact]
    public async Task Un_lotto_gia_riconciliato_non_compare()
    {
        // IsErpImported e' lo stato terminale: il lotto e' passato all'ERP e non e' piu' lavoro
        // in corso, qualunque sia lo stato delle due chiusure.
        SeedLotto("MP1260901080000", "MP1", Giorno.AddHours(8), erpImported: true);

        var page = await _harness.BatchServiceFor(Users.Reader).GetPageAsync(Query());

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Un_lotto_chiuso_a_pressa_e_a_sega_non_e_in_corso()
    {
        SeedLotto("MP1260901080000", "MP1", Giorno.AddHours(8), pressClosed: true, sawClosed: true);

        var page = await _harness.BatchServiceFor(Users.Reader).GetPageAsync(Query());

        Assert.Equal(0, page.TotalCount);
    }

    // ------------------------------------------------------------------ stato di chiusura

    [Fact]
    public async Task Lo_stato_distingue_i_lotti_in_corso_da_quelli_parzialmente_chiusi()
    {
        SeedLotto("MP1260901080000", "MP1", Giorno.AddHours(8));
        SeedLotto("MP1260901090000", "MP1", Giorno.AddHours(9), pressClosed: true);
        SeedLotto("MP1260901100000", "MP1", Giorno.AddHours(10), sawClosed: true);

        var service = _harness.BatchServiceFor(Users.Reader);

        Assert.Equal(3, (await service.GetPageAsync(Query())).TotalCount);

        var inCorso = await service.GetPageAsync(Query(state: BatchCloseState.Running));
        Assert.Equal("MP1260901080000", Assert.Single(inCorso.Rows).BatchId);

        var parziali = await service.GetPageAsync(Query(state: BatchCloseState.PartiallyClosed));
        Assert.Equal(2, parziali.TotalCount);
    }

    // ------------------------------------------------------------------ pressa, ordine, pagine

    [Fact]
    public async Task Con_una_pressa_il_filtro_esclude_le_altre()
    {
        SeedLotto("MP1260901080000", "MP1", Giorno.AddHours(8));
        SeedLotto("MP5260901080000", "MP5", Giorno.AddHours(8));

        var page = await _harness.BatchServiceFor(Users.Reader).GetPageAsync(Query(pressId: "MP5"));

        Assert.Equal("MP5", Assert.Single(page.Rows).PressId);
    }

    [Fact]
    public async Task L_ordine_e_per_data_e_non_per_chiave()
    {
        // La chiave comincia con la sigla della pressa, quindi l'ordine per chiave del vecchio
        // applicativo raggruppava per pressa: con questi tre lotti avrebbe messo davanti MP5,
        // che e' il meno recente.
        SeedLotto("MP5260901080000", "MP5", Giorno.AddHours(8));
        SeedLotto("MP1260901120000", "MP1", Giorno.AddHours(12));
        SeedLotto("MP1260901100000", "MP1", Giorno.AddHours(10));

        var page = await _harness.BatchServiceFor(Users.Reader).GetPageAsync(Query());

        Assert.Equal(
            ["MP1260901120000", "MP1260901100000", "MP5260901080000"],
            page.Rows.Select(r => r.BatchId));
    }

    [Fact]
    public async Task Le_righe_arrivano_paginate_col_totale_completo()
    {
        // Il vecchio applicativo mostrava dieci righe cablate, senza dirlo a nessuno.
        for (var ora = 0; ora < 12; ora++)
        {
            SeedLotto($"MP1260901{ora:00}0000", "MP1", Giorno.AddHours(ora));
        }

        var page = await _harness.BatchServiceFor(Users.Reader).GetPageAsync(
            Query() with { PageNumber = 2, PageSize = 5 });

        Assert.Equal(12, page.TotalCount);
        Assert.Equal(5, page.Rows.Count);
        Assert.Equal("MP1260901060000", page.Rows[0].BatchId);
    }

    // ------------------------------------------------------------------ dettaglio

    [Fact]
    public async Task Il_dettaglio_elenca_solo_le_billette_vere_in_ordine_di_numero()
    {
        _harness.Seed(Lotto("MP1260901080000", "MP1", Giorno.AddHours(8), billetCount: 3));
        _harness.Seed(
            Billetta("MP1260901080000", 0, BatchBilletType.BatchStart),
            Billetta("MP1260901080000", 3),
            Billetta("MP1260901080000", 1),
            Billetta("MP1260901080000", 2),
            Billetta("MP1260901080000", 0, BatchBilletType.BatchStop));

        var detail = await _harness.BatchServiceFor(Users.Reader).GetDetailAsync("MP1260901080000");

        Assert.Equal([1, 2, 3], detail.Billets.Select(b => (int)b.BilletNo));

        // La scheda mostra "billette vere su dichiarate": i due marcatori non contano.
        Assert.Equal(3, detail.RealBilletCount);
        Assert.Equal((short)3, detail.BilletCount);
    }

    [Fact]
    public async Task Il_dettaglio_risolve_la_causale_di_chiusura()
    {
        _harness.Seed(new PressBatchClosingReason
        {
            PressBatchClosingReasonId = 7,
            Description = "Fine ordine",
            Result = "OK",
            IsActive = true,
            IsActiveMaster = true,
        });
        SeedLotto("MP1260901080000", "MP1", Giorno.AddHours(8), closingReasonId: 7);

        var detail = await _harness.BatchServiceFor(Users.Reader).GetDetailAsync("MP1260901080000");

        Assert.Equal("Fine ordine", detail.ClosingReasonDescription);
    }

    [Fact]
    public async Task Un_lotto_ancora_aperto_non_ha_causale_di_chiusura()
    {
        SeedLotto("MP1260901080000", "MP1", Giorno.AddHours(8));

        var detail = await _harness.BatchServiceFor(Users.Reader).GetDetailAsync("MP1260901080000");

        Assert.Null(detail.ClosingReasonDescription);
    }

    [Fact]
    public async Task Un_lotto_inesistente_non_ha_dettaglio()
    {
        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Users.Reader).GetDetailAsync("MP1999999999999"));

        Assert.Equal(ProductionErrorKind.NotFound, errore.Kind);
    }

    // ------------------------------------------------------------------ permessi

    [Fact]
    public async Task Chi_non_e_autenticato_non_legge_i_lotti()
    {
        var elenco = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Users.Anonymous).GetPageAsync(Query()));
        Assert.Equal(ProductionErrorKind.Forbidden, elenco.Kind);

        var dettaglio = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Users.Anonymous).GetDetailAsync("MP1260901080000"));
        Assert.Equal(ProductionErrorKind.Forbidden, dettaglio.Kind);
    }

    [Fact]
    public async Task Il_redattore_delle_anagrafiche_puo_consultare_i_lotti()
    {
        // La lettura non si divide per ambito: qualunque ruolo noto consulta.
        SeedLotto("MP1260901080000", "MP1", Giorno.AddHours(8));

        var page = await _harness.BatchServiceFor(Users.Writer).GetPageAsync(Query());

        Assert.Equal(1, page.TotalCount);
    }

    // ------------------------------------------------------------------ appoggio

    private static BatchListQuery Query(
        string? pressId = null,
        BatchCloseState state = BatchCloseState.Any) =>
        new(pressId, state);

    /// <summary>Lotto con una billetta vera: il minimo per comparire nell'elenco.</summary>
    private void SeedLotto(
        string batchId,
        string pressId,
        DateTime start,
        bool pressClosed = false,
        bool sawClosed = false,
        bool erpImported = false,
        short? closingReasonId = null)
    {
        _harness.Seed(Lotto(
            batchId,
            pressId,
            start,
            pressClosed: pressClosed,
            sawClosed: sawClosed,
            erpImported: erpImported,
            closingReasonId: closingReasonId));

        _harness.Seed(Billetta(batchId, 1));
    }

    private static Batch Lotto(
        string batchId,
        string pressId,
        DateTime start,
        bool pressClosed = false,
        bool sawClosed = false,
        bool erpImported = false,
        short? billetCount = 1,
        short? closingReasonId = null) =>
        new()
        {
            BatchId = batchId,
            PressId = pressId,
            StartTs = start,
            StopTs = start.AddHours(1),
            BilletCount = billetCount,
            IsPressClosed = pressClosed,
            IsSawClosed = sawClosed,
            IsErpImported = erpImported,
            PressBatchClosingReasonId = closingReasonId,
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

    private static BatchBillet Billetta(

        string batchId,
        short billetNo,
        byte typeId = BatchBilletType.Real) =>
        new()
        {
            BatchId = batchId,
            PressId = batchId.Substring(0, 3),
            DieId = "R22225/1",
            TypeId = typeId,
            BilletNo = billetNo,
            SecCycle = 0,
        };
}
