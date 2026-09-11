using MesDataManager.Application.Production;
using MesDataManager.Application.Security;
using MesDataManager.Domain.Entities;
using MesDataManager.Tests.Support;

namespace MesDataManager.Tests;

/// <summary>
/// Il salvataggio del lotto: cosa finisce a database e cosa no.
/// <para>
/// Il ricalcolo dei valori di riepilogo non si verifica qui — lo fa <c>usp_Batch_Elab</c>, che
/// su SQLite non esiste. Qui si verifica che l'applicazione scriva <b>solo</b> quello che
/// l'operatore ha modificato, che i marcatori seguano le billette e che un blocco perduto fermi
/// tutto.
/// </para>
/// </summary>
public sealed class BatchSaveTests : IDisposable
{
    private const string Lotto = "MP1260901080000";
    private static readonly DateTime Inizio = new(2026, 9, 1, 8, 0, 0);
    private static readonly DateTime Fine = new(2026, 9, 1, 9, 0, 0);

    private static readonly UserPermissions Anna = Utente("anna.rossi@metra.it");
    private static readonly UserPermissions Bruno = Utente("bruno.verdi@metra.it");

    private readonly ProductionHarness _harness = new();

    public BatchSaveTests()
    {
        _harness.Seed(new Company { CompanyId = "MET1", Description = "Metra", IsActive = true });
        _harness.Seed(new Press
        {
            PressId = "MP1",
            Description = "MP1",
            CompanyId = "MET1",
            SawOprId = "SAW",
            SawWrkCtrId = "SAW",
            HasMes = true,
        });

        _harness.Seed(new PressBatchClosingReason
        {
            PressBatchClosingReasonId = 7,
            Description = "Fine ordine",
            Result = "OK",
            IsActive = true,
            IsActiveMaster = true,
        });
    }

    public void Dispose() => _harness.Dispose();

    // ------------------------------------------------------------------ testata

    [Fact]
    public async Task La_matrice_nuova_arriva_sulla_testata_e_su_tutte_le_billette()
    {
        // Marcatori compresi: il vecchio applicativo li lasciava indietro, e restavano con la
        // matrice vecchia mentre il lotto ne dichiarava un'altra.
        var (service, edit) = await Editing();
        edit.ChangeDie(new DieValidation("R30000/2", "R30000", 2, DieCheck.Available));

        await service.SaveAsync(edit);

        await using var context = _harness.CreateContext();
        var batch = context.Batches.Single();

        Assert.Equal("R30000/2", batch.DieId);
        Assert.Equal("R30000", batch.DieCode);
        Assert.Equal((short)2, batch.DieNumber);
        Assert.All(context.BatchBillets.ToList(), b => Assert.Equal("R30000/2", b.DieId));
    }

    [Fact]
    public async Task La_causale_di_chiusura_arriva_anche_sul_marcatore_di_chiusura()
    {
        var (service, edit) = await Editing();
        edit.ChangeClosingReason(9);

        await service.SaveAsync(edit);

        await using var context = _harness.CreateContext();

        Assert.Equal((short)9, context.Batches.Single().PressBatchClosingReasonId);
        Assert.Equal(
            (byte)9,
            context.BatchBillets.Single(b => b.TypeId == BatchBilletType.BatchStop).ClosingReasonId);
    }

    [Fact]
    public async Task Senza_modifiche_il_salvataggio_rilascia_soltanto_il_blocco()
    {
        var (service, edit) = await Editing();

        await service.SaveAsync(edit);

        await using var context = _harness.CreateContext();
        var batch = context.Batches.Single();

        Assert.False(batch.IsLock);
        Assert.Null(batch.LockUsr);
        Assert.Null(batch.LockTs);
        Assert.Equal("R22225/1", batch.DieId);
    }

    // ------------------------------------------------------------------ billette

