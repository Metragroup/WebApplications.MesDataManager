using MesDataManager.Application.Production;
using MesDataManager.Application.Security;
using MesDataManager.Domain.Entities;
using MesDataManager.Tests.Support;

namespace MesDataManager.Tests;

/// <summary>
/// Diagnostica del lotto: lettura della risposta, esito, conservazione e billette mancanti.
/// <para>
/// La chiamata HTTP e' sostituita da un client finto, quindi qui si verifica cio' che
/// l'applicazione fa <b>con</b> la risposta — che e' tutta la parte che le appartiene.
/// </para>
/// </summary>
public sealed class BatchDiagnosticsTests : IDisposable
{
    private const string Lotto = "MP1260901080000";
    private static readonly DateTime Inizio = new(2026, 9, 1, 8, 0, 0);

    private static readonly UserPermissions Anna = new()
    {
        UserName = "anna.rossi@metra.it",
        IsAuthenticated = true,
        CanRead = true,
        CanEditProduction = true,
    };

    private readonly ProductionHarness _harness = new();

    public BatchDiagnosticsTests()
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
    }

    public void Dispose() => _harness.Dispose();

    // ------------------------------------------------------------------ lettura della risposta

    [Fact]
    public void La_risposta_del_servizio_si_legge_in_forma_strutturata()
    {
        var report = DiagnosticsReportReader.TryRead(Risposta("ERR", warnings: 1, failed: 1));

        Assert.NotNull(report);
        Assert.Equal("ERR", report.Status);
        Assert.Equal(15, report.Total);
        Assert.Equal(13, report.Passed);
        Assert.Equal(1, report.Warnings);
        Assert.Equal(1, report.Failed);
        Assert.Equal("Billetta 7 senza colata", Assert.Single(report.ErrorMessages));
        Assert.Equal("Lega diversa dal cartellino", Assert.Single(report.WarningMessages));
        Assert.Equal(2, report.Checks.Count);
    }

    [Fact]
    public void Le_billette_mancanti_portano_il_motivo_per_cui_lo_sono()
    {
        // E' il dato che serve a fidarsi — o a non fidarsi — di una billetta comparsa dal nulla.
        var report = DiagnosticsReportReader.TryRead(Risposta("ATT", warnings: 1, failed: 0));

        var mancante = Assert.Single(report!.MissingBillets);

        Assert.Equal((short)20, mancante.BilletNo);
        Assert.Equal("AUTO_INSERT_CANDIDATE", mancante.Decision);
        Assert.Equal(0.9111m, mancante.ConfidenceScore);
        Assert.Contains("NET_GAP_COMPATIBLE_WITH_EXPECTED_DURATION", mancante.ReasonCodes);
        Assert.Contains("vuoto 161 s", mancante.GapDetail);
        Assert.Contains("attesi 135 s", mancante.GapDetail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Diagnostica lotto: OK")]
    [InlineData("{ questo non e' json }")]
    [InlineData("{\"altro\":1}")]
    public void Cio_che_non_e_una_risposta_del_servizio_non_si_legge(string? contenuto)
    {
        // Le 232 mila righe storiche contengono il referto testuale del vecchio applicativo: la
        // scheda le mostra come testo, e non come errore.
        Assert.Null(DiagnosticsReportReader.TryRead(contenuto));
    }

    // ------------------------------------------------------------------ esito

    [Theory]
    [InlineData("OK", 0, 0, "OK")]
    [InlineData("OK", 2, 0, "ATT")]
    [InlineData("ERR", 0, 1, "ERR")]
    [InlineData("OK", 0, 1, "ERR")]
    [InlineData("ATT", 1, 0, "ATT")]
    public async Task L_esito_si_ricava_da_stato_avvisi_e_controlli_falliti(
        string statoServizio,
        int avvisi,
        int falliti,
        string atteso)
    {
        SeedLotto();

        var esito = await Service(Risposta(statoServizio, avvisi, falliti)).RunAsync(Lotto);

        Assert.Equal(atteso, esito.Status);

        await using var context = _harness.CreateContext();
        Assert.Equal(atteso, context.Batches.Single().UsrDiagStatus);
    }

    // ------------------------------------------------------------------ conservazione

    [Fact]
    public async Task La_risposta_si_conserva_verbatim()
    {
        // Non riserializzata: cio' che si rilegge fra sei mesi deve essere quello che il servizio
        // ha detto, carattere per carattere.
        SeedLotto();
        var risposta = Risposta("OK", 0, 0);

        await Service(risposta).RunAsync(Lotto);

        await using var context = _harness.CreateContext();
        Assert.Equal(risposta, context.Batches.Single().UsrDiagJson);
    }

    [Fact]
    public async Task Il_referto_arriva_dal_servizio_e_non_si_ricostruisce()
    {
        // Dall'11 settembre 2026 messaggio e risposta si conservano separati: il referto e'
        // quello che il servizio mette in diagnostics.message, non un testo composto qui.
        SeedLotto();

        await Service(Risposta("ATT", 1, 0)).RunAsync(Lotto);

        await using var context = _harness.CreateContext();
        Assert.Equal("Diagnostica lotto: ATT", context.Batches.Single().UsrDiagMsg);
    }

    [Fact]
    public async Task La_diagnostica_del_servizio_non_viene_mai_toccata()
    {
        // E' il punto della separazione: un lotto senza esito del servizio e' un buco nel calcolo
        // automatico, e riempirlo con l'esito di una esecuzione chiesta a mano cancellerebbe
        // proprio l'informazione che serve a trovarlo. Fino all'11 settembre 2026 l'applicazione
        // compilava i campi del servizio "se vuoti".
        SeedLotto();

        await Service(Risposta("ERR", 0, 1)).RunAsync(Lotto);

        await using (var context = _harness.CreateContext())
        {
            var batch = context.Batches.Single();
            Assert.Equal("ERR", batch.UsrDiagStatus);
            Assert.NotNull(batch.UsrDiagTs);

            Assert.Null(batch.SvcDiagStatus);
            Assert.Null(batch.SvcDiagTs);
            Assert.Null(batch.SvcDiagMsg);
            Assert.Null(batch.SvcDiagJson);
        }

        // E non lo tocca nemmeno quando c'e' gia': resta la fotografia del calcolo automatico.
        await using (var context = _harness.CreateContext())
        {
            var batch = context.Batches.Single();
            batch.SvcDiagStatus = "ATT";
            batch.SvcDiagTs = new DateTime(2026, 9, 1, 6, 0, 0);
            await context.SaveChangesAsync();
        }

        await Service(Risposta("OK", 0, 0)).RunAsync(Lotto);

        await using (var context = _harness.CreateContext())
        {
            var batch = context.Batches.Single();
            Assert.Equal("OK", batch.UsrDiagStatus);
            Assert.Equal("ATT", batch.SvcDiagStatus);
            Assert.Equal(new DateTime(2026, 9, 1, 6, 0, 0), batch.SvcDiagTs);
        }
    }

    [Fact]
    public async Task Un_referto_piu_lungo_della_colonna_viene_troncato()
    {
        // La colonna tiene 2000 caratteri; un lotto con molti errori ha un referto lungo quanto
        // l'elenco degli errori, e sarebbe il salvataggio a fallire proprio sul lotto messo
        // peggio. Il testo intero resta nel JSON accanto.
        SeedLotto();
        var lungo = new string('x', 2500);

        await Service(RispostaConMessaggio(lungo)).RunAsync(Lotto);

        await using var context = _harness.CreateContext();
        var batch = context.Batches.Single();

        Assert.Equal(2000, batch.UsrDiagMsg!.Length);
        Assert.Contains(lungo, batch.UsrDiagJson);
    }

    // ------------------------------------------------------------------ billette mancanti

    [Fact]
    public async Task Una_billetta_mancante_si_inserisce_marcata_automatica()
    {
        SeedLotto();

        var esito = await Service(Risposta("ATT", 1, 0)).RunAsync(Lotto);

        Assert.Equal(1, esito.InsertedBillets);

        await using var context = _harness.CreateContext();
        var inserita = context.BatchBillets.Single(b => b.BilletNo == 20);

        Assert.Equal("A", inserita.EditStatusId);
        Assert.Equal(-1, inserita.BatchBilletRawId);
        Assert.Equal("MP1", inserita.PressId);
        Assert.Equal("R22225/1", inserita.DieId);
        Assert.Equal(6800m, inserita.MmBarSet);

        // La lunghezza billetta e' un intero a database e il servizio la manda coi decimali.
        Assert.Equal(680, inserita.MmBilletAct);
        Assert.Equal("F 22584-26", inserita.Billet1CastingId);
        Assert.Equal(135, inserita.SecCycle);
    }

    [Fact]
    public async Task Una_billetta_corretta_a_mano_non_viene_sovrascritta()
    {
        // E' la regola che conta di tutta la funzione: il giudizio di una persona batte quello
        // del servizio. Senza, la diagnostica successiva cancellerebbe la correzione.
        SeedLotto();
        _harness.Seed(new BatchBillet
        {
            BatchId = Lotto,
            PressId = "MP1",
            DieId = "R22225/1",
            TypeId = BatchBilletType.Real,
            BilletNo = 20,
            SecCycle = 0,
            MmBarSet = 1234,
            EditStatusId = "M",
        });

        var esito = await Service(Risposta("ATT", 1, 0)).RunAsync(Lotto);

        Assert.Equal(0, esito.InsertedBillets);

        await using var context = _harness.CreateContext();
        var billetta = context.BatchBillets.Single(b => b.BilletNo == 20);

        Assert.Equal(1234m, billetta.MmBarSet);
        Assert.Equal("M", billetta.EditStatusId?.Trim());
    }

    [Fact]
    public async Task Una_billetta_gia_automatica_viene_aggiornata()
    {
        SeedLotto();
        _harness.Seed(new BatchBillet
        {
            BatchId = Lotto,
            PressId = "MP1",
            DieId = "R22225/1",
            TypeId = BatchBilletType.Real,
            BilletNo = 20,
            SecCycle = 0,
            MmBarSet = 1234,
            EditStatusId = "A",
        });

        var esito = await Service(Risposta("ATT", 1, 0)).RunAsync(Lotto);

        Assert.Equal(1, esito.InsertedBillets);

        await using var context = _harness.CreateContext();
        Assert.Equal(6800m, context.BatchBillets.Single(b => b.BilletNo == 20).MmBarSet);
    }

    // ------------------------------------------------------------------ permessi e blocco

    [Fact]
    public async Task Chi_consulta_soltanto_non_esegue_la_diagnostica()
    {
        // La diagnostica scrive: esito, risposta e billette. Non e' una lettura.
        SeedLotto();

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => Service(Risposta("OK", 0, 0), Users.Reader).RunAsync(Lotto));

        Assert.Equal(ProductionErrorKind.Forbidden, errore.Kind);
    }

    [Fact]
    public async Task Su_un_lotto_in_modifica_la_esegue_solo_chi_lo_sta_modificando()
    {
        SeedLotto();
        await _harness.BatchServiceFor(Anna).BeginEditAsync(Lotto);

        var bruno = new UserPermissions
        {
            UserName = "bruno.verdi@metra.it",
            IsAuthenticated = true,
            CanRead = true,
            CanEditProduction = true,
        };

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => Service(Risposta("OK", 0, 0), bruno).RunAsync(Lotto));

        Assert.Equal(ProductionErrorKind.Conflict, errore.Kind);

        // Chi ha il blocco invece la esegue: e' il caso normale, dopo aver corretto il lotto.
        var esito = await Service(Risposta("OK", 0, 0)).RunAsync(Lotto);
        Assert.Equal("OK", esito.Status);
    }

    [Fact]
    public async Task Se_il_servizio_non_risponde_non_c_e_ripiego()
    {
        // Le ~340 righe di regole locali del vecchio applicativo non sono state riportate: due
        // copie delle stesse regole divergono, e la locale era l'unica a non essere aggiornata.
        SeedLotto();

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => ServiceWithFailingClient().RunAsync(Lotto));

        Assert.Contains("DiagnosticsUnavailable", errore.MessageKey);

        await using var context = _harness.CreateContext();
        Assert.Null(context.Batches.Single().UsrDiagStatus);
    }

    [Fact]
    public async Task Una_risposta_illeggibile_non_si_scrive_sul_lotto()
    {
        SeedLotto();

        await Assert.ThrowsAsync<ProductionException>(
            () => Service("non sono json").RunAsync(Lotto));

        await using var context = _harness.CreateContext();
        Assert.Null(context.Batches.Single().UsrDiagJson);
    }

    [Fact]
    public async Task Il_parametro_del_servizio_chiede_di_guardare_il_lotto_come_e_adesso()
    {
        // Dalla scheda si passa ignoreManualAddedBillets = false: e' il caso d'uso per cui il
        // parametro esiste — controllare il lotto dopo gli aggiustamenti.
        SeedLotto();
        var client = new FakeDiagnosticsClient(Risposta("OK", 0, 0));

        await ServiceWith(client, Anna).RunAsync(Lotto);

        Assert.False(client.LastIgnoreManualAddedBillets);
        Assert.Equal(Lotto, client.LastBatchId);
    }

    // ------------------------------------------------------------------ appoggio

    private void SeedLotto()
    {
        _harness.Seed(new Batch
        {
            BatchId = Lotto,
            PressId = "MP1",
            DieId = "R22225/1",
            StartTs = Inizio,
            StopTs = Inizio.AddHours(1),
            BilletCount = 1,
            IsBatchProcessed = true,
        });

        _harness.Seed(new BatchBillet
        {
            BatchId = Lotto,
            PressId = "MP1",
            DieId = "R22225/1",
            TypeId = BatchBilletType.Real,
            BilletNo = 1,
            SecCycle = 0,
        });
    }

    private IBatchDiagnosticsService Service(string risposta, UserPermissions? permissions = null) =>
        ServiceWith(new FakeDiagnosticsClient(risposta), permissions ?? Anna);

    private IBatchDiagnosticsService ServiceWithFailingClient() =>
        ServiceWith(new FakeDiagnosticsClient(null), Anna);

    private IBatchDiagnosticsService ServiceWith(IDiagnosticsClient client, UserPermissions permissions) =>
        _harness.DiagnosticsServiceFor(permissions, client);

    /// <summary>
    /// Una risposta con la forma di quella vera, ricalcata sul campione del servizio
    /// (<c>AD-2210-SampleResult.json</c>): nomi dei campi, tipi e il record della billetta
    /// mancante con i nomi delle colonne di <c>Press.BatchBillet</c>.
    /// </summary>
    /// <summary>Risposta minima con un referto di lunghezza scelta, per la prova di troncamento.</summary>
    private static string RispostaConMessaggio(string message) =>
        $$"""
        {
          "batchId": "{{Lotto}}",
          "diagnostics": {
            "status": "OK",
            "message": "{{message}}",
            "summary": { "total": 1, "passed": 1, "warnings": 0, "failed": 0, "skipped": 0 },
            "checks": [],
            "errors": [],
            "warnings": []
          }
        }
        """;

    private static string Risposta(string status, int warnings, int failed) =>
        $$"""
        {
          "batchId": "{{Lotto}}",
          "diagnostics": {
            "status": "{{status}}",
            "message": "Diagnostica lotto: {{status}}",
            "summary": { "total": 15, "passed": 13, "warnings": {{warnings}}, "failed": {{failed}}, "skipped": 0 },
            "checks": [
              { "id": "BATCH_ID_PRESENT", "description": "BatchID valorizzato", "outcome": "PASS" },
              { "id": "BILLET_WEIGHT", "description": "Peso billetta", "outcome": "WARN", "detail": "fuori tolleranza" }
            ],
            "errors": [ { "code": "E01", "message": "Billetta 7 senza colata" } ],
            "warnings": [ { "code": "W01", "message": "Lega diversa dal cartellino" } ]
          },
          "analysis": { "decision": "REVIEW", "reasonCodes": [ "MISSING_BILLETS" ] },
          "missingBillets": [
            {
              "decision": "AUTO_INSERT_CANDIDATE",
              "confidenceScore": 0.9111,
              "reasonCodes": [ "NET_GAP_COMPATIBLE_WITH_EXPECTED_DURATION", "MULTIPLE_BILLETS_IN_GAP" ],
              "gap": {
                "isFinalGap": false,
                "billetsInGap": 2,
                "netGapSec": 160.5,
                "expectedDurationSec": 135.0
              },
              "record": {
                "BatchID": "{{Lotto}}",
                "TypeID": 1,
                "BilletNo": 20,
                "PressID": "MP1",
                "DieID": "R22225/1",
                "StartTs": "2026-09-01T08:43:16",
                "StopTs": "2026-09-01T08:45:31",
                "MmBarSet": 6800.0,
                "MmBilletAct": 680.0,
                "KgSheared": 110.16,
                "KgExtruded": 103.0,
                "Billet1_AlloyID": "6060PX",
                "Billet1_CastingID": "F 22584-26          ",
                "ProdID": null,
                "SecCycle": 135,
                "SecExtrusion": 135,
                "BatchBilletRawID": -1,
                "EditStatusID": "A"
              }
            }
          ]
        }
        """;
}

/// <summary>
/// Client di diagnostica finto: restituisce la risposta preparata, oppure fallisce come
/// fallirebbe il servizio spento. Registra cosa gli e' stato chiesto.
/// </summary>
internal sealed class FakeDiagnosticsClient(string? response) : IDiagnosticsClient
{
    public string? LastBatchId { get; private set; }

    public bool? LastIgnoreManualAddedBillets { get; private set; }

    public Task<string> AnalyzeAsync(
        string batchId,
        bool ignoreManualAddedBillets,
        CancellationToken cancellationToken = default)
    {
        LastBatchId = batchId;
        LastIgnoreManualAddedBillets = ignoreManualAddedBillets;

        return response is null
            ? throw ProductionException.DiagnosticsUnavailable()
            : Task.FromResult(response);
    }
}
