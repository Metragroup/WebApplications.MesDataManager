using MesDataManager.Application.Production;
using MesDataManager.Application.Security;
using MesDataManager.Domain.Entities;
using MesDataManager.Tests.Support;

namespace MesDataManager.Tests;

/// <summary>
/// Le operazioni sulle billette in sospeso: aggiunta, duplicazione, rimozione, rinumerazione e
/// modifica multipla.
/// <para>
/// Sono le regole che nel vecchio applicativo stavano dentro i gestori dei form — divisione dei
/// chilogrammi, distribuzione degli istanti, stato di modifica — e che qui vivono nel modello,
/// dove si possono verificare senza aprire una finestra.
/// </para>
/// </summary>
public sealed class BatchBilletEditTests : IDisposable
{
    private const string Lotto = "MP1260901080000";
    private static readonly DateTime Inizio = new(2026, 9, 1, 8, 0, 0);
    private static readonly DateTime Fine = new(2026, 9, 1, 9, 0, 0);

    private static readonly UserPermissions Anna = new()
    {
        UserName = "anna.rossi@metra.it",
        IsAuthenticated = true,
        CanRead = true,
        CanEditProduction = true,
    };

    private readonly ProductionHarness _harness = new();

    public BatchBilletEditTests()
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

    // ------------------------------------------------------------------ aggiunta

    [Fact]
    public async Task Le_billette_nuove_dividono_tempo_e_chilogrammi()
    {
        // I kg indicati sono il totale del blocco: e' cosi' che li trattava
        // BatchesPresenter.AddBillet, e chi inserisce conosce il peso dell'insieme.
        var edit = await Edit(billets: 0);

        var aggiunte = edit.AddBillets(Request(count: 4, kgSheared: 400, kgExtruded: 360));

        Assert.Equal(4, aggiunte.Count);
        Assert.All(aggiunte, b => Assert.Equal(100m, b.Billet1Kg));
        Assert.All(aggiunte, b => Assert.Equal(90m, b.KgExtruded));

        // Un'ora divisa in quattro: quindici minuti per billetta, senza buchi fra una e l'altra.
        Assert.Equal(Inizio, aggiunte[0].StartTs);
        Assert.Equal(Inizio.AddMinutes(15), aggiunte[0].StopTs);
        Assert.Equal(Inizio.AddMinutes(15), aggiunte[1].StartTs);
        Assert.Equal(Fine, aggiunte[3].StopTs);
    }

    [Fact]
    public async Task Le_billette_nuove_sono_numerate_in_sequenza_e_marcate_nuove()
    {
        var edit = await Edit(billets: 2);

        var aggiunte = edit.AddBillets(Request(count: 3, fromNo: 3));

        Assert.Equal([3, 4, 5], aggiunte.Select(b => (int)b.BilletNo));
        Assert.All(aggiunte, b => Assert.Equal("N", b.EditStatusId));
        Assert.All(aggiunte, b => Assert.True(b.IsNew));
        Assert.All(aggiunte, b => Assert.Null(b.Id));
    }

    [Fact]
    public async Task I_kg_cesoiati_sono_la_somma_dei_due_tronconi()
    {
        // Non e' un campo da compilare: lo calcolava il salvataggio del vecchio applicativo
        // (CalcBilletsKgSheared), e resta un valore derivato.
        var edit = await Edit(billets: 0);

        var billetta = edit.AddBillets(Request(count: 1, kgSheared: 120))[0];

        Assert.Equal(120m, billetta.KgSheared);

        billetta.Billet2Kg = 30;
        Assert.Equal(150m, billetta.KgSheared);
    }

    [Theory]
    [InlineData(0, 100, 100, 6000, 600)]      // nessuna billetta
    [InlineData(2, 0, 100, 6000, 600)]        // senza kg cesoiati
    [InlineData(2, 100, 0, 6000, 600)]        // senza kg estrusi
    [InlineData(2, 100, 200, 6000, 600)]      // estrusi piu' dei cesoiati
    [InlineData(2, 100, 100, 0, 600)]         // senza lunghezza barra
    [InlineData(2, 100, 100, 6000, 0)]        // senza lunghezza billetta
    [InlineData(2, 100, 100, 500, 600)]       // barra piu' corta della billetta
    public async Task Un_inserimento_incoerente_viene_respinto(
        int count,
        decimal kgSheared,
        decimal kgExtruded,
        decimal barLength,
        int billetLength)
    {
        var edit = await Edit(billets: 0);

        Assert.Throws<ProductionException>(() => edit.AddBillets(new NewBilletsRequest(
            Count: count,
            FromNo: 1,
            From: Inizio,
            To: Fine,
            BarLengthMm: barLength,
            BilletLengthMm: billetLength,
            KgSheared: kgSheared,
            KgExtruded: kgExtruded,
            ProdId: null,
            CastingId: "F1234",
            AlloyId: "6060")));

        Assert.Empty(edit.Billets);
    }

