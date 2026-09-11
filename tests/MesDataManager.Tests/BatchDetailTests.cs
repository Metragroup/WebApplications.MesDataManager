using MesDataManager.Application.Production;
using MesDataManager.Domain.Entities;
using MesDataManager.Tests.Support;

namespace MesDataManager.Tests;

/// <summary>
/// Le raccolte della scheda del lotto: rettifiche, incestamento e ordini di produzione. Sono le
/// tre parti in cui il dato mostrato non coincide con una tabella — va composto — ed e' quindi
/// dove il vecchio applicativo aveva le regole implicite che vale la pena fissare in un test.
/// </summary>
public sealed class BatchDetailTests : IDisposable
{
    private const string Lotto = "MP1260901080000";
    private static readonly DateTime Giorno = new(2026, 9, 1);

    private readonly ProductionHarness _harness = new();

    public BatchDetailTests()
    {
        _harness.Seed(new Company
        {
            CompanyId = "MET1",
            Description = "Metra",
            IsActive = true,
            SawAllowAdjustments = true,
        });

        _harness.Seed(new Press
        {
            PressId = "MP1",
            Description = "MP1",
            CompanyId = "MET1",
            SawOprId = "SAW",
            SawWrkCtrId = "SAW",
        });

        _harness.Seed(new Batch
        {
            BatchId = Lotto,
            PressId = "MP1",
            DieId = "R22225/1",
            DieCode = "R22225",
            DieNumber = 1,
            StartTs = Giorno.AddHours(8),
            StopTs = Giorno.AddHours(9),
            BilletCount = 1,
            EditStatusId = "N",
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

    public void Dispose() => _harness.Dispose();

    // ------------------------------------------------------------------ rettifiche

    [Fact]
    public async Task Le_rettifiche_arrivano_in_ordine_di_inserimento_e_tengono_il_segno()
    {
        // Una rettifica negativa e' il caso che conta: significa che la sega ha contato barre
        // che non c'erano. Se il segno si perdesse, la correzione diventerebbe il suo contrario.
        _harness.Seed(
            Rettifica(6000, -3, Giorno.AddHours(10)),
            Rettifica(4000, 12, Giorno.AddHours(9)));

        var detail = await Detail();

        Assert.Equal([12, -3], detail.Adjustments.Select(a => a.Qty));
        Assert.Equal([4000m, 6000m], detail.Adjustments.Select(a => a.BarLength));
    }

    [Fact]
    public async Task Il_pannello_rettifiche_segue_la_societa_attiva()
    {
        var conRettifiche = await Detail();
        Assert.True(conRettifiche.AllowAdjustments);

        // Cambia l'impostazione della societa', non i dati del lotto: le rettifiche restano a
        // database ma la scheda non deve mostrarle, come nel vecchio applicativo.
        await using (var context = _harness.CreateContext())
        {
            var company = context.Companies.Single();
            company.SawAllowAdjustments = false;
            await context.SaveChangesAsync();
        }

        var senzaRettifiche = await Detail();
        Assert.False(senzaRettifiche.AllowAdjustments);
    }

    [Fact]
    public async Task Una_societa_che_non_dichiara_nulla_non_ammette_rettifiche()
    {
        await using (var context = _harness.CreateContext())
        {
            var company = context.Companies.Single();
            company.SawAllowAdjustments = null;
            await context.SaveChangesAsync();
        }

        var detail = await Detail();

        Assert.False(detail.AllowAdjustments);
    }

    // ------------------------------------------------------------------ incestamento

    [Fact]
    public async Task L_incestamento_prende_l_ultimo_passo_lavorato_del_ciclo()
    {
        // Le tre colonne di operazione vengono dal ciclo della cesta, non dalla transazione: si
        // guarda l'ultimo passo lavorato in ordine di OprNumPriority.
        _harness.Seed(Cesta(1, "C001", qty: 40));
        _harness.Seed(
            Passo(1, priorita: 10, oprNum: 10, oprId: "ESTR", wrkCtr: "MP1", processato: true),
            Passo(1, priorita: 30, oprNum: 30, oprId: "IMB", wrkCtr: "IMB1", processato: true),
            Passo(1, priorita: 40, oprNum: 40, oprId: "SPED", wrkCtr: "SPED1", processato: false));

        var trans = Assert.Single((await Detail()).ModuleTransactions);

        Assert.Equal(30, trans.OprNum);
        Assert.Equal("IMB", trans.OprId);
        Assert.Equal("IMB1", trans.WrkCtrId);
    }

    [Fact]
    public async Task Una_cesta_senza_passi_lavorati_resta_senza_operazione()
    {
        _harness.Seed(Cesta(1, "C001", qty: 40));
        _harness.Seed(Passo(1, priorita: 10, oprNum: 10, oprId: "ESTR", wrkCtr: "MP1", processato: false));

        var trans = Assert.Single((await Detail()).ModuleTransactions);

        Assert.Null(trans.OprNum);
        Assert.Null(trans.OprId);
        Assert.Null(trans.WrkCtrId);
    }

    [Fact]
    public async Task La_quantita_di_scarto_e_la_somma_degli_scarti_della_cesta()
    {
        _harness.Seed(Cesta(1, "C001", qty: 40), Cesta(2, "C002", qty: 20));
        _harness.Seed(Scarto(1, 3), Scarto(1, 2), Scarto(2, 7));

        var detail = await Detail();

        Assert.Equal(5, detail.ModuleTransactions.Single(t => t.ModuleId == "C001").ScrapQty);
        Assert.Equal(7, detail.ModuleTransactions.Single(t => t.ModuleId == "C002").ScrapQty);
    }

    [Fact]
    public async Task Una_cesta_senza_scarti_ha_scarto_zero_non_nullo()
    {
        _harness.Seed(Cesta(1, "C001", qty: 40));

        Assert.Equal(0, Assert.Single((await Detail()).ModuleTransactions).ScrapQty);
    }

    [Fact]
    public async Task L_incestamento_di_un_altro_lotto_non_compare()
    {
        _harness.Seed(Cesta(1, "C001", qty: 40));
        _harness.Seed(Cesta(2, "C002", qty: 20, batchId: "MP1260901090000"));

        Assert.Equal("C001", Assert.Single((await Detail()).ModuleTransactions).ModuleId);
    }

    // ------------------------------------------------------------------ ordini di produzione

    [Fact]
    public async Task Gli_ordini_delle_billette_vincono_su_quelli_del_lotto()
    {
        // I due livelli non si sommano: se le billette hanno ordini, quelli del lotto — che sono
        // gli ordini rilasciati all'avvio dell'estrusione — non si guardano nemmeno.
        _harness.Seed(new BatchBilletProdOrder { BatchId = Lotto, ProdId = "P100" });
        _harness.Seed(new BatchProdOrder { BatchId = Lotto, ProdId = "P900" });

        Assert.Equal("P100", Assert.Single((await Detail()).ProductionOrders).ProdId);
    }

    [Fact]
    public async Task Senza_ordini_sulle_billette_si_ripiega_su_quelli_del_lotto()
    {
        _harness.Seed(new BatchProdOrder { BatchId = Lotto, ProdId = "P900" });

        Assert.Equal("P900", Assert.Single((await Detail()).ProductionOrders).ProdId);
    }

    [Fact]
    public async Task Lo_stesso_ordine_su_piu_billette_compare_una_volta_sola()
    {
        _harness.Seed(
            new BatchBilletProdOrder { BatchId = Lotto, ProdId = "P100" },
            new BatchBilletProdOrder { BatchId = Lotto, ProdId = "P100" },
            new BatchBilletProdOrder { BatchId = Lotto, ProdId = "P200" });

        Assert.Equal(["P100", "P200"], (await Detail()).ProductionOrders.Select(o => o.ProdId));
    }

    [Fact]
    public async Task Un_ordine_senza_cartellino_compare_comunque()
    {
        // Correzione rispetto alla vecchia scheda, che partiva dai cartellini e quindi perdeva
        // gli ordini non presenti fra loro: qui l'ordine c'e' col solo numero.
        _harness.Seed(new BatchProdOrder { BatchId = Lotto, ProdId = "P900" });

        var ordine = Assert.Single((await Detail()).ProductionOrders);

        Assert.Equal("P900", ordine.ProdId);
        Assert.Null(ordine.CustomerName);
    }

    [Fact]
    public async Task Le_due_leghe_dell_ordine_restano_distinte()
    {
        // Lega di vendita e lega di produzione possono differire, e la differenza e' proprio il
        // dato da vedere: unificarle nasconderebbe l'anomalia.
        _harness.Seed(new BatchProdOrder { BatchId = Lotto, ProdId = "P900" });
        _harness.Seed(new ProductionTag
        {
            ProdId = "P900",
            CustName = "Cliente Uno",
            SalesAlloyId = "6060",
            ProdAlloyId = "6060PX",
            SalesHeatTreatment = "T5",
        });

        var ordine = Assert.Single((await Detail()).ProductionOrders);

        Assert.Equal("Cliente Uno", ordine.CustomerName);
        Assert.Equal("6060", ordine.SalesAlloyId);
        Assert.Equal("6060PX", ordine.ProdAlloyId);
        Assert.Equal("T5", ordine.HeatTreatment);
    }

    // ------------------------------------------------------------------ testata

    [Fact]
    public async Task La_testata_porta_i_dati_che_servono_alla_modifica()
    {
        var detail = await Detail();

        Assert.Equal("R22225", detail.DieCode);
        Assert.Equal((short)1, detail.DieNumber);

        // EditStatusID = "N" significa inserito a mano: l'eliminazione non deve chiedere la
        // seconda conferma, che serve solo sui lotti veri di produzione.
        Assert.False(detail.IsFromProduction);
    }

    [Fact]
    public async Task Un_lotto_di_produzione_si_riconosce_dallo_stato_di_modifica()
    {
        await using (var context = _harness.CreateContext())
        {
            var batch = context.Batches.Single();
            batch.EditStatusId = null;
            await context.SaveChangesAsync();
        }

        Assert.True((await Detail()).IsFromProduction);
    }

    [Fact]
    public async Task I_due_esiti_di_diagnostica_arrivano_entrambi()
    {
        // Servizio e utente convivono: quello del servizio resta anche dopo che un utente ha
        // rieseguito la diagnostica da qui, e la scheda mostra i due accanto.
        await using (var context = _harness.CreateContext())
        {
            var batch = context.Batches.Single();
            batch.SvcDiagStatus = "ERR";
            batch.SvcDiagTs = Giorno.AddHours(10);
            batch.SvcDiagMsg = "Diagnostica lotto: ERR";
            batch.SvcDiagJson = "{\"diagnostics\":{\"status\":\"ERR\"}}";
            batch.UsrDiagStatus = "OK";
            batch.UsrDiagTs = Giorno.AddHours(12);
            batch.UsrDiagMsg = "Diagnostica lotto: OK";
            batch.UsrDiagJson = "{\"diagnostics\":{\"status\":\"OK\"}}";
            await context.SaveChangesAsync();
        }

        var detail = await Detail();

        Assert.Equal("ERR", detail.SvcDiagStatus);
        Assert.Equal(Giorno.AddHours(10), detail.SvcDiagTs);
        Assert.Equal("Diagnostica lotto: ERR", detail.SvcDiagMsg);
        Assert.Contains("ERR", detail.SvcDiagJson);

        Assert.Equal("OK", detail.UsrDiagStatus);
        Assert.Equal("Diagnostica lotto: OK", detail.UsrDiagMsg);

        // L'esito che vale e' quello dell'utente: e' l'ultima parola sul lotto.
        Assert.Equal("OK", detail.DiagnosticsStatus);
        Assert.True(detail.HasDiagnostics);
    }

    [Fact]
    public async Task Senza_esito_dell_utente_vale_quello_del_servizio()
    {
        await using (var context = _harness.CreateContext())
        {
            var batch = context.Batches.Single();
            batch.SvcDiagStatus = "ATT";
            batch.SvcDiagTs = Giorno.AddHours(10);
            await context.SaveChangesAsync();
        }

        var detail = await Detail();

        Assert.Equal("ATT", detail.DiagnosticsStatus);
        Assert.Null(detail.UsrDiagStatus);
        Assert.True(detail.HasDiagnostics);
    }

    [Fact]
    public async Task Un_lotto_mai_diagnosticato_non_ha_esito()
    {
        var detail = await Detail();

        Assert.Null(detail.DiagnosticsStatus);
        Assert.False(detail.HasDiagnostics);
    }

    [Fact]
    public async Task Finche_esiste_il_vecchio_esito_ha_la_precedenza()
    {
        // Il vecchio applicativo e' ancora in servizio e scrive le sue colonne: finche' le
        // scrive, e' lui a dire come sta il lotto. Stessa precedenza delle funzioni
        // EF.ufn_BatchByLength(Shift), allineate l'11 settembre 2026 — due ordini diversi
        // farebbero dire cose diverse alle due modalita' dello stesso elenco.
        await using (var context = _harness.CreateContext())
        {
            var batch = context.Batches.Single();
            batch.LegacyDiagStatus = "ERR";
            batch.LegacyDiagTs = Giorno.AddHours(8);
            batch.LegacyDiagMsg = "Diagnostica lotto: ERR";
            batch.UsrDiagStatus = "OK";
            batch.UsrDiagTs = Giorno.AddHours(12);
            batch.SvcDiagStatus = "ATT";
            await context.SaveChangesAsync();
        }

        var detail = await Detail();

        Assert.Equal("ERR", detail.DiagnosticsStatus);

        // I tre restano distinti e visibili: la scheda li mostra affiancati, ed e' li' che si
        // vede che una riesecuzione fatta da qui non ha cambiato quello che l'elenco mostra.
        Assert.Equal("ERR", detail.LegacyDiagStatus);
        Assert.Equal("OK", detail.UsrDiagStatus);
        Assert.Equal("ATT", detail.SvcDiagStatus);
        Assert.Equal("Diagnostica lotto: ERR", detail.LegacyDiagMsg);
    }

    [Fact]
    public async Task Senza_il_vecchio_esito_vale_quello_dell_utente()
    {
        await using (var context = _harness.CreateContext())
        {
            var batch = context.Batches.Single();
            batch.UsrDiagStatus = "OK";
            batch.SvcDiagStatus = "ATT";
            await context.SaveChangesAsync();
        }

        Assert.Equal("OK", (await Detail()).DiagnosticsStatus);
    }

    [Fact]
    public async Task L_elenco_mostra_lo_stesso_esito_della_scheda()
    {
        // La stessa precedenza deve valere nella query dell'elenco, che la calcola in SQL, e
        // nella scheda, che la calcola in memoria: sono due strade allo stesso valore.
        await using (var context = _harness.CreateContext())
        {
            var batch = context.Batches.Single();
            batch.LegacyDiagStatus = "ATT";
            batch.UsrDiagStatus = "OK";
            await context.SaveChangesAsync();
        }

        var page = await _harness.BatchServiceFor(Users.Reader).GetPageAsync(
            new BatchListQuery(PressId: null));

        Assert.Equal("ATT", Assert.Single(page.Rows).DiagnosticsStatus);
        Assert.Equal("ATT", (await Detail()).DiagnosticsStatus);
    }

    // ------------------------------------------------------------------ appoggio

    private Task<BatchDetail> Detail() =>
        _harness.BatchServiceFor(Users.Reader).GetDetailAsync(Lotto);

    private static BatchBarQty Rettifica(decimal barLength, int qty, DateTime created) =>
        new()
        {
            BatchId = Lotto,
            PressId = "MP1",
            BarLength = barLength,
            Qty = qty,
            CreatedTs = created,
        };

    private static ModuleTrans Cesta(long id, string moduleId, int qty, string batchId = Lotto) =>
        new()
        {
            ModuleTransId = id,
            ModuleId = moduleId,
            PressId = "MP1",
            BatchId = batchId,
            ProdId = "P100",
            BarLength = 6000,
            Qty = qty,
            CreatedTs = Giorno.AddHours(9).AddMinutes(id),
        };

    private static ModuleTransRoute Passo(
        long moduleTransId,
        int priorita,
        int oprNum,
        string oprId,
        string wrkCtr,
        bool processato) =>
        new()
        {
            ModuleTransRouteId = (int)(moduleTransId * 100 + priorita),
            ModuleTransId = moduleTransId,
            OprNumPriority = priorita,
            OprNum = oprNum,
            OprId = oprId,
            WrkCtrId = wrkCtr,
            IsProcessed = processato,
        };

    private static ModuleTransScrap Scarto(long moduleTransId, int qty) =>
        new()
        {
            ModuleTransScrapId = (int)(moduleTransId * 100 + qty),
            ModuleTransId = moduleTransId,
            Qty = qty,
        };
}
