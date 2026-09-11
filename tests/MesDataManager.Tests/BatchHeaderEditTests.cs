using MesDataManager.Application.Production;
using MesDataManager.Application.Security;
using MesDataManager.Domain.Entities;
using MesDataManager.Tests.Support;

namespace MesDataManager.Tests;

/// <summary>
/// Modifica della testata: matrice e causale di chiusura.
/// <para>
/// Il controllo dello stato d'uso della matrice e' la correzione piu' rilevante rispetto al
/// vecchio applicativo, che verificava la sola esistenza e lasciava allo stato il compito di
/// emergere piu' tardi, dalla diagnostica.
/// </para>
/// </summary>
public sealed class BatchHeaderEditTests : IDisposable
{
    private const string Lotto = "MP1260901080000";
    private static readonly DateTime Giorno = new(2026, 9, 1);

    private static readonly UserPermissions Anna = new()
    {
        UserName = "anna.rossi@metra.it",
        IsAuthenticated = true,
        CanRead = true,
        CanEditProduction = true,
    };

    private readonly ProductionHarness _harness = new();

    public BatchHeaderEditTests()
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

    // ------------------------------------------------------------------ composizione del codice

    [Theory]
    [InlineData("R22225", (short)1, "R22225/1")]
    [InlineData("R22225", null, "R22225")]
    [InlineData(" r22225 ", (short)3, "r22225/3")]
    public void Il_codice_matrice_si_compone_da_codice_e_numero(string code, short? number, string expected)
    {
        // Senza numero resta il solo codice, e non "codice/": e' il formato che il vecchio
        // applicativo scriveva in Batch.DieID e su tutte le billette.
        Assert.Equal(expected, DieValidation.ComposeDieId(code, number));
    }

    // ------------------------------------------------------------------ controllo della matrice

    [Fact]
    public async Task Una_matrice_inesistente_non_e_assegnabile()
    {
        var esito = await _harness.BatchServiceFor(Anna).ValidateDieAsync("R99999", 1);

        Assert.Equal(DieCheck.NotFound, esito.Outcome);
        Assert.False(esito.IsUsable);
    }

    [Theory]
    [InlineData(DieUseStatus.Available, DieCheck.Available, true, false)]
    [InlineData(DieUseStatus.Test, DieCheck.Test, true, true)]
    [InlineData(DieUseStatus.Stored, DieCheck.Stored, false, false)]
    [InlineData(DieUseStatus.Deleted, DieCheck.Deleted, false, false)]
    [InlineData(DieUseStatus.Transferred, DieCheck.Transferred, false, false)]
    public async Task Lo_stato_d_uso_decide_se_la_matrice_si_assegna(
        int statusUse,
        DieCheck atteso,
        bool assegnabile,
        bool conAvviso)
    {
        SeedMatrice("R22225/1", statusUse);

        var esito = await _harness.BatchServiceFor(Anna).ValidateDieAsync("R22225", 1);

        Assert.Equal(atteso, esito.Outcome);
        Assert.Equal(assegnabile, esito.IsUsable);
        Assert.Equal(conAvviso, esito.IsWarning);
    }

    [Fact]
    public async Task Una_matrice_senza_stato_dichiarato_si_assegna_senza_avviso()
    {
        // Esiste come centro di lavoro ma non ha la riga di stato d.uso: 693 matrici sul database
        // di test. Vietarla direbbe una cosa che non si sa.
        _harness.Seed(new Die { DieId = "R22225/1" });

        var esito = await _harness.BatchServiceFor(Anna).ValidateDieAsync("R22225", 1);

        Assert.Equal(DieCheck.Unknown, esito.Outcome);
        Assert.True(esito.IsUsable);

        // Nessun avviso: sul database di test 94 lotti veri su 347 girano su matrici senza riga
        // di stato, e avvisare su un quarto delle assegnazioni sarebbe rumore.
        Assert.False(esito.IsWarning);
    }

    [Fact]
    public async Task Uno_stato_d_uso_non_previsto_non_blocca()
    {
        SeedMatrice("R22225/1", statusUse: 42);

        var esito = await _harness.BatchServiceFor(Anna).ValidateDieAsync("R22225", 1);

        Assert.Equal(DieCheck.Unknown, esito.Outcome);
        Assert.True(esito.IsUsable);
    }

    [Fact]
    public async Task Il_codice_matrice_si_confronta_in_maiuscolo()
    {
        SeedMatrice("R22225/1", DieUseStatus.Available);

        var esito = await _harness.BatchServiceFor(Anna).ValidateDieAsync(" r22225 ", 1);

        Assert.Equal(DieCheck.Available, esito.Outcome);
        Assert.Equal("R22225/1", esito.DieId);
    }