    [Fact]
    public async Task Una_billetta_non_puo_cominciare_prima_del_lotto()
    {
        var edit = await Edit(billets: 1);

        Assert.Throws<ProductionException>(() => edit.AddBillets(
            Request(count: 1) with { From = Inizio.AddHours(-1) }));
    }

    // ------------------------------------------------------------------ duplicazione

    [Fact]
    public async Task Duplicare_una_billetta_ne_ricopia_i_dati_e_non_gli_istanti()
    {
        var edit = await Edit(billets: 1);
        var origine = edit.Billets[0];
        origine.SetCasting1("F1234", "6060");
        origine.MmBarSet = 6000;
        origine.Billet1Kg = 110;

        var copie = edit.DuplicateBillet(
            origine.LocalId,
            count: 2,
            fromNo: 2,
            from: Fine,
            to: Fine.AddMinutes(30));

        Assert.Equal(2, copie.Count);
        Assert.All(copie, c => Assert.Equal("F1234", c.Billet1CastingId));
        Assert.All(copie, c => Assert.Equal("6060", c.Billet1AlloyId));
        Assert.All(copie, c => Assert.Equal(6000m, c.MmBarSet));
        Assert.All(copie, c => Assert.Equal(110m, c.Billet1Kg));

        Assert.Equal([2, 3], copie.Select(c => (int)c.BilletNo));
        Assert.Equal(Fine, copie[0].StartTs);
        Assert.Equal(Fine.AddMinutes(15), copie[0].StopTs);
        Assert.Equal(Fine.AddMinutes(30), copie[1].StopTs);
    }

    [Fact]
    public async Task Duplicare_una_billetta_che_non_c_e_e_un_errore()
    {
        var edit = await Edit(billets: 1);

        var errore = Assert.Throws<ProductionException>(
            () => edit.DuplicateBillet(999, 1, 2, Inizio, Fine));

        Assert.Equal(ProductionErrorKind.NotFound, errore.Kind);
    }

    // ------------------------------------------------------------------ rimozione

    [Fact]
    public async Task Rimuovere_billette_esistenti_le_mette_in_coda_per_la_cancellazione()
    {
        var edit = await Edit(billets: 3);
        var chiavi = edit.Billets.Select(b => b.Id!.Value).ToList();

        edit.RemoveBillets([edit.Billets[0].LocalId, edit.Billets[2].LocalId]);

        Assert.Single(edit.Billets);
        Assert.Equal([chiavi[0], chiavi[2]], edit.DeletedBilletIds);
        Assert.True(edit.BilletsChanged);
    }

    [Fact]
    public async Task Rimuovere_una_billetta_appena_aggiunta_non_lascia_traccia()
    {
        // Non esiste a database: non c'e' niente da cancellare al salvataggio.
        var edit = await Edit(billets: 1);
        var nuova = edit.AddBillets(Request(count: 1, fromNo: 2))[0];

        edit.RemoveBillets([nuova.LocalId]);

        Assert.Single(edit.Billets);
        Assert.Empty(edit.DeletedBilletIds);
        Assert.False(edit.BilletsChanged);
    }

    // ------------------------------------------------------------------ rinumerazione

    [Fact]
    public async Task La_rinumerazione_agisce_sulle_righe_selezionate_in_ordine_di_numero()
    {
        var edit = await Edit(billets: 4);

        // Si selezionano la terza e la seconda, in quest'ordine: la rinumerazione le riordina
        // per numero attuale prima di assegnare, come frmRenumberBillet.
        edit.RenumberBillets([edit.Billets[2].LocalId, edit.Billets[1].LocalId], fromNo: 10);

        Assert.Equal([1, 4, 10, 11], edit.Billets.Select(b => (int)b.BilletNo));
    }

    [Fact]
    public async Task Rinumerare_una_sola_billetta_funziona()
    {
        // Nel vecchio applicativo con una riga selezionata il comando non faceva nulla: era un
        // difetto, non una regola.
        var edit = await Edit(billets: 2);

        edit.RenumberBillets([edit.Billets[1].LocalId], fromNo: 7);

        Assert.Equal([1, 7], edit.Billets.Select(b => (int)b.BilletNo));
    }

    [Fact]
    public async Task Le_billette_restano_in_ordine_di_numero()
    {
        var edit = await Edit(billets: 2);

        edit.RenumberBillets([edit.Billets[0].LocalId], fromNo: 9);

        Assert.Equal([2, 9], edit.Billets.Select(b => (int)b.BilletNo));
    }

    // ------------------------------------------------------------------ modifica multipla

    [Fact]
    public async Task La_colata_si_applica_alla_selezione_con_la_sua_lega()
    {
        var edit = await Edit(billets: 3);
        var scelte = edit.Billets.Take(2).Select(b => b.LocalId).ToList();

        edit.ApplyCasting(scelte, "f5678", "6082");

        // Il codice si normalizza in maiuscolo, come faceva il form.
        Assert.Equal("F5678", edit.Billets[0].Billet1CastingId);
        Assert.Equal("6082", edit.Billets[0].Billet1AlloyId);
        Assert.Equal("F5678", edit.Billets[1].Billet1CastingId);
        Assert.Null(edit.Billets[2].Billet1CastingId);
    }