    [Fact]
    public async Task Una_billetta_modificata_si_scrive_e_diventa_M()
    {
        var (service, edit) = await Editing();
        var billetta = edit.Billets.Single(b => b.BilletNo == 2);
        billetta.MmBarSet = 6500;
        billetta.Billet1Kg = 120;

        await service.SaveAsync(edit);

        await using var context = _harness.CreateContext();
        var salvata = context.BatchBillets.Single(b => b.BilletNo == 2 && b.TypeId == BatchBilletType.Real);

        Assert.Equal(6500m, salvata.MmBarSet);
        Assert.Equal("M", salvata.EditStatusId?.Trim());

        // I kg cesoiati sono derivati: somma dei due tronconi, calcolata dal salvataggio.
        Assert.Equal(120m, salvata.KgSheared);
    }

    [Fact]
    public async Task Una_billetta_non_toccata_non_viene_riscritta()
    {
        var (service, edit) = await Editing();
        edit.Billets.Single(b => b.BilletNo == 2).MmBarSet = 6500;

        await service.SaveAsync(edit);

        await using var context = _harness.CreateContext();
        var intatta = context.BatchBillets.Single(b => b.BilletNo == 1 && b.TypeId == BatchBilletType.Real);

        // Lo stato di modifica resta vuoto: se il salvataggio riscrivesse tutte le righe, ogni
        // billetta del lotto risulterebbe modificata a mano.
        Assert.True(string.IsNullOrWhiteSpace(intatta.EditStatusId));
    }

    [Fact]
    public async Task Le_billette_nuove_si_inseriscono_con_i_valori_derivati()
    {
        var (service, edit) = await Editing();
        edit.AddBillets(new NewBilletsRequest(
            Count: 2,
            FromNo: 3,
            From: Fine,
            To: Fine.AddMinutes(20),
            BarLengthMm: 6000,
            BilletLengthMm: 600,
            KgSheared: 200,
            KgExtruded: 180,
            ProdId: "OP1",
            CastingId: "F1234",
            AlloyId: "6060"));

        await service.SaveAsync(edit);

        await using var context = _harness.CreateContext();
        var nuove = context.BatchBillets
            .Where(b => b.TypeId == BatchBilletType.Real && b.BilletNo >= 3)
            .OrderBy(b => b.BilletNo)
            .ToList();

        Assert.Equal(2, nuove.Count);
        Assert.All(nuove, b => Assert.Equal("N", b.EditStatusId?.Trim()));
        Assert.All(nuove, b => Assert.Equal(-1, b.BatchBilletRawId));
        Assert.All(nuove, b => Assert.Equal("MP1", b.PressId));
        Assert.All(nuove, b => Assert.Equal("R22225/1", b.DieId));
        Assert.All(nuove, b => Assert.Equal(100m, b.KgSheared));
        Assert.All(nuove, b => Assert.Equal(600, b.SecCycle));
    }

    [Fact]
    public async Task Le_billette_rimosse_si_cancellano()
    {
        var (service, edit) = await Editing();
        edit.RemoveBillets([edit.Billets.Single(b => b.BilletNo == 1).LocalId]);

        await service.SaveAsync(edit);

        await using var context = _harness.CreateContext();
        var rimaste = context.BatchBillets
            .Where(b => b.TypeId == BatchBilletType.Real)
            .Select(b => b.BilletNo)
            .ToList();

        Assert.Equal([(short)2], rimaste);
    }

    [Fact]
    public async Task I_marcatori_seguono_la_prima_e_l_ultima_billetta()
    {
        var (service, edit) = await Editing();

        // Il lotto si allunga da entrambe le parti: mezz'ora prima e mezz'ora dopo.
        var prima = edit.Billets.Single(b => b.BilletNo == 1);
        prima.StartTs = Inizio.AddMinutes(-30);

        var ultima = edit.Billets.Single(b => b.BilletNo == 2);
        ultima.StopTs = Fine.AddMinutes(30);

        await service.SaveAsync(edit);

        await using var context = _harness.CreateContext();
        var apertura = context.BatchBillets.Single(b => b.TypeId == BatchBilletType.BatchStart);
        var chiusura = context.BatchBillets.Single(b => b.TypeId == BatchBilletType.BatchStop);

        Assert.Equal(Inizio.AddMinutes(-30), apertura.StartTs);
        Assert.Equal(Inizio.AddMinutes(-30), apertura.StopTs);
        Assert.Equal(Fine.AddMinutes(30), chiusura.StartTs);
        Assert.Equal(Fine.AddMinutes(30), chiusura.StopTs);
        Assert.Equal("N", apertura.EditStatusId?.Trim());
    }

