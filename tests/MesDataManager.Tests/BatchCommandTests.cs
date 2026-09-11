using MesDataManager.Application.Production;
using MesDataManager.Application.Security;
using MesDataManager.Domain.Entities;
using MesDataManager.Tests.Support;

namespace MesDataManager.Tests;

/// <summary>
/// I comandi dell'elenco dati di produzione: marcatura per la riconciliazione, creazione ed
/// eliminazione di un lotto.
/// <para>
/// Le procedure del MES — ricalcolo, chiusure forzate, messaggio all'ERP — non girano su SQLite e
/// si verificano sul database vero con la sonda. Qui si verifica cio' che decide
/// l'applicazione: chi si marca e chi si salta, cosa nasce con un lotto nuovo, cosa muore con un
/// lotto eliminato.
/// </para>
/// </summary>
public sealed class BatchCommandTests : IDisposable
{
    private static readonly DateTime Inizio = new(2026, 9, 1, 8, 0, 0);

    private static readonly UserPermissions Anna = new()
    {
        UserName = "anna.rossi@metra.it",
        IsAuthenticated = true,
        CanRead = true,
        CanEditProduction = true,
    };

    private readonly ProductionHarness _harness = new();

    public BatchCommandTests()
    {
        _harness.Seed(new Company { CompanyId = "MET1", Description = "Metra", IsActive = true });
        _harness.Seed(new Press
        {
            PressId = "MP1",
            Description = "MP1",
            CompanyId = "MET1",
            SawOprId = "SAW",
            SawWrkCtrId = "SAW",
        });

        _harness.Seed(new PressBatchClosingReason
        {
            PressBatchClosingReasonId = 1,
            Description = "Chiusura ordine",
            Result = "OK",
            IsActive = true,
            IsActiveMaster = true,
        });

        _harness.Seed(new Die { DieId = "R22225/1" });
        _harness.Seed(new DieSetup { DieId = "R22225/1", StatusUse = DieUseStatus.Available });
    }

    public void Dispose() => _harness.Dispose();

    // ------------------------------------------------------------------ marcatura

    [Fact]
    public async Task Si_marcano_i_lotti_con_diagnostica_non_in_errore()
    {
        SeedLotto("MP1260901080000", diagnostics: DiagnosticsOutcome.Ok);
        SeedLotto("MP1260901090000", diagnostics: DiagnosticsOutcome.Warning);

        var result = await Service().MarkForErpAsync(
            ["MP1260901080000", "MP1260901090000"],
            marked: true);

        Assert.Equal(2, result.Marked);
        Assert.Empty(result.Skipped);

        await using var context = _harness.CreateContext();
        Assert.All(context.Batches.ToList(), b => Assert.True(b.IsErpMarked));
    }

    [Fact]
    public async Task Un_lotto_senza_diagnostica_viene_saltato_e_segnalato()
    {
        // Deciso l'8 settembre 2026: la diagnostica non si rilancia — il vecchio applicativo la
        // rieseguiva su ogni riga selezionata — e chi non ce l'ha resta indietro, dichiarandolo.
        SeedLotto("MP1260901080000", diagnostics: null);

        var result = await Service().MarkForErpAsync(["MP1260901080000"], marked: true);

        Assert.Equal(0, result.Marked);
        Assert.Equal(BatchMarkSkipReason.NoDiagnostics, Assert.Single(result.Skipped).Reason);
    }

    [Fact]
    public async Task Un_lotto_con_diagnostica_in_errore_viene_saltato()
    {
        SeedLotto("MP1260901080000", diagnostics: DiagnosticsOutcome.Error);

        var result = await Service().MarkForErpAsync(["MP1260901080000"], marked: true);

        Assert.Equal(BatchMarkSkipReason.DiagnosticsError, Assert.Single(result.Skipped).Reason);
    }

    [Fact]
    public async Task Un_lotto_bloccato_o_gia_riconciliato_viene_saltato()
    {
        SeedLotto("MP1260901080000", diagnostics: DiagnosticsOutcome.Ok, erpImported: true);
        SeedLotto("MP1260901090000", diagnostics: DiagnosticsOutcome.Ok);
        await Service().BeginEditAsync("MP1260901090000");

        var result = await Service().MarkForErpAsync(
            ["MP1260901080000", "MP1260901090000"],
            marked: true);

        Assert.Equal(0, result.Marked);
        Assert.Contains(result.Skipped, s => s.Reason == BatchMarkSkipReason.AlreadyImported);
        Assert.Contains(result.Skipped, s => s.Reason == BatchMarkSkipReason.Locked);
    }

