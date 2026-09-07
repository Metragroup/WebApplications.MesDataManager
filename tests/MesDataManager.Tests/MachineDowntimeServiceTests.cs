using MesDataManager.Application.Production;
using MesDataManager.Domain.Entities;
using MesDataManager.Tests.Support;

using Microsoft.EntityFrameworkCore;

namespace MesDataManager.Tests;

/// <summary>
/// Regole dei fermi macchina: filtri, periodo massimo per tipo, sovrapposizioni e permessi
/// dell'ambito produzione. La pagina e' nuova e non ha un precedente da confrontare — il vecchio
/// applicativo non limitava il periodo e non rilevava tutte le sovrapposizioni — quindi le regole
/// vanno verificate qui.
/// </summary>
public sealed class MachineDowntimeServiceTests : IDisposable
{
    private const short MacrofermoId = 3;
    private const short MicrofermoId = 4;

    private static readonly DateTime Giorno = new(2026, 9, 1);

    private readonly ProductionHarness _harness = new();

    public MachineDowntimeServiceTests()
    {
        // Gli id non sono 1 e 2 di proposito: la regola sul periodo si abbina alla descrizione
        // del tipo, non al suo id, e un test con gli id "ovvi" non lo dimostrerebbe.
        _harness.Seed(
            new PressDowntimeType
            {
                PressDowntimeTypeId = MacrofermoId,
                Description = MachineDowntimePeriodPolicy.Macrofermo,
            },
            new PressDowntimeType
            {
                PressDowntimeTypeId = MicrofermoId,
                Description = MachineDowntimePeriodPolicy.Microfermo,
            },
            Causale(1, "Guasto elettrico"),
            Causale(2, "Cambio matrice"));
    }

    public void Dispose() => _harness.Dispose();

    // ------------------------------------------------------------------ filtri

    [Fact]
    public async Task Il_tipo_scelto_esclude_gli_altri()
    {
        _harness.Seed(
            Fermo("P01", Giorno.AddHours(8), Giorno.AddHours(9), typeId: MacrofermoId),
            Fermo("P01", Giorno.AddHours(10), Giorno.AddMinutes(601), typeId: MicrofermoId));

        var page = await _harness.ServiceFor(Users.Reader).GetPageAsync(Query());

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(MachineDowntimePeriodPolicy.Macrofermo, Assert.Single(page.Rows).TypeDescription);
    }