    [Fact]
    public async Task Una_matrice_senza_numero_si_controlla_col_solo_codice()
    {
        SeedMatrice("R22225", DieUseStatus.Available);

        var esito = await _harness.BatchServiceFor(Anna).ValidateDieAsync("R22225", null);

        Assert.Equal(DieCheck.Available, esito.Outcome);
        Assert.Equal("R22225", esito.DieId);
    }

    [Fact]
    public async Task Il_codice_matrice_e_obbligatorio()
    {
        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Anna).ValidateDieAsync("   ", null));

        Assert.Equal(ProductionErrorKind.Validation, errore.Kind);
    }

    [Fact]
    public async Task Chi_consulta_soltanto_non_controlla_le_matrici()
    {
        // Il controllo non scrive niente, ma appartiene alla modifica: aprirlo a chi consulta
        // darebbe accesso all'anagrafica dell'ERP dall'area produzione.
        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Users.Reader).ValidateDieAsync("R22225", 1));

        Assert.Equal(ProductionErrorKind.Forbidden, errore.Kind);
    }

    // ------------------------------------------------------------------ modifiche in sospeso

    [Fact]
    public async Task Il_modello_di_modifica_parte_dai_valori_del_lotto()
    {
        var edit = new BatchEditModel(await Detail());

        Assert.Equal("R22225/1", edit.DieId);
        Assert.Equal("R22225", edit.DieCode);
        Assert.Equal((short)1, edit.DieNumber);
        Assert.Equal((short)7, edit.ClosingReasonId);
        Assert.False(edit.HasChanges);
    }

    [Fact]
    public async Task Assegnare_una_matrice_diversa_e_una_modifica_in_sospeso()
    {
        var edit = new BatchEditModel(await Detail());

        edit.ChangeDie(new DieValidation("R30000/2", "R30000", 2, DieCheck.Available));

        Assert.Equal("R30000/2", edit.DieId);
        Assert.Equal("R30000", edit.DieCode);
        Assert.Equal((short)2, edit.DieNumber);
        Assert.True(edit.DieChanged);
        Assert.True(edit.HasChanges);
    }

    [Fact]
    public async Task Riassegnare_la_stessa_matrice_non_e_una_modifica()
    {
        // Aprire il controllo e confermare la matrice che c'era gia' non deve abilitare il
        // salvataggio: HasChanges confronta i valori, non conta i form aperti.
        var edit = new BatchEditModel(await Detail());

        edit.ChangeDie(new DieValidation("R22225/1", "R22225", 1, DieCheck.Available));

        Assert.False(edit.DieChanged);
        Assert.False(edit.HasChanges);
    }

    [Fact]
    public async Task Una_matrice_non_assegnabile_non_entra_nel_modello()
    {
        // Doppio presidio: l'interfaccia non abilita il pulsante, e il modello rifiuta comunque.
        var edit = new BatchEditModel(await Detail());

        var errore = Assert.Throws<ProductionException>(
            () => edit.ChangeDie(new DieValidation("R30000/2", "R30000", 2, DieCheck.Stored)));

        Assert.Equal(ProductionErrorKind.Validation, errore.Kind);
        Assert.Equal("R22225/1", edit.DieId);
    }

    [Fact]
    public async Task Cambiare_causale_di_chiusura_e_una_modifica_in_sospeso()
    {
        var edit = new BatchEditModel(await Detail());

        edit.ChangeClosingReason(9);

        Assert.True(edit.ClosingReasonChanged);
        Assert.True(edit.HasChanges);
    }

    [Fact]
    public async Task Togliere_la_causale_di_chiusura_e_una_modifica_in_sospeso()
    {
        var edit = new BatchEditModel(await Detail());

        edit.ChangeClosingReason(null);

        Assert.True(edit.ClosingReasonChanged);
    }

    [Fact]
    public async Task Rimettere_la_causale_che_c_era_non_e_una_modifica()
    {
        var edit = new BatchEditModel(await Detail());

        edit.ChangeClosingReason(9);
        edit.ChangeClosingReason(7);

        Assert.False(edit.HasChanges);
    }

    // ------------------------------------------------------------------ appoggio

    private async Task<BatchDetail> Detail()
    {
        _harness.Seed(new PressBatchClosingReason
        {
            PressBatchClosingReasonId = 7,
            Description = "Fine ordine",
            Result = "OK",
            IsActive = true,
            IsActiveMaster = true,
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
            IsBatchProcessed = true,
            PressBatchClosingReasonId = 7,
        });

        return await _harness.BatchServiceFor(Anna).GetDetailAsync(Lotto);
    }

    private void SeedMatrice(string dieId, int statusUse)
    {
        _harness.Seed(new Die { DieId = dieId });
        _harness.Seed(new DieSetup { DieId = dieId, StatusUse = statusUse });
    }
}