    [Fact]
    public async Task Le_lunghezze_si_applicano_alla_selezione()
    {
        var edit = await Edit(billets: 2);
        var tutte = edit.Billets.Select(b => b.LocalId).ToList();

        edit.ApplyBarLength(tutte, 6500);
        edit.ApplyBilletLength(tutte, 680);

        Assert.All(edit.Billets, b => Assert.Equal(6500m, b.MmBarSet));
        Assert.All(edit.Billets, b => Assert.Equal(680, b.MmBilletAct));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task Una_lunghezza_non_positiva_viene_respinta(int millimetri)
    {
        var edit = await Edit(billets: 1);
        var tutte = edit.Billets.Select(b => b.LocalId).ToList();

        Assert.Throws<ProductionException>(() => edit.ApplyBarLength(tutte, millimetri));
        Assert.Throws<ProductionException>(() => edit.ApplyBilletLength(tutte, millimetri));
    }

    [Fact]
    public async Task L_ordine_di_produzione_si_puo_togliere()
    {
        var edit = await Edit(billets: 1);
        var tutte = edit.Billets.Select(b => b.LocalId).ToList();

        edit.ApplyProdId(tutte, "op123");
        Assert.Equal("OP123", edit.Billets[0].ProdId);

        edit.ApplyProdId(tutte, "   ");
        Assert.Null(edit.Billets[0].ProdId);
    }

    // ------------------------------------------------------------------ stato di modifica

    [Fact]
    public async Task Una_billetta_toccata_a_mano_diventa_modificata()
    {
        var edit = await Edit(billets: 1);
        var billetta = edit.Billets[0];

        Assert.False(billetta.IsModified);
        Assert.Null(billetta.EditStatusId);

        billetta.MmBarSet = 6500;

        Assert.True(billetta.IsModified);
        Assert.Equal("M", billetta.EditStatusId);
        Assert.True(edit.HasChanges);
    }

    [Fact]
    public async Task Rimettere_il_valore_di_partenza_non_lascia_la_billetta_modificata()
    {
        var edit = await Edit(billets: 1);
        var billetta = edit.Billets[0];
        var originale = billetta.MmBarSet;

        billetta.MmBarSet = 6500;
        billetta.MmBarSet = originale;

        Assert.False(billetta.IsModified);
        Assert.False(edit.HasChanges);
    }

    [Fact]
    public async Task Una_billetta_della_diagnostica_resta_riconoscibile_se_non_si_tocca()
    {
        // EditStatusID = "A" dice che ce l'ha messa la diagnostica: va conservato, altrimenti si
        // perde la distinzione fra un dato rilevato e uno ricostruito.
        _harness.Seed(new Batch
        {
            BatchId = Lotto,
            PressId = "MP1",
            DieId = "R22225/1",
            StartTs = Inizio,
            StopTs = Fine,
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
            EditStatusId = "A",
        });

        var edit = new BatchEditModel(await _harness.BatchServiceFor(Anna).GetDetailAsync(Lotto));

        Assert.Equal("A", edit.Billets[0].EditStatusId);

        edit.Billets[0].MmBarSet = 6500;
        Assert.Equal("M", edit.Billets[0].EditStatusId);
    }

    // ------------------------------------------------------------------ appoggio

    private static NewBilletsRequest Request(
        int count = 1,
        short fromNo = 1,
        decimal kgSheared = 100,
        decimal kgExtruded = 90) =>
        new(
            Count: count,
            FromNo: fromNo,
            From: Inizio,
            To: Fine,
            BarLengthMm: 6000,
            BilletLengthMm: 600,
            KgSheared: kgSheared,
            KgExtruded: kgExtruded,
            ProdId: null,
            CastingId: "F1234",
            AlloyId: "6060");

    private async Task<BatchEditModel> Edit(int billets)
    {
        _harness.Seed(new Batch
        {
            BatchId = Lotto,
            PressId = "MP1",
            DieId = "R22225/1",
            DieCode = "R22225",
            DieNumber = 1,
            StartTs = Inizio,
            StopTs = Fine,
            BilletCount = (short)billets,
            IsBatchProcessed = true,
        });

        for (short n = 1; n <= billets; n++)
        {
            _harness.Seed(new BatchBillet
            {
                BatchId = Lotto,
                PressId = "MP1",
                DieId = "R22225/1",
                TypeId = BatchBilletType.Real,
                BilletNo = n,
                SecCycle = 0,
                MmBarSet = 6000,
                MmBilletAct = 600,
            });
        }

        return new BatchEditModel(await _harness.BatchServiceFor(Anna).GetDetailAsync(Lotto));
    }
}
