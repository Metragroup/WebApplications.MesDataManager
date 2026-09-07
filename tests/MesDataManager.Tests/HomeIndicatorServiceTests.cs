using MesDataManager.Application.Production;
using MesDataManager.Domain.Entities;
using MesDataManager.Tests.Support;

namespace MesDataManager.Tests;

/// <summary>
/// Indicatori del pannello di apertura. La parte delicata e' l'attribuzione dei fermi al turno:
/// <c>BatchDowntime</c> non porta ne' turno ne' lotto, solo pressa e istanti, e i fermi capitano
/// spesso fra una billetta e l'altra. Le finestre dei turni arrivano quindi dal calendario
/// dell'impianto, che qui e' sostituito da uno finto: la funzione di SQL Server che lo fornisce
/// non esiste su SQLite, mentre l'aggregazione va verificata riga per riga.
/// </summary>
public sealed class HomeIndicatorServiceTests : IDisposable
{
    private const short MacrofermoId = 3;
    private const short MicrofermoId = 4;

    private static readonly DateOnly Giorno = new(2026, 9, 7);

    /// <summary>Due turni di otto ore contigui su una pressa, come li darebbe l'impianto.</summary>
    private static readonly ShiftWindow PrimoTurno = new(
        "MP5", "T1", new DateTime(2026, 9, 7, 6, 0, 0), new DateTime(2026, 9, 7, 14, 0, 0));

    private static readonly ShiftWindow SecondoTurno = new(
        "MP5", "T2", new DateTime(2026, 9, 7, 14, 0, 0), new DateTime(2026, 9, 7, 22, 0, 0));

    private readonly ProductionHarness _harness = new();