    [Fact]
    public async Task Senza_pressa_il_filtro_le_prende_tutte()
    {
        _harness.Seed(
            Fermo("P01", Giorno.AddHours(8), Giorno.AddHours(9)),
            Fermo("P02", Giorno.AddHours(8), Giorno.AddHours(9)));

        var page = await _harness.ServiceFor(Users.Reader).GetPageAsync(Query());

        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task Con_una_pressa_il_filtro_esclude_le_altre()
    {
        _harness.Seed(
            Fermo("P01", Giorno.AddHours(8), Giorno.AddHours(9)),
            Fermo("P02", Giorno.AddHours(8), Giorno.AddHours(9)));

        var page = await _harness.ServiceFor(Users.Reader).GetPageAsync(Query(pressId: "P02"));

        Assert.Equal("P02", Assert.Single(page.Rows).PressId);
    }

    [Fact]
    public async Task La_causale_e_un_filtro_opzionale()
    {
        _harness.Seed(
            Fermo("P01", Giorno.AddHours(8), Giorno.AddHours(9), reasonId: 1),
            Fermo("P01", Giorno.AddHours(10), Giorno.AddHours(11), reasonId: 2));

        var service = _harness.ServiceFor(Users.Reader);

        Assert.Equal(2, (await service.GetPageAsync(Query())).TotalCount);

        var filtrata = await service.GetPageAsync(Query(reasonId: 2));
        Assert.Equal("Cambio matrice", Assert.Single(filtrata.Rows).ReasonDescription);
    }

    [Fact]
    public async Task Un_fermo_a_cavallo_del_periodo_rientra_nel_risultato()
    {
        // Il fermo comincia il giorno prima e finisce dentro il periodo: e' un fermo di quel
        // giorno per chi lo consulta, ed e' il caso che il confronto sulle sole date di inizio
        // perderebbe.
        _harness.Seed(Fermo("P01", Giorno.AddHours(-2), Giorno.AddHours(1)));

        var page = await _harness.ServiceFor(Users.Reader).GetPageAsync(Query());

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task I_fermi_piu_recenti_arrivano_per_primi()
    {
        _harness.Seed(
            Fermo("P01", Giorno.AddHours(8), Giorno.AddHours(9)),
            Fermo("P01", Giorno.AddHours(14), Giorno.AddHours(15)),
            Fermo("P01", Giorno.AddHours(11), Giorno.AddHours(12)));

        var page = await _harness.ServiceFor(Users.Reader).GetPageAsync(Query());

        Assert.Equal(
            [Giorno.AddHours(14), Giorno.AddHours(11), Giorno.AddHours(8)],
            page.Rows.Select(r => r.StartTs));
    }

    [Fact]
    public async Task Le_righe_arrivano_paginate_col_totale_completo()
    {
        // La tabella vera ha 4,59 milioni di righe: la griglia chiede una pagina per volta e il
        // totale serve al paginatore, non al caricamento.
        for (var ora = 0; ora < 5; ora++)
        {
            _harness.Seed(Fermo("P01", Giorno.AddHours(ora), Giorno.AddHours(ora).AddMinutes(30)));
        }

        var page = await _harness.ServiceFor(Users.Reader).GetPageAsync(
            Query() with { PageNumber = 2, PageSize = 2 });

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.Rows.Count);
        Assert.Equal([Giorno.AddHours(2), Giorno.AddHours(1)], page.Rows.Select(r => r.StartTs));
    }

    [Fact]
    public async Task La_durata_arriva_calcolata()
    {
        _harness.Seed(Fermo("P01", Giorno.AddHours(8), Giorno.AddHours(9).AddMinutes(30)));

        var page = await _harness.ServiceFor(Users.Reader).GetPageAsync(Query());

        Assert.Equal(TimeSpan.FromMinutes(90), Assert.Single(page.Rows).Duration);
    }

    // ------------------------------------------------------------------ periodo massimo

    [Fact]
    public async Task Il_periodo_di_un_macrofermo_arriva_a_sette_giorni()
    {
        var service = _harness.ServiceFor(Users.Reader);

        // Sette giorni di calendario, estremi inclusi: dal 1 al 7 settembre.
        await service.GetPageAsync(Query(to: Giorno.AddDays(6)));

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => service.GetPageAsync(Query(to: Giorno.AddDays(7))));

        Assert.Equal(ProductionErrorKind.Validation, errore.Kind);
        Assert.Equal("Error.DowntimePeriodTooWide", errore.MessageKey);
        Assert.Equal([MachineDowntimePeriodPolicy.Macrofermo, 7], errore.MessageArguments);
    }

    [Fact]
    public async Task Il_periodo_di_un_microfermo_e_di_un_giorno_solo()
    {
        var service = _harness.ServiceFor(Users.Reader);

        await service.GetPageAsync(Query(typeId: MicrofermoId));

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => service.GetPageAsync(Query(typeId: MicrofermoId, to: Giorno.AddDays(1))));

