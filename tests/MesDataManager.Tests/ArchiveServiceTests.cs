using MesDataManager.Application.Archives;
using MesDataManager.Domain.Entities;
using MesDataManager.Tests.Support;

using Microsoft.EntityFrameworkCore;

namespace MesDataManager.Tests;

/// <summary>
/// Regole del servizio anagrafiche: permessi, validazione e i comportamenti recuperati
/// dall'applicazione WinForms.
/// </summary>
public sealed class ArchiveServiceTests : IDisposable
{
    private readonly ArchiveHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    // ------------------------------------------------------------------ persistenza

    [Fact]
    public async Task La_modifica_arriva_a_database()
    {
        // Regressione: con il tracking disattivato a livello di contesto, Find restituiva
        // un'entita' non tracciata e SaveChanges non scriveva nulla, senza segnalare errori.
        _harness.Seed(Causale(id: 1, position: 5, active: true, activeMaster: true));

        var row = await LoadRowAsync(id: 1);
        row[nameof(ModuleRepairReason.Position)] = 9;

        await _harness.ServiceFor(Users.Editor).UpdateAsync("ModuleRepairReason", row);

        using var context = _harness.CreateContext();
        var saved = await context.ModuleRepairReasons.SingleAsync();
        Assert.Equal((short)9, saved.Position);
    }

    [Fact]
    public async Task Sulle_tabelle_allineate_dall_ERP_la_descrizione_non_si_modifica()
    {
        _harness.Seed(Causale(id: 1, position: 1, active: true, activeMaster: true));

        var row = await LoadRowAsync(id: 1);
        row[nameof(ModuleRepairReason.Position)] = 3;
        row[nameof(ModuleRepairReason.Description)] = "tentativo di modifica";

        await _harness.ServiceFor(Users.Editor).UpdateAsync("ModuleRepairReason", row);

        using var context = _harness.CreateContext();
        var saved = await context.ModuleRepairReasons.SingleAsync();
        Assert.Equal((short)3, saved.Position);
        Assert.Equal("causale di prova", saved.Description);
    }

    [Fact]
    public async Task L_inserimento_scrive_la_riga()
    {
        var service = _harness.ServiceFor(Users.Editor);

        var row = await service.CreateTemplateAsync("DieCorrectionIssue");
        row[nameof(DieCorrectionIssue.Name)] = "riga nuova";

        await service.InsertAsync("DieCorrectionIssue", row);

        using var context = _harness.CreateContext();
        Assert.Equal("riga nuova", (await context.DieCorrectionIssues.SingleAsync()).Name);
    }

    [Fact]
    public async Task L_eliminazione_rimuove_la_riga()
    {
        _harness.Seed(new DieCorrectionIssue { Name = "da eliminare" });

        var service = _harness.ServiceFor(Users.Administrator);
        var rows = await service.GetRowsAsync("DieCorrectionIssue", new ArchiveQuery());

        await service.DeleteAsync("DieCorrectionIssue", rows[0]);

        using var context = _harness.CreateContext();
        Assert.Empty(context.DieCorrectionIssues);
    }

    // ------------------------------------------------------------------ regola sul master