    public HomeIndicatorServiceTests()
    {
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
            });

        _harness.Seed(new Company { CompanyId = "MET1", Description = "Metra" });
        _harness.Seed(Pressa("MP1"), Pressa("MP5"));
    }

    public void Dispose() => _harness.Dispose();

    // ------------------------------------------------------------------ attivita' in corso

    [Fact]
    public async Task I_lotti_aperti_si_contano_per_pressa()
    {
        SeedLotto("MP5260907080000", "MP5");
        SeedLotto("MP5260907090000", "MP5");
        SeedLotto("MP1260907080000", "MP1");

        var indicators = await ServiceFor(Users.Reader).GetAsync(Giorno);

        Assert.Equal(2, indicators.Activity.Count);
        Assert.Equal(1, indicators.Activity.Single(a => a.PressId == "MP1").OpenBatches);
        Assert.Equal(2, indicators.Activity.Single(a => a.PressId == "MP5").OpenBatches);
    }

    [Fact]
    public async Task Il_conteggio_degli_aperti_segue_le_regole_della_pagina_dei_lotti()
    {
        // Un indicatore che contasse lotti che la pagina non mostra darebbe un numero che,
        // cliccato, apre un elenco piu' corto: lotti fantasma e lotti gia' riconciliati restano
        // fuori da entrambi.
        SeedLotto("MP5260907080000", "MP5");
        _harness.Seed(Lotto("MP5260907090000", "MP5"));                       // senza billette vere
        SeedLotto("MP5260907100000", "MP5", erpImported: true);               // gia' riconciliato
        SeedLotto("MP5260907110000", "MP5", pressClosed: true, sawClosed: true); // concluso

        var indicators = await ServiceFor(Users.Reader).GetAsync(Giorno);

        Assert.Equal(1, indicators.Activity.Single().OpenBatches);
    }

    [Fact]
    public async Task I_lotti_da_riconciliare_sono_quelli_segnati_e_non_ancora_importati()
    {
        SeedLotto("MP5260907080000", "MP5", pressClosed: true, sawClosed: true, erpMarked: true);
        SeedLotto("MP5260907090000", "MP5", pressClosed: true, sawClosed: true, erpMarked: true, erpImported: true);

        var indicators = await ServiceFor(Users.Reader).GetAsync(Giorno);

        var press = indicators.Activity.Single();
        Assert.Equal(1, press.ToReconcile);
        Assert.Equal(0, press.OpenBatches);
    }

    [Fact]
    public async Task Una_pressa_senza_niente_da_mostrare_non_compare()
    {
        SeedLotto("MP5260907080000", "MP5");

        var indicators = await ServiceFor(Users.Reader).GetAsync(Giorno);

        Assert.Equal("MP5", indicators.Activity.Single().PressId);
    }

    // ------------------------------------------------------------------ fermi per turno

    [Fact]
    public async Task I_fermi_si_contano_e_si_sommano_per_pressa_e_turno()
    {
        _harness.Seed(
            Fermo("MP5", At(7, 0), At(7, 10)),
            Fermo("MP5", At(9, 0), At(9, 5)),
            Fermo("MP5", At(15, 0), At(15, 30)));

        var indicators = await ServiceFor(Users.Reader).GetAsync(Giorno);

        var primo = Turno(indicators.Macro, "T1");
        Assert.Equal(2, primo.Count);
        Assert.Equal(TimeSpan.FromMinutes(15), primo.Total);

        var secondo = Turno(indicators.Macro, "T2");
        Assert.Equal(1, secondo.Count);
        Assert.Equal(TimeSpan.FromMinutes(30), secondo.Total);
    }

    [Fact]
    public async Task Ogni_turno_della_giornata_compare_anche_senza_fermi()
    {
        // La scheda della pressa mostra tutti i turni: uno zero dice "nessun fermo", un turno
        // assente lascerebbe il dubbio che il dato manchi.
        _harness.Seed(Fermo("MP5", At(7, 0), At(7, 10)));

        var indicators = await ServiceFor(Users.Reader).GetAsync(Giorno);

        Assert.Equal(["T1", "T2"], indicators.Macro.Single().Shifts.Select(s => s.ShiftId));
        Assert.Equal(["T1", "T2"], indicators.Micro.Single().Shifts.Select(s => s.ShiftId));

        var secondo = Turno(indicators.Macro, "T2");
        Assert.Equal(0, secondo.Count);
        Assert.Equal(TimeSpan.Zero, secondo.Total);
    }

    [Fact]
    public async Task Macrofermi_e_microfermi_restano_due_indicatori_distinti()
    {
        _harness.Seed(
            Fermo("MP5", At(7, 0), At(7, 10)),
            Fermo("MP5", At(8, 0), At(8, 0).AddSeconds(20), MicrofermoId),
            Fermo("MP5", At(8, 5), At(8, 5).AddSeconds(40), MicrofermoId));

        var indicators = await ServiceFor(Users.Reader).GetAsync(Giorno);

        Assert.Equal(1, Turno(indicators.Macro, "T1").Count);
        Assert.Equal(2, Turno(indicators.Micro, "T1").Count);
        Assert.Equal(TimeSpan.FromSeconds(60), Turno(indicators.Micro, "T1").Total);
    }

    [Fact]
    public async Task Un_fermo_fra_due_billette_viene_contato_comunque()
    {
        // E' il motivo per cui l'attribuzione passa dalle finestre dei turni e non dalle
        // billette: il cambio billetta e il cambio matrice avvengono quando non si estrude, e
        // sono proprio i fermi che interessano.
        _harness.Seed(Fermo("MP5", At(10, 0), At(10, 30)));

        var indicators = await ServiceFor(Users.Reader).GetAsync(Giorno);

        Assert.Equal(1, Turno(indicators.Macro, "T1").Count);
    }

    [Fact]
    public async Task L_istante_di_confine_appartiene_al_turno_che_comincia()
    {
        // Le due finestre si toccano alle 14:00: un fermo che comincia in quel momento e' del
        // secondo turno, e non deve essere contato due volte.
        _harness.Seed(Fermo("MP5", At(14, 0), At(14, 5)));

        var indicators = await ServiceFor(Users.Reader).GetAsync(Giorno);

        Assert.Equal(0, Turno(indicators.Macro, "T1").Count);
        Assert.Equal(1, Turno(indicators.Macro, "T2").Count);
    }

    [Fact]
    public async Task I_fermi_fuori_dalla_giornata_non_rientrano()
    {
        _harness.Seed(
            Fermo("MP5", At(5, 0), At(5, 30)),
            Fermo("MP5", At(23, 0), At(23, 30)));

        var indicators = await ServiceFor(Users.Reader).GetAsync(Giorno);

        Assert.Equal(0, indicators.Macro.Single().Count);
    }

    [Fact]
    public async Task I_fermi_di_una_pressa_senza_turni_non_rientrano()
    {
        // Il calendario finto copre solo MP5: MP1 quel giorno non ha turni, quindi non ha
        // nemmeno una scheda, e i suoi fermi non finiscono in quella di un'altra pressa.
        _harness.Seed(Fermo("MP1", At(7, 0), At(7, 10)));

        var indicators = await ServiceFor(Users.Reader).GetAsync(Giorno);

        Assert.Equal(["MP5"], indicators.Macro.Select(r => r.PressId));
        Assert.Equal(0, indicators.Macro.Single().Count);
    }

    [Fact]
    public async Task Un_fermo_con_fine_precedente_all_inizio_non_sottrae_durata()
    {
        // Sui dati veri capita: la durata di una riga incoerente vale zero, non un negativo che
        // falserebbe il totale del turno.
        _harness.Seed(
            Fermo("MP5", At(7, 0), At(7, 10)),
            Fermo("MP5", At(8, 0), At(7, 50)));

        var indicators = await ServiceFor(Users.Reader).GetAsync(Giorno);

        Assert.Equal(2, Turno(indicators.Macro, "T1").Count);
        Assert.Equal(TimeSpan.FromMinutes(10), Turno(indicators.Macro, "T1").Total);
    }

    [Fact]
    public async Task Senza_turni_nella_giornata_i_fermi_non_si_calcolano()

    {
        _harness.Seed(Fermo("MP5", At(7, 0), At(7, 10)));

        var indicators = await ServiceFor(Users.Reader, shifts: []).GetAsync(Giorno);

        Assert.Empty(indicators.Macro);
        Assert.Empty(indicators.Micro);
    }

    // ------------------------------------------------------------------ permessi

    [Fact]
    public async Task Chi_non_e_autenticato_non_vede_gli_indicatori()
    {
        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => ServiceFor(Users.Anonymous).GetAsync(Giorno));

        Assert.Equal(ProductionErrorKind.Forbidden, errore.Kind);
    }

    // ------------------------------------------------------------------ appoggio

    private IHomeIndicatorService ServiceFor(
        Application.Security.UserPermissions permissions,
        ShiftWindow[]? shifts = null) =>
        _harness.HomeIndicatorServiceFor(permissions, shifts ?? [PrimoTurno, SecondoTurno]);

    private static PressDowntimeTotals Pressa(IReadOnlyList<PressDowntimeTotals> rows, string pressId) =>
        rows.Single(r => r.PressId == pressId);

    private static ShiftDowntimeTotals Turno(IReadOnlyList<PressDowntimeTotals> rows, string shiftId) =>
        rows.Single().Shifts.Single(s => s.ShiftId == shiftId);

    private static DateTime At(int hour, int minute) =>
        new(Giorno.Year, Giorno.Month, Giorno.Day, hour, minute, 0);

    private void SeedLotto(
        string batchId,
        string pressId,
        bool pressClosed = false,
        bool sawClosed = false,
        bool erpMarked = false,
        bool erpImported = false)
    {
        _harness.Seed(Lotto(batchId, pressId, pressClosed, sawClosed, erpMarked, erpImported));
        _harness.Seed(new BatchBillet
        {
            BatchId = batchId,
            PressId = pressId,
            DieId = "R22225/1",
            TypeId = BatchBilletType.Real,
            BilletNo = 1,
            SecCycle = 0,
        });
    }

    private static Batch Lotto(
        string batchId,
        string pressId,
        bool pressClosed = false,
        bool sawClosed = false,
        bool erpMarked = false,
        bool erpImported = false) =>
        new()
        {
            BatchId = batchId,
            PressId = pressId,
            StartTs = At(6, 0),
            StopTs = At(7, 0),
            IsPressClosed = pressClosed,
            IsSawClosed = sawClosed,
            IsErpMarked = erpMarked,
            IsErpImported = erpImported,
        };

    private static BatchDowntime Fermo(
        string pressId,
        DateTime start,
        DateTime stop,
        short typeId = MacrofermoId) =>
        new()
        {
            PressId = pressId,
            StartTs = start,
            StopTs = stop,
            DowntimeReasonId = 1,
            DowntimeType = (byte)typeId,
            DowntimeCode = $"{pressId}{stop:yyMMddHHmmss}",
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