        Assert.Equal([MachineDowntimePeriodPolicy.Microfermo, 1], errore.MessageArguments);
    }

    [Fact]
    public async Task La_fine_del_periodo_non_puo_precedere_l_inizio()
    {
        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.ServiceFor(Users.Reader).GetPageAsync(Query(to: Giorno.AddDays(-1))));

        Assert.Equal("Error.PeriodInvalid", errore.MessageKey);
    }

    [Fact]
    public async Task Un_tipo_inesistente_non_passa_per_buono()
    {
        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.ServiceFor(Users.Reader).GetPageAsync(Query(typeId: 99)));

        Assert.Equal("Error.InvalidValue", errore.MessageKey);
    }

    // ------------------------------------------------------------------ scrittura

    [Fact]
    public async Task Un_fermo_nuovo_riceve_codice_stato_e_tipo_di_guasto()
    {
        await _harness.ServiceFor(Users.ProductionWriter).SaveAsync(
            Modifica(start: Giorno.AddHours(8), stop: Giorno.AddHours(9)));

        using var context = _harness.CreateContext();
        var salvato = await context.BatchDowntimes.SingleAsync();

        // Il codice lo genera il servizio come nel vecchio applicativo: pressa + fine del fermo.
        Assert.Equal("P01260901090000", salvato.DowntimeCode);
        Assert.Equal("N", salvato.EditStatusId);
        Assert.Equal(0, salvato.FailureType);
        Assert.Equal(MacrofermoId, salvato.DowntimeType);
    }

    [Fact]
    public async Task La_modifica_marca_la_riga_come_modificata()
    {
        _harness.Seed(Fermo("P01", Giorno.AddHours(8), Giorno.AddHours(9)));
        var id = await PrimoIdAsync();

        await _harness.ServiceFor(Users.ProductionWriter).SaveAsync(
            Modifica(id: id, start: Giorno.AddHours(8), stop: Giorno.AddHours(10), reasonId: 2));

        using var context = _harness.CreateContext();
        var salvato = await context.BatchDowntimes.SingleAsync();

        Assert.Equal(Giorno.AddHours(10), salvato.StopTs);
        Assert.Equal((short)2, salvato.DowntimeReasonId);
        Assert.Equal("M", salvato.EditStatusId);
    }

    [Fact]
    public async Task Due_fermi_sulla_stessa_pressa_non_possono_sovrapporsi()
    {
        _harness.Seed(Fermo("P01", Giorno.AddHours(8), Giorno.AddHours(10)));

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.ServiceFor(Users.ProductionWriter).SaveAsync(
                Modifica(start: Giorno.AddHours(9), stop: Giorno.AddHours(11))));

        Assert.Equal(ProductionErrorKind.PeriodOverlap, errore.Kind);
        Assert.Equal("Error.DowntimePeriodOverlap", errore.MessageKey);
    }

    [Fact]
    public async Task Un_fermo_che_ne_contiene_un_altro_e_una_sovrapposizione()
    {
        // Il vecchio controllo guardava solo se un fermo esistente conteneva l'inizio o la fine
        // del nuovo: un fermo nuovo che ne inghiotte uno esistente gli sfuggiva.
        _harness.Seed(Fermo("P01", Giorno.AddHours(9), Giorno.AddHours(10)));

        await Assert.ThrowsAsync<ProductionException>(
            () => _harness.ServiceFor(Users.ProductionWriter).SaveAsync(
                Modifica(start: Giorno.AddHours(8), stop: Giorno.AddHours(11))));
    }

    [Fact]
    public async Task Due_fermi_consecutivi_non_si_sovrappongono()
    {
        // La fine di uno e l'inizio dell'altro possono coincidere: non c'e' nessun istante
        // coperto da entrambi.
        _harness.Seed(Fermo("P01", Giorno.AddHours(8), Giorno.AddHours(9)));

        await _harness.ServiceFor(Users.ProductionWriter).SaveAsync(
            Modifica(start: Giorno.AddHours(9), stop: Giorno.AddHours(10)));

        using var context = _harness.CreateContext();
        Assert.Equal(2, await context.BatchDowntimes.CountAsync());
    }

    [Fact]
    public async Task Presse_diverse_possono_fermarsi_nello_stesso_momento()
    {
        _harness.Seed(Fermo("P01", Giorno.AddHours(8), Giorno.AddHours(10)));

        await _harness.ServiceFor(Users.ProductionWriter).SaveAsync(
            Modifica(pressId: "P02", start: Giorno.AddHours(8), stop: Giorno.AddHours(10)));

        using var context = _harness.CreateContext();
        Assert.Equal(2, await context.BatchDowntimes.CountAsync());
    }

    [Fact]
    public async Task La_modifica_non_si_sovrappone_a_se_stessa()
    {
        _harness.Seed(Fermo("P01", Giorno.AddHours(8), Giorno.AddHours(10)));
        var id = await PrimoIdAsync();

        await _harness.ServiceFor(Users.ProductionWriter).SaveAsync(
            Modifica(id: id, start: Giorno.AddHours(8).AddMinutes(30), stop: Giorno.AddHours(10)));

        using var context = _harness.CreateContext();
        Assert.Equal(Giorno.AddHours(8).AddMinutes(30), (await context.BatchDowntimes.SingleAsync()).StartTs);
    }

    [Fact]
    public async Task Un_fermo_non_puo_finire_prima_di_cominciare()
    {
        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.ServiceFor(Users.ProductionWriter).SaveAsync(
                Modifica(start: Giorno.AddHours(10), stop: Giorno.AddHours(9))));

        Assert.Equal("Error.PeriodInvalid", errore.MessageKey);
    }

    [Fact]
    public async Task L_eliminazione_rimuove_la_riga()
    {
        _harness.Seed(Fermo("P01", Giorno.AddHours(8), Giorno.AddHours(9)));
        var id = await PrimoIdAsync();

        await _harness.ServiceFor(Users.ProductionWriter).DeleteAsync(id);

        using var context = _harness.CreateContext();
        Assert.Empty(context.BatchDowntimes);
    }

    [Fact]
    public async Task Un_fermo_inesistente_non_si_elimina()
    {
        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.ServiceFor(Users.ProductionWriter).DeleteAsync(4242));

        Assert.Equal(ProductionErrorKind.NotFound, errore.Kind);
    }

    // ------------------------------------------------------------------ permessi

    [Fact]
    public async Task Chi_non_e_autenticato_non_legge()
    {
        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.ServiceFor(Users.Anonymous).GetPageAsync(Query()));

        Assert.Equal(ProductionErrorKind.Forbidden, errore.Kind);
    }

    [Fact]
    public async Task Il_lettore_consulta_e_non_scrive()
    {
        var service = _harness.ServiceFor(Users.Reader);

        await service.GetPageAsync(Query());

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => service.SaveAsync(Modifica(start: Giorno.AddHours(8), stop: Giorno.AddHours(9))));

        Assert.Equal(ProductionErrorKind.Forbidden, errore.Kind);
    }

    [Fact]
    public async Task Il_redattore_delle_anagrafiche_non_scrive_i_fermi()
    {
        // E' la separazione degli ambiti: Archive.Editor ha inserimento, modifica ed
        // eliminazione sulle anagrafiche e nessuna scrittura sulla produzione.
        var service = _harness.ServiceFor(Users.Writer);

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => service.SaveAsync(Modifica(start: Giorno.AddHours(8), stop: Giorno.AddHours(9))));

        Assert.Equal(ProductionErrorKind.Forbidden, errore.Kind);
    }

    [Fact]
    public async Task L_amministratore_scrive_i_fermi_senza_il_ruolo_di_ambito()
    {
        await _harness.ServiceFor(Users.Administrator).SaveAsync(
            Modifica(start: Giorno.AddHours(8), stop: Giorno.AddHours(9)));

        using var context = _harness.CreateContext();
        Assert.Equal(1, await context.BatchDowntimes.CountAsync());
    }

    [Fact]
    public void Il_trigger_di_BatchDowntime_e_dichiarato_nel_modello()
    {
        // Press.BatchDowntime ha un trigger su INSERT/UPDATE/DELETE. Da EF Core 7 il
        // salvataggio rilegge la chiave generata con una clausola OUTPUT, che SQL Server
        // rifiuta sulle tabelle con trigger: senza la dichiarazione nel modello ogni scrittura
        // falliva in esercizio con "Salvataggio non riuscito", mentre le letture funzionavano.
        //
        // Il test guarda il modello e non il database perche' SQLite non ha trigger: e'
        // l'unico modo di far fallire qui una regressione che altrimenti si scopre solo
        // provando a salvare su SQL Server.
        using var context = _harness.CreateContext();

        var entity = context.Model.FindEntityType(typeof(BatchDowntime));
        Assert.NotNull(entity);

        var triggers = entity.GetDeclaredTriggers().Select(t => t.ModelName).ToList();

        Assert.Contains("TR_Press_BatchDowntime", triggers);
    }

    // ------------------------------------------------------------------ fermi di un lotto

    [Fact]
    public async Task La_scheda_del_lotto_prende_i_macrofermi_sovrapposti()
    {
        _harness.Seed(
            Fermo("P01", Giorno.AddHours(7), Giorno.AddHours(9)),
            Fermo("P01", Giorno.AddHours(11), Giorno.AddHours(12)),
            Fermo("P01", Giorno.AddHours(20), Giorno.AddHours(21)));

        var fermi = await _harness.ServiceFor(Users.Reader).GetForBatchAsync(
            "P01", Giorno.AddHours(8), Giorno.AddHours(14));

        // Il primo fermo comincia prima del lotto ma lo tocca, il terzo e' fuori finestra.
        Assert.Equal(
            [Giorno.AddHours(11), Giorno.AddHours(7)],
            fermi.Select(f => f.StartTs));
    }

    [Fact]
    public async Task La_scheda_del_lotto_non_mostra_i_microfermi()
    {
        // In un turno i microfermi sono centinaia: la scheda del lotto ne resterebbe sommersa,
        // e anche il vecchio applicativo mostrava solo i macrofermi.
        _harness.Seed(
            Fermo("P01", Giorno.AddHours(9), Giorno.AddHours(10), typeId: MacrofermoId),
            Fermo("P01", Giorno.AddHours(9), Giorno.AddMinutes(541), typeId: MicrofermoId));

        var fermi = await _harness.ServiceFor(Users.Reader).GetForBatchAsync(
            "P01", Giorno.AddHours(8), Giorno.AddHours(14));

        Assert.Equal(
            MachineDowntimePeriodPolicy.Macrofermo,
            Assert.Single(fermi).TypeDescription);
    }

    [Fact]
    public async Task La_scheda_del_lotto_non_mostra_i_fermi_di_altre_presse()
    {
        _harness.Seed(
            Fermo("P01", Giorno.AddHours(9), Giorno.AddHours(10)),
            Fermo("P02", Giorno.AddHours(9), Giorno.AddHours(10)));

        var fermi = await _harness.ServiceFor(Users.Reader).GetForBatchAsync(
            "P01", Giorno.AddHours(8), Giorno.AddHours(14));

        Assert.Equal("P01", Assert.Single(fermi).PressId);
    }

    [Fact]
    public async Task I_fermi_di_un_lotto_non_hanno_limite_di_periodo()
    {
        // Sull'elenco il periodo massimo e' obbligatorio; qui la finestra la impone il lotto,
        // quindi un lotto lungo piu' di sette giorni non deve essere rifiutato.
        _harness.Seed(Fermo("P01", Giorno.AddHours(9), Giorno.AddHours(10)));

        var fermi = await _harness.ServiceFor(Users.Reader).GetForBatchAsync(
            "P01", Giorno, Giorno.AddDays(30));

        Assert.Single(fermi);
    }

    [Fact]
    public async Task Chi_non_e_autenticato_non_legge_i_fermi_di_un_lotto()
    {
        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.ServiceFor(Users.Anonymous).GetForBatchAsync(
                "P01", Giorno, Giorno.AddHours(1)));

        Assert.Equal(ProductionErrorKind.Forbidden, errore.Kind);
    }

    // ------------------------------------------------------------------ appoggio

    private static MachineDowntimeQuery Query(
        string? pressId = null,
        short typeId = MacrofermoId,
        short? reasonId = null,
        DateTime? from = null,
        DateTime? to = null) =>
        new(pressId, typeId, reasonId, from ?? Giorno, to ?? Giorno);

    private static MachineDowntimeEditModel Modifica(
        int? id = null,
        string pressId = "P01",
        DateTime start = default,
        DateTime stop = default,
        short reasonId = 1,
        short typeId = MacrofermoId) =>
        new(id, pressId, start, stop, reasonId, typeId);

    private static BatchDowntime Fermo(
        string pressId,
        DateTime start,
        DateTime stop,
        short reasonId = 1,
        short typeId = MacrofermoId) =>
        new()
        {
            PressId = pressId,
            StartTs = start,
            StopTs = stop,
            DowntimeReasonId = reasonId,
            DowntimeType = (byte)typeId,
            DowntimeCode = $"{pressId}{stop:yyMMddHHmmss}",
        };

    private static PressDowntimeReason Causale(short id, string description) =>
        new()
        {
            PressDowntimeReasonId = id,
            Description = description,
            IsActive = true,
            IsActiveMaster = true,
        };

    private async Task<int> PrimoIdAsync()
    {
        using var context = _harness.CreateContext();
        return await context.BatchDowntimes.Select(d => d.BatchDowntimeId).SingleAsync();
    }
}