    [Fact]
    public async Task I_marcatori_restano_fermi_se_le_billette_non_si_spostano()
    {
        var (service, edit) = await Editing();
        edit.Billets.Single(b => b.BilletNo == 2).MmBarSet = 6500;

        await service.SaveAsync(edit);

        await using var context = _harness.CreateContext();
        var apertura = context.BatchBillets.Single(b => b.TypeId == BatchBilletType.BatchStart);

        Assert.Equal(Inizio, apertura.StartTs);
        Assert.True(string.IsNullOrWhiteSpace(apertura.EditStatusId));
    }

    [Fact]
    public async Task Rimuovendo_la_prima_billetta_il_marcatore_si_sposta_sulla_successiva()
    {
        var (service, edit) = await Editing();
        edit.RemoveBillets([edit.Billets.Single(b => b.BilletNo == 1).LocalId]);

        await service.SaveAsync(edit);

        await using var context = _harness.CreateContext();
        var apertura = context.BatchBillets.Single(b => b.TypeId == BatchBilletType.BatchStart);
        var seconda = context.BatchBillets.Single(b => b.BilletNo == 2 && b.TypeId == BatchBilletType.Real);

        Assert.Equal(seconda.StartTs, apertura.StartTs);
    }

    // ------------------------------------------------------------------ rettifiche

    [Fact]
    public async Task Le_rettifiche_si_inseriscono_col_loro_segno()
    {
        var (service, edit) = await Editing();
        edit.AddAdjustment(6000, "op9", -4);

        await service.SaveAsync(edit);

        await using var context = _harness.CreateContext();
        var rettifica = context.BatchBarQties.Single();

        Assert.Equal(-4, rettifica.Qty);
        Assert.Equal(6000m, rettifica.BarLength);
        Assert.Equal("OP9", rettifica.ProdId);
        Assert.Equal("MP1", rettifica.PressId);
        Assert.Equal(Lotto, rettifica.BatchId);
    }

    [Fact]
    public async Task Una_rettifica_si_modifica_e_si_cancella()
    {
        // Il lotto va creato prima della rettifica: la chiave esterna verso Press.Batch esiste
        // davvero a database, e SQLite la fa rispettare come SQL Server.
        await Editing();

        _harness.Seed(new BatchBarQty
        {
            BatchId = Lotto,
            PressId = "MP1",
            BarLength = 4000,
            Qty = 2,
            CreatedTs = Inizio,
        });

        var (service, edit) = await Editing();
        edit.Adjustments[0].Qty = 5;

        await service.SaveAsync(edit);

        await using (var context = _harness.CreateContext())
        {
            Assert.Equal(5, context.BatchBarQties.Single().Qty);
        }

        var (service2, edit2) = await Editing();
        edit2.RemoveAdjustments([edit2.Adjustments[0].LocalId]);

        await service2.SaveAsync(edit2);

        await using (var context = _harness.CreateContext())
        {
            Assert.Empty(context.BatchBarQties);
        }
    }

    // ------------------------------------------------------------------ coda di elaborazione

    [Fact]
    public async Task Il_salvataggio_rimette_il_lotto_in_coda_di_elaborazione()
    {
        // I valori di riepilogo li ricalcola usp_Batch_Elab, che costa 35 secondi per lotto e
        // gira gia' ogni cinque minuti sul MES su tutti i lotti con IsBatchProcessed = 0. Il
        // salvataggio quindi non la chiama: rimette il lotto in quella coda, e l'operatore non
        // aspetta quaranta secondi.
        var (service, edit) = await Editing();
        edit.ChangeClosingReason(9);

        var detail = await service.SaveAsync(edit);

        await using var context = _harness.CreateContext();
        Assert.False(context.Batches.Single().IsBatchProcessed);

        // E la fotografia restituita lo dice: e' quella che la scheda mostra subito dopo.
        Assert.False(detail.IsBatchProcessed);
    }