    [Fact]
    public async Task Attivo_non_si_puo_alzare_se_il_master_e_disattivo()
    {
        _harness.Seed(Causale(id: 1, position: 1, active: false, activeMaster: false));

        var row = await LoadRowAsync(id: 1, includeInactive: true);
        row[nameof(ModuleRepairReason.IsActive)] = true;

        var error = await Assert.ThrowsAsync<ArchiveException>(
            () => _harness.ServiceFor(Users.Editor).UpdateAsync("ModuleRepairReason", row));

        Assert.Equal(ArchiveErrorKind.Validation, error.Kind);
        Assert.Equal("Msg.IsActiveLockedHint", error.MessageKey);

        using var context = _harness.CreateContext();
        Assert.False((await context.ModuleRepairReasons.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Attivo_si_puo_alzare_se_il_master_e_attivo()
    {
        _harness.Seed(Causale(id: 1, position: 1, active: false, activeMaster: true));

        var row = await LoadRowAsync(id: 1, includeInactive: true);
        row[nameof(ModuleRepairReason.IsActive)] = true;

        await _harness.ServiceFor(Users.Editor).UpdateAsync("ModuleRepairReason", row);

        using var context = _harness.CreateContext();
        Assert.True((await context.ModuleRepairReasons.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Attivo_si_puo_sempre_abbassare()
    {
        _harness.Seed(Causale(id: 1, position: 1, active: true, activeMaster: true));

        var row = await LoadRowAsync(id: 1);
        row[nameof(ModuleRepairReason.IsActive)] = false;

        await _harness.ServiceFor(Users.Editor).UpdateAsync("ModuleRepairReason", row);

        using var context = _harness.CreateContext();
        Assert.False((await context.ModuleRepairReasons.SingleAsync()).IsActive);
    }

    // ------------------------------------------------------------------ permessi

    [Fact]
    public async Task Senza_autenticazione_non_si_legge()
    {
        var error = await Assert.ThrowsAsync<ArchiveException>(
            () => _harness.ServiceFor(Users.Anonymous).GetRowsAsync("ModuleRepairReason", new ArchiveQuery()));

        Assert.Equal(ArchiveErrorKind.Forbidden, error.Kind);
    }

    [Fact]
    public async Task Il_lettore_non_modifica()
    {
        _harness.Seed(Causale(id: 1, position: 1, active: true, activeMaster: true));
        var row = await LoadRowAsync(id: 1);

        var error = await Assert.ThrowsAsync<ArchiveException>(
            () => _harness.ServiceFor(Users.Reader).UpdateAsync("ModuleRepairReason", row));

        Assert.Equal(ArchiveErrorKind.Forbidden, error.Kind);
    }

    [Fact]
    public async Task Il_redattore_non_elimina()
    {
        _harness.Seed(new DieCorrectionIssue { Name = "protetta" });

        var service = _harness.ServiceFor(Users.Editor);
        var rows = await service.GetRowsAsync("DieCorrectionIssue", new ArchiveQuery());

        var error = await Assert.ThrowsAsync<ArchiveException>(
            () => service.DeleteAsync("DieCorrectionIssue", rows[0]));

        Assert.Equal(ArchiveErrorKind.Forbidden, error.Kind);
    }

    [Theory]
    [InlineData("ModuleRepairReason")]  // allineata dall'ERP
    [InlineData("Press")]               // sola lettura
    public async Task Su_queste_anagrafiche_non_si_inserisce(string archiveKey)
    {
        var error = await Assert.ThrowsAsync<ArchiveException>(
            () => _harness.ServiceFor(Users.Administrator).CreateTemplateAsync(archiveKey));

        Assert.Equal(ArchiveErrorKind.Forbidden, error.Kind);
    }

    [Fact]
    public async Task Su_PressFailureType_l_eliminazione_e_vietata_a_chiunque()
    {
        // La tabella e' referenziata dallo storico dei fermi macchina senza vincolo di chiave
        // esterna: il database non respingerebbe il DELETE. Vedi decisioni-aperte, voce A5.
        _harness.Seed(new PressFailureType { PressFailureTypeId = 1, Description = "tipo di prova" });

        var service = _harness.ServiceFor(Users.Administrator);
        var rows = await service.GetRowsAsync("PressFailureType", new ArchiveQuery());

        var error = await Assert.ThrowsAsync<ArchiveException>(
            () => service.DeleteAsync("PressFailureType", rows[0]));

        Assert.Equal(ArchiveErrorKind.Forbidden, error.Kind);

        using var context = _harness.CreateContext();
        Assert.Equal(1, await context.PressFailureTypes.CountAsync());
    }

    [Fact]
    public async Task Su_PressFailureType_inserimento_e_modifica_restano_possibili()
    {
        // Il divieto riguarda solo l'eliminazione: la tabella resta a gestione piena.
        var service = _harness.ServiceFor(Users.Editor);

        var row = await service.CreateTemplateAsync("PressFailureType");
        row[nameof(PressFailureType.PressFailureTypeId)] = 7;
        row[nameof(PressFailureType.Description)] = "guasto idraulico";
        await service.InsertAsync("PressFailureType", row);

        var inserita = (await service.GetRowsAsync("PressFailureType", new ArchiveQuery()))[0];
        inserita[nameof(PressFailureType.Description)] = "guasto elettrico";
        await service.UpdateAsync("PressFailureType", inserita);

        using var context = _harness.CreateContext();
        Assert.Equal("guasto elettrico", (await context.PressFailureTypes.SingleAsync()).Description);
    }

    [Fact]
    public async Task Su_anagrafica_in_sola_lettura_non_si_modifica()
    {
        _harness.Seed(new HeatThreatment
        {
            HeatThreatmentId = "T1",
            Description = "trattamento",
            DurationMinutes = 30m,
        });

        var service = _harness.ServiceFor(Users.Administrator);
        var rows = await service.GetRowsAsync("HeatThreatment", new ArchiveQuery());

        var error = await Assert.ThrowsAsync<ArchiveException>(
            () => service.UpdateAsync("HeatThreatment", rows[0]));

        Assert.Equal(ArchiveErrorKind.Forbidden, error.Kind);
    }

    [Fact]
    public async Task Una_chiave_di_anagrafica_inesistente_e_un_errore_esplicito()
    {
        var error = await Assert.ThrowsAsync<ArchiveException>(
            () => _harness.ServiceFor(Users.Editor).GetRowsAsync("NonEsiste", new ArchiveQuery()));

        Assert.Equal(ArchiveErrorKind.NotFound, error.Kind);
    }

    // ------------------------------------------------------------------ validazione

    [Fact]
    public async Task Un_campo_obbligatorio_vuoto_viene_respinto()
    {
        var service = _harness.ServiceFor(Users.Editor);
        var row = await service.CreateTemplateAsync("DieCorrectionIssue");
        row[nameof(DieCorrectionIssue.Name)] = "   ";

        var error = await Assert.ThrowsAsync<ArchiveException>(
            () => service.InsertAsync("DieCorrectionIssue", row));

        Assert.Equal("Error.RequiredField", error.MessageKey);
        Assert.Equal(nameof(DieCorrectionIssue.Name), error.Field);
    }

    [Fact]
    public async Task Una_stringa_troppo_lunga_viene_respinta()
    {
        var service = _harness.ServiceFor(Users.Editor);
        var row = await service.CreateTemplateAsync("DieCorrectionIssue");
        row[nameof(DieCorrectionIssue.Name)] = new string('x', 256);

        var error = await Assert.ThrowsAsync<ArchiveException>(
            () => service.InsertAsync("DieCorrectionIssue", row));

        Assert.Equal("Error.MaxLengthExceeded", error.MessageKey);
        Assert.Equal(255, Assert.Single(error.MessageArguments));
    }

    [Fact]
    public async Task Un_numero_fuori_dall_intervallo_della_colonna_viene_respinto()
    {
        // Position e' smallint: senza questo controllo la conversione lanciava
        // OverflowException, che la UI non intercetta e che fa cadere il circuito.
        _harness.Seed(Causale(id: 1, position: 1, active: true, activeMaster: true));

        var row = await LoadRowAsync(id: 1);
        row[nameof(ModuleRepairReason.Position)] = 40_000L;

        var error = await Assert.ThrowsAsync<ArchiveException>(
            () => _harness.ServiceFor(Users.Editor).UpdateAsync("ModuleRepairReason", row));

        Assert.Equal(ArchiveErrorKind.Validation, error.Kind);
        Assert.Equal("Error.ValueOutOfRange", error.MessageKey);
        Assert.Equal(nameof(ModuleRepairReason.Position), error.Field);
    }

    [Fact]
    public async Task Modificare_una_riga_inesistente_non_ne_crea_una_nuova()
    {
        var row = await _harness.ServiceFor(Users.Editor).CreateTemplateAsync("DieCorrectionIssue");
        row[nameof(DieCorrectionIssue.DieCorrectionIssueId)] = 999;
        row[nameof(DieCorrectionIssue.Name)] = "fantasma";

        var error = await Assert.ThrowsAsync<ArchiveException>(
            () => _harness.ServiceFor(Users.Editor).UpdateAsync("DieCorrectionIssue", row));

        Assert.Equal(ArchiveErrorKind.NotFound, error.Kind);
    }

    // ------------------------------------------------------------------ lettura

    [Fact]
    public async Task Le_voci_disattivate_si_vedono_solo_su_richiesta()
    {
        _harness.Seed(
            Causale(id: 1, position: 1, active: true, activeMaster: true),
            Causale(id: 2, position: 2, active: false, activeMaster: true));

        var service = _harness.ServiceFor(Users.Reader);

        Assert.Single(await service.GetRowsAsync("ModuleRepairReason", new ArchiveQuery()));
        Assert.Equal(2, (await service.GetRowsAsync(
            "ModuleRepairReason", new ArchiveQuery(IncludeInactive: true))).Count);
    }

    [Fact]
    public async Task La_ricerca_filtra_sui_campi_dichiarati()
    {
        _harness.Seed(
            new DieCorrectionIssue { Name = "riga di sinistra" },
            new DieCorrectionIssue { Name = "riga di destra" });

        var rows = await _harness.ServiceFor(Users.Reader)
            .GetRowsAsync("DieCorrectionIssue", new ArchiveQuery(Search: "sinistra"));

        Assert.Equal("riga di sinistra", Assert.Single(rows)[nameof(DieCorrectionIssue.Name)]);
    }

    [Fact]
    public async Task L_ordinamento_predefinito_del_descrittore_e_applicato()
    {
        _harness.Seed(
            Causale(id: 1, position: 20, active: true, activeMaster: true, description: "seconda"),
            Causale(id: 2, position: 10, active: true, activeMaster: true, description: "prima"));

        var rows = await _harness.ServiceFor(Users.Reader)
            .GetRowsAsync("ModuleRepairReason", new ArchiveQuery());

        Assert.Collection(
            rows,
            first => Assert.Equal("prima", first[nameof(ModuleRepairReason.Description)]),
            second => Assert.Equal("seconda", second[nameof(ModuleRepairReason.Description)]));
    }

    [Fact]
    public async Task La_nuova_riga_nasce_attiva_dove_l_anagrafica_lo_prevede()
    {
        var row = await _harness.ServiceFor(Users.Editor).CreateTemplateAsync("EmailRecipient");

        Assert.True(row.IsActive);
    }

    // ------------------------------------------------------------------ supporto

    private static ModuleRepairReason Causale(
        short id,
        short position,
        bool active,
        bool activeMaster,
        string description = "causale di prova") =>
        new()
        {
            ModuleRepairReasonId = id,
            Position = position,
            Description = description,
            IsActive = active,
            IsActiveMaster = activeMaster,
        };

    private async Task<ArchiveRow> LoadRowAsync(short id, bool includeInactive = false)
    {
        var rows = await _harness.ServiceFor(Users.Reader)
            .GetRowsAsync("ModuleRepairReason", new ArchiveQuery(includeInactive));

        return rows.Single(r => Equals(r[nameof(ModuleRepairReason.ModuleRepairReasonId)], id));
    }
}