    [Fact]
    public async Task Un_lotto_che_non_esiste_piu_viene_segnalato_e_non_fa_fallire_gli_altri()
    {
        SeedLotto("MP1260901080000", diagnostics: DiagnosticsOutcome.Ok);

        var result = await Service().MarkForErpAsync(
            ["MP1260901080000", "MP1999999999999"],
            marked: true);

        Assert.Equal(1, result.Marked);
        Assert.Equal(BatchMarkSkipReason.NotFound, Assert.Single(result.Skipped).Reason);
    }

    [Fact]
    public async Task Annullare_la_marcatura_non_ha_limitazioni()
    {
        // Togliere un lotto dalla coda dell'ERP e' sempre lecito: anche senza diagnostica, anche
        // in errore. Cosi' era anche nel vecchio applicativo.
        SeedLotto("MP1260901080000", diagnostics: DiagnosticsOutcome.Error, erpMarked: true);

        var result = await Service().MarkForErpAsync(["MP1260901080000"], marked: false);

        Assert.Equal(1, result.Marked);
        Assert.Empty(result.Skipped);

        await using var context = _harness.CreateContext();
        Assert.False(context.Batches.Single().IsErpMarked);
    }

    [Fact]
    public async Task Chi_consulta_soltanto_non_marca()
    {
        SeedLotto("MP1260901080000", diagnostics: DiagnosticsOutcome.Ok);

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Users.Reader).MarkForErpAsync(["MP1260901080000"], true));

        Assert.Equal(ProductionErrorKind.Forbidden, errore.Kind);
    }

    // ------------------------------------------------------------------ creazione

    [Fact]
    public async Task Un_lotto_nuovo_nasce_chiuso_con_i_marcatori_e_le_billette()
    {
        var batchId = await Service().CreateAsync(NuovoLotto());

        Assert.Equal("MP1260901080000", batchId);

        await using var context = _harness.CreateContext();
        var batch = context.Batches.Single();

        Assert.True(batch.IsPressClosed);
        Assert.True(batch.IsSawClosed);
        Assert.Equal("N", batch.EditStatusId);

        // E nasce da elaborare: chiuso a pressa e a sega con IsBatchProcessed a zero e' quello
        // che il lavoro pianificato del MES cerca ogni cinque minuti. L'applicazione non chiama
        // piu' usp_Batch_Elab, quindi questo flag e' l'unica cosa che mette il lotto in coda.
        Assert.False(batch.IsBatchProcessed);
        Assert.Equal("R22225/1", batch.DieId);
        Assert.Equal("R22225", batch.DieCode);
        Assert.Equal((short)1, batch.DieNumber);
        Assert.Equal((short)1, batch.PressBatchClosingReasonId);

        var billette = context.BatchBillets.OrderBy(b => b.TypeId).ThenBy(b => b.BilletNo).ToList();

        // Due marcatori piu' due billette vere.
        Assert.Equal(4, billette.Count);
        Assert.Equal(2, billette.Count(b => b.TypeId == BatchBilletType.Real));

        var apertura = billette.Single(b => b.TypeId == BatchBilletType.BatchStart);
        Assert.Equal(Inizio, apertura.StartTs);
        Assert.Equal(Inizio, apertura.StopTs);

        var chiusura = billette.Single(b => b.TypeId == BatchBilletType.BatchStop);
        Assert.Equal(Inizio.AddHours(1), chiusura.StopTs);

        // La causale di chiusura sta anche sul marcatore, come nel vecchio applicativo.
        Assert.Equal((byte)1, chiusura.ClosingReasonId);
    }

    [Fact]
    public async Task Le_billette_del_lotto_nuovo_dividono_tempo_e_chilogrammi()
    {
        await Service().CreateAsync(NuovoLotto());

        await using var context = _harness.CreateContext();
        var vere = context.BatchBillets
            .Where(b => b.TypeId == BatchBilletType.Real)
            .OrderBy(b => b.BilletNo)
            .ToList();

        Assert.Equal([1, 2], vere.Select(b => (int)b.BilletNo));
        Assert.All(vere, b => Assert.Equal(100m, b.KgSheared));
        Assert.All(vere, b => Assert.Equal(90m, b.KgExtruded));
        Assert.All(vere, b => Assert.Equal("N", b.EditStatusId));
        Assert.All(vere, b => Assert.Equal(-1, b.BatchBilletRawId));
        Assert.Equal(Inizio.AddMinutes(30), vere[0].StopTs);
        Assert.Equal(1800, vere[0].SecCycle);
    }

    [Fact]
    public async Task Un_lotto_nuovo_su_una_matrice_non_assegnabile_non_si_crea()
    {
        _harness.Seed(new Die { DieId = "R30000/1" });
        _harness.Seed(new DieSetup { DieId = "R30000/1", StatusUse = DieUseStatus.Stored });

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => Service().CreateAsync(NuovoLotto() with { DieCode = "R30000" }));

        Assert.Equal(ProductionErrorKind.Validation, errore.Kind);

        await using var context = _harness.CreateContext();
        Assert.Empty(context.Batches);
    }

    [Fact]
    public async Task Un_lotto_nuovo_che_si_sovrappone_a_un_altro_non_si_crea()
    {
        // Il controllo del vecchio applicativo non vedeva il lotto nuovo che ne inghiotte uno
        // esistente: qui il confronto fra intervalli e' completo, come sui fermi macchina.
        SeedLotto("MP1260901083000", start: Inizio.AddMinutes(30), stop: Inizio.AddMinutes(45));

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => Service().CreateAsync(NuovoLotto()));

        Assert.Equal(ProductionErrorKind.PeriodOverlap, errore.Kind);
        Assert.Contains("MP1260901083000", errore.MessageArguments[0]?.ToString());
    }

    [Fact]
    public async Task Un_lotto_su_una_pressa_diversa_puo_avere_lo_stesso_periodo()
    {
        _harness.Seed(new Press
        {
            PressId = "MP5",
            Description = "MP5",
            CompanyId = "MET1",
            SawOprId = "SAW",
            SawWrkCtrId = "SAW",
        });

        SeedLotto("MP5260901080000", pressId: "MP5");

        var batchId = await Service().CreateAsync(NuovoLotto());

        Assert.Equal("MP1260901080000", batchId);
    }

    [Fact]
    public async Task Due_lotti_nello_stesso_istante_sulla_stessa_pressa_si_fermano_sul_periodo()
    {
        // Il numero del lotto e' pressa piu' istante di inizio, quindi due lotti con lo stesso
        // numero hanno per forza lo stesso inizio — e si sovrappongono. Il controllo che scatta
        // e' quello del periodo, non quello della chiave: dice la stessa cosa in modo piu' utile,
        // perche' nomina il lotto con cui va a sbattere.
        await Service().CreateAsync(NuovoLotto());

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => Service().CreateAsync(NuovoLotto() with
            {
                StopTs = Inizio.AddMinutes(10),
                Billets = NuovoLotto().Billets with { To = Inizio.AddMinutes(10) },
            }));

        Assert.Equal(ProductionErrorKind.PeriodOverlap, errore.Kind);
        Assert.Contains("MP1260901080000", errore.MessageArguments[0]?.ToString());

        // La traduzione della chiave duplicata resta comunque, come rete: due creazioni
        // simultanee possono passare entrambe il controllo del periodo e arrivare insieme alla
        // chiave primaria.
    }

    [Fact]
    public async Task Chi_consulta_soltanto_non_crea_lotti()
    {
        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Users.Reader).CreateAsync(NuovoLotto()));

        Assert.Equal(ProductionErrorKind.Forbidden, errore.Kind);
    }

    // ------------------------------------------------------------------ eliminazione

    [Fact]
    public async Task L_eliminazione_cancella_tutto_quello_che_appartiene_al_lotto()
    {
        const string lotto = "MP1260901080000";
        SeedLotto(lotto);
        _harness.Seed(new BatchBillet
        {
            BatchId = lotto,
            PressId = "MP1",
            DieId = "R22225/1",
            TypeId = BatchBilletType.Real,
            BilletNo = 1,
            SecCycle = 0,
        });
        _harness.Seed(new BatchBarQty
        {
            BatchId = lotto,
            PressId = "MP1",
            BarLength = 6000,
            Qty = 2,
            CreatedTs = Inizio,
        });
        _harness.Seed(new BatchProdOrder { BatchId = lotto, ProdId = "OP1" });
        _harness.Seed(new BatchBilletProdOrder { BatchId = lotto, ProdId = "OP1" });

        // Le presenze degli operatori: il vecchio applicativo non le cancellava e restavano
        // orfane, perche' l'EDMX non modellava la tabella.
        _harness.Seed(new BatchWorker { BatchId = lotto, WorkerId = 7, StartTs = Inizio });

        await Service().DeleteAsync(lotto);

        await using var context = _harness.CreateContext();
        Assert.Empty(context.Batches);
        Assert.Empty(context.BatchBillets);
        Assert.Empty(context.BatchBarQties);
        Assert.Empty(context.BatchProdOrders);
        Assert.Empty(context.BatchBilletProdOrders);
        Assert.Empty(context.BatchWorkers);
    }

    [Fact]
    public async Task Un_lotto_riconciliato_non_si_elimina()
    {
        SeedLotto("MP1260901080000", erpImported: true);

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => Service().DeleteAsync("MP1260901080000"));

        Assert.Equal(ProductionErrorKind.Conflict, errore.Kind);

        await using var context = _harness.CreateContext();
        Assert.Single(context.Batches);
    }

    [Fact]
    public async Task Un_lotto_in_modifica_da_altri_non_si_elimina()
    {
        SeedLotto("MP1260901080000");
        await Service().BeginEditAsync("MP1260901080000");

        var bruno = new UserPermissions
        {
            UserName = "bruno.verdi@metra.it",
            IsAuthenticated = true,
            CanRead = true,
            CanEditProduction = true,
        };

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(bruno).DeleteAsync("MP1260901080000"));

        Assert.Equal(ProductionErrorKind.Conflict, errore.Kind);
    }

    [Fact]
    public async Task Un_lotto_inesistente_non_si_elimina()
    {
        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => Service().DeleteAsync("MP1999999999999"));

        Assert.Equal(ProductionErrorKind.NotFound, errore.Kind);
    }

    [Fact]
    public async Task Chi_consulta_soltanto_non_elimina()
    {
        SeedLotto("MP1260901080000");

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Users.Reader).DeleteAsync("MP1260901080000"));

        Assert.Equal(ProductionErrorKind.Forbidden, errore.Kind);
    }

    // ------------------------------------------------------------------ chiusura forzata

    [Fact]
    public async Task La_chiusura_forzata_di_un_lotto_riconciliato_viene_respinta()
    {
        SeedLotto("MP1260901080000", erpImported: true);

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => Service().ForceCloseAsync("MP1260901080000", press: true, saw: true));

        Assert.Equal(ProductionErrorKind.Conflict, errore.Kind);
    }

    [Fact]
    public async Task Chi_consulta_soltanto_non_forza_le_chiusure()
    {
        SeedLotto("MP1260901080000");

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Users.Reader)
                .ForceCloseAsync("MP1260901080000", press: true, saw: false));

        Assert.Equal(ProductionErrorKind.Forbidden, errore.Kind);
    }

    // ------------------------------------------------------------------ appoggio

    private IBatchService Service() => _harness.BatchServiceFor(Anna);

    private static NewBatchRequest NuovoLotto() =>
        new(
            PressId: "MP1",
            DieCode: "R22225",
            DieNumber: 1,
            StartTs: Inizio,
            StopTs: Inizio.AddHours(1),
            ClosingReasonId: 1,
            Billets: new NewBilletsRequest(
                Count: 2,
                FromNo: 1,
                From: Inizio,
                To: Inizio.AddHours(1),
                BarLengthMm: 6000,
                BilletLengthMm: 600,
                KgSheared: 200,
                KgExtruded: 180,
                ProdId: null,
                CastingId: "F1234",
                AlloyId: "6060"));

    private void SeedLotto(
        string batchId,
        string pressId = "MP1",
        string? diagnostics = null,
        bool erpImported = false,
        bool erpMarked = false,
        DateTime? start = null,
        DateTime? stop = null)
    {
        _harness.Seed(new Batch
        {
            BatchId = batchId,
            PressId = pressId,
            DieId = "R22225/1",
            StartTs = start ?? Inizio,
            StopTs = stop ?? (start ?? Inizio).AddHours(1),
            BilletCount = 1,
            IsBatchProcessed = true,
            IsErpImported = erpImported,
            IsErpMarked = erpMarked,
            UsrDiagStatus = diagnostics,
        });
    }
}