    [Fact]
    public async Task Un_lotto_in_coda_di_elaborazione_non_si_riprende_in_modifica()
    {
        // Conseguenza voluta della scelta di sopra: finche' il MES non ha ricalcolato, i valori
        // a schermo non sono quelli veri e modificarli significherebbe lavorare su numeri che
        // stanno per cambiare.
        var (service, edit) = await Editing();
        edit.ChangeClosingReason(9);
        await service.SaveAsync(edit);

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Anna).BeginEditAsync(Lotto));

        Assert.Equal(ProductionErrorKind.Conflict, errore.Kind);
        Assert.EndsWith("BatchProcessing", errore.MessageKey, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ blocco e permessi

    [Fact]
    public async Task Un_blocco_forzato_nel_frattempo_fa_fallire_il_salvataggio()
    {
        var (service, edit) = await Editing();
        edit.Billets[0].MmBarSet = 6500;

        // Mentre Anna modifica, un amministratore le porta via il lotto.
        await _harness.BatchServiceFor(Users.Administrator).ForceUnlockAsync(Lotto);

        var errore = await Assert.ThrowsAsync<ProductionException>(() => service.SaveAsync(edit));

        Assert.Equal(ProductionErrorKind.Conflict, errore.Kind);

        await using var context = _harness.CreateContext();
        Assert.Equal(6000m, context.BatchBillets.First(b => b.TypeId == BatchBilletType.Real).MmBarSet);
    }

    [Fact]
    public async Task Un_blocco_passato_a_un_altro_fa_fallire_il_salvataggio()
    {
        var (service, edit) = await Editing();
        edit.Billets[0].MmBarSet = 6500;

        await _harness.BatchServiceFor(Users.Administrator).ForceUnlockAsync(Lotto);
        await _harness.BatchServiceFor(Bruno).BeginEditAsync(Lotto);

        var errore = await Assert.ThrowsAsync<ProductionException>(() => service.SaveAsync(edit));

        Assert.Contains("bruno.verdi@metra.it", errore.MessageArguments[0]?.ToString());
    }

    [Fact]
    public async Task Chi_consulta_soltanto_non_salva()
    {
        var (_, edit) = await Editing();

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Users.Reader).SaveAsync(edit));

        Assert.Equal(ProductionErrorKind.Forbidden, errore.Kind);
    }

    [Fact]
    public async Task Un_lotto_riconciliato_nel_frattempo_non_si_salva()
    {
        var (service, edit) = await Editing();
        edit.Billets[0].MmBarSet = 6500;

        await using (var context = _harness.CreateContext())
        {
            context.Batches.Single().IsErpImported = true;
            await context.SaveChangesAsync();
        }

        var errore = await Assert.ThrowsAsync<ProductionException>(() => service.SaveAsync(edit));

        Assert.Equal(ProductionErrorKind.Conflict, errore.Kind);
    }

    // ------------------------------------------------------------------ presse senza MES

    [Fact]
    public async Task Su_una_pressa_senza_mes_la_diagnostica_decide_le_chiusure()
    {
        await SetHasMes(false);
        await SetDiagnostics(DiagnosticsOutcome.Ok);

        var (service, edit) = await Editing();
        edit.Billets[0].MmBarSet = 6500;

        await service.SaveAsync(edit);

        await using var context = _harness.CreateContext();
        var batch = context.Batches.Single();

        Assert.True(batch.IsPressClosed);
        Assert.True(batch.IsSawClosed);
    }

    [Fact]
    public async Task Su_una_pressa_senza_mes_una_diagnostica_in_errore_riapre_le_chiusure()
    {
        await SetHasMes(false);
        await SetDiagnostics(DiagnosticsOutcome.Error);
        await SetClosed(true);

        var (service, edit) = await Editing();
        edit.Billets[0].MmBarSet = 6500;

        await service.SaveAsync(edit);

        await using var context = _harness.CreateContext();
        var batch = context.Batches.Single();

        Assert.False(batch.IsPressClosed);
        Assert.False(batch.IsSawClosed);
    }

    [Fact]
    public async Task Su_una_pressa_con_mes_le_chiusure_non_si_toccano()
    {
        await SetDiagnostics(DiagnosticsOutcome.Error);
        await SetClosed(true);

        var (service, edit) = await Editing();
        edit.Billets[0].MmBarSet = 6500;

        await service.SaveAsync(edit);

        await using var context = _harness.CreateContext();
        var batch = context.Batches.Single();

        Assert.True(batch.IsPressClosed);
        Assert.True(batch.IsSawClosed);
    }

    // ------------------------------------------------------------------ appoggio

    private static UserPermissions Utente(string upn) =>
        new()
        {
            UserName = upn,
            IsAuthenticated = true,
            CanRead = true,
            CanEditProduction = true,
        };

    /// <summary>
    /// Lotto con due billette vere e i due marcatori, preso in modifica da Anna: e' lo stato di
    /// partenza di ogni prova di salvataggio.
    /// </summary>
    private async Task<(IBatchService Service, BatchEditModel Edit)> Editing()
    {
        if (!_harness.CreateContext().Batches.Any())
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
                BilletCount = 2,
                IsBatchProcessed = true,
                PressBatchClosingReasonId = 7,
            });

            _harness.Seed(
                Billetta(BatchBilletType.BatchStart, 0, Inizio, Inizio),
                Billetta(BatchBilletType.Real, 1, Inizio, Inizio.AddMinutes(30)),
                Billetta(BatchBilletType.Real, 2, Inizio.AddMinutes(30), Fine),
                Billetta(BatchBilletType.BatchStop, 0, Fine, Fine));
        }

        // Il lavoro pianificato del MES, che qui non c'e'. Dopo un salvataggio il lotto resta in
        // coda di elaborazione e non si potrebbe riprendere in modifica: e' il comportamento
        // voluto (vedi Il_salvataggio_rimette_il_lotto_in_coda_di_elaborazione), e a database lo
        // scioglie usp_Batch_Elab entro cinque minuti. Le prove che salvano due volte lo fanno a
        // mano, altrimenti verificherebbero il lavoro pianificato invece del salvataggio.
        await using (var context = _harness.CreateContext())
        {
            var batch = context.Batches.Single(b => b.BatchId == Lotto);
            batch.IsBatchProcessed = true;
            await context.SaveChangesAsync();
        }

        var service = _harness.BatchServiceFor(Anna);
        var detail = await service.BeginEditAsync(Lotto);

        return (service, new BatchEditModel(detail));
    }

    private static BatchBillet Billetta(byte typeId, short billetNo, DateTime start, DateTime stop) =>
        new()
        {
            BatchId = Lotto,
            PressId = "MP1",
            DieId = "R22225/1",
            TypeId = typeId,
            BilletNo = billetNo,
            StartTs = start,
            StopTs = stop,
            SecCycle = (int)(stop - start).TotalSeconds,
            MmBarSet = 6000,
            MmBilletAct = 600,
        };

    private async Task SetHasMes(bool hasMes)
    {
        await using var context = _harness.CreateContext();
        context.Presses.Single().HasMes = hasMes;
        await context.SaveChangesAsync();
    }

    private async Task SetDiagnostics(string status)
    {
        await Editing();

        await using var context = _harness.CreateContext();
        context.Batches.Single().UsrDiagStatus = status;
        await context.SaveChangesAsync();
    }

    private async Task SetClosed(bool closed)
    {
        await using var context = _harness.CreateContext();
        var batch = context.Batches.Single();
        batch.IsPressClosed = closed;
        batch.IsSawClosed = closed;
        await context.SaveChangesAsync();
    }
}
