using MesDataManager.Application.Production;
using MesDataManager.Application.Security;
using MesDataManager.Domain.Entities;
using MesDataManager.Tests.Support;

namespace MesDataManager.Tests;

/// <summary>
/// Ciclo di vita del blocco di modifica del lotto.
/// <para>
/// E' la parte in cui questa applicazione si discosta di piu' dal WinForms, e di proposito: la
/// scadenza del blocco non esisteva, il rientro nel proprio blocco non era previsto e la presa
/// era una lettura seguita da una scrittura. Nel web ognuna delle tre e' un problema, quindi
/// ognuna ha il suo test.
/// </para>
/// </summary>
public sealed class BatchLockTests : IDisposable
{
    private const string Lotto = "MP1260901080000";
    private static readonly DateTime Giorno = new(2026, 9, 1);

    private static readonly UserPermissions Anna = Utente("anna.rossi@metra.it");
    private static readonly UserPermissions Bruno = Utente("bruno.verdi@metra.it");

    private readonly ProductionHarness _harness = new();

    public BatchLockTests()
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

    // ------------------------------------------------------------------ permessi

    [Fact]
    public async Task Chi_consulta_soltanto_non_prende_il_lotto()
    {
        SeedLotto();

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Users.Reader).BeginEditAsync(Lotto));

        Assert.Equal(ProductionErrorKind.Forbidden, errore.Kind);
        Assert.False(await IsLocked());
    }

    [Fact]
    public async Task Il_redattore_delle_anagrafiche_non_prende_il_lotto()
    {
        // Archive.Editor scrive le anagrafiche e non la produzione: e' la separazione degli
        // ambiti decisa il 3 settembre 2026, e qui si vede all'opera.
        SeedLotto();

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Users.Writer).BeginEditAsync(Lotto));

        Assert.Equal(ProductionErrorKind.Forbidden, errore.Kind);
    }

    // ------------------------------------------------------------------ presa del blocco

    [Fact]
    public async Task Prendere_il_lotto_scrive_utente_e_istante()
    {
        SeedLotto();

        var prima = DateTime.Now.AddSeconds(-1);
        var detail = await _harness.BatchServiceFor(Anna).BeginEditAsync(Lotto);

        Assert.True(detail.IsLock);
        Assert.Equal("anna.rossi@metra.it", detail.LockUsr);
        Assert.NotNull(detail.LockTs);
        Assert.True(detail.LockTs >= prima);
    }

    [Fact]
    public async Task Un_upn_piu_lungo_della_colonna_viene_troncato()
    {
        // Lock_Usr e' nvarchar(50): un indirizzo piu' lungo farebbe fallire il salvataggio, e il
        // blocco non deve dipendere dalla lunghezza del nome di chi lo prende.
        SeedLotto();
        var lungo = Utente(new string('a', 45) + "@metra.it");

        var detail = await _harness.BatchServiceFor(lungo).BeginEditAsync(Lotto);

        Assert.Equal(50, detail.LockUsr!.Length);
    }

    [Fact]
    public async Task Un_lotto_inesistente_non_si_prende()
    {
        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Anna).BeginEditAsync("MP1999999999999"));

        Assert.Equal(ProductionErrorKind.NotFound, errore.Kind);
    }

    // ------------------------------------------------------------------ precondizioni

    [Fact]
    public async Task Un_lotto_in_elaborazione_non_si_prende()
    {
        SeedLotto(batchProcessed: false);

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Anna).BeginEditAsync(Lotto));

        Assert.Equal(ProductionErrorKind.Conflict, errore.Kind);

        // Il lotto non deve restare bloccato da un tentativo respinto: nel vecchio applicativo
        // bastava premere "Modifica" per bloccare, e il controllo veniva dopo.
        Assert.False(await IsLocked());
    }

    [Fact]
    public async Task Un_lotto_gia_riconciliato_non_si_prende()
    {
        SeedLotto(erpImported: true);

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Anna).BeginEditAsync(Lotto));

        Assert.Equal(ProductionErrorKind.Conflict, errore.Kind);
        Assert.False(await IsLocked());
    }

    // ------------------------------------------------------------------ contesa

    [Fact]
    public async Task Un_lotto_tenuto_da_un_altro_non_si_prende_e_il_messaggio_dice_chi()
    {
        SeedLotto();
        await _harness.BatchServiceFor(Anna).BeginEditAsync(Lotto);

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Bruno).BeginEditAsync(Lotto));

        Assert.Equal(ProductionErrorKind.Conflict, errore.Kind);
        Assert.Contains("anna.rossi@metra.it", errore.MessageArguments[0]?.ToString());

        // E il blocco resta di chi ce l'aveva.
        Assert.Equal("anna.rossi@metra.it", await LockUser());
    }

    [Fact]
    public async Task Rientrare_nel_proprio_blocco_e_consentito()
    {
        // Nel web ricaricare la pagina e' normale: "bloccato da te stesso" sarebbe un vicolo
        // cieco, e il vecchio applicativo ci finiva dentro (LockBatch tornava falso).
        SeedLotto();
        var servizio = _harness.BatchServiceFor(Anna);

        await servizio.BeginEditAsync(Lotto);
        var detail = await servizio.BeginEditAsync(Lotto);

        Assert.True(detail.IsLock);
        Assert.Equal("anna.rossi@metra.it", detail.LockUsr);
    }

    [Fact]
    public async Task Un_blocco_scaduto_passa_a_chi_lo_chiede()
    {
        SeedLotto();
        await _harness.BatchServiceFor(Anna).BeginEditAsync(Lotto);
        await InvecchiaBlocco(BatchLockPolicy.Expiry + TimeSpan.FromMinutes(1));

        var detail = await _harness.BatchServiceFor(Bruno).BeginEditAsync(Lotto);

        Assert.Equal("bruno.verdi@metra.it", detail.LockUsr);
    }

    [Fact]
    public async Task Un_blocco_ancora_valido_non_passa_a_nessuno()
    {
        SeedLotto();
        await _harness.BatchServiceFor(Anna).BeginEditAsync(Lotto);
        await InvecchiaBlocco(BatchLockPolicy.Expiry - TimeSpan.FromMinutes(1));

        await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Bruno).BeginEditAsync(Lotto));
    }

    [Fact]
    public async Task Un_blocco_senza_istante_e_abbandonato()
    {
        // Sono i blocchi lasciati dal vecchio applicativo, che nessuno rilascerebbe mai: sul
        // database di test ce ne sono ancora.
        SeedLotto();
        await using (var context = _harness.CreateContext())
        {
            var batch = context.Batches.Single();
            batch.IsLock = true;
            batch.LockUsr = "vecchio\\PC01";
            batch.LockTs = null;
            await context.SaveChangesAsync();
        }

        var detail = await _harness.BatchServiceFor(Anna).BeginEditAsync(Lotto);

        Assert.Equal("anna.rossi@metra.it", detail.LockUsr);
    }

    // ------------------------------------------------------------------ rilascio

    [Fact]
    public async Task Chi_ha_il_blocco_lo_rilascia()
    {
        SeedLotto();
        var servizio = _harness.BatchServiceFor(Anna);
        await servizio.BeginEditAsync(Lotto);

        await servizio.CancelEditAsync(Lotto);

        Assert.False(await IsLocked());
        Assert.Null(await LockUser());
    }

    [Fact]
    public async Task Il_rilascio_non_tocca_il_blocco_di_un_altro_e_non_solleva()
    {
        SeedLotto();
        await _harness.BatchServiceFor(Anna).BeginEditAsync(Lotto);

        await _harness.BatchServiceFor(Bruno).CancelEditAsync(Lotto);

        Assert.Equal("anna.rossi@metra.it", await LockUser());
    }

    [Fact]
    public async Task Il_rilascio_di_un_lotto_non_bloccato_non_solleva()
    {
        SeedLotto();

        await _harness.BatchServiceFor(Anna).CancelEditAsync(Lotto);

        Assert.False(await IsLocked());
    }

    // ------------------------------------------------------------------ sblocco forzato

    [Fact]
    public async Task Solo_l_amministratore_forza_lo_sblocco()
    {
        SeedLotto();
        await _harness.BatchServiceFor(Anna).BeginEditAsync(Lotto);

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Bruno).ForceUnlockAsync(Lotto));

        Assert.Equal(ProductionErrorKind.Forbidden, errore.Kind);
        Assert.Equal("anna.rossi@metra.it", await LockUser());
    }

    [Fact]
    public async Task L_amministratore_libera_il_lotto_di_un_altro()
    {
        SeedLotto();
        await _harness.BatchServiceFor(Anna).BeginEditAsync(Lotto);

        await _harness.BatchServiceFor(Users.Administrator).ForceUnlockAsync(Lotto);

        Assert.False(await IsLocked());
        Assert.Null(await LockUser());
    }

    [Fact]
    public async Task Avere_i_due_ruoli_di_scrittura_non_fa_un_amministratore()
    {
        // Archive.Editor piu' Production.Editor danno la stessa combinazione di permessi
        // dell'amministratore: se lo sblocco forzato la deducesse, glielo concederebbe per
        // errore. Guarda invece il ruolo, ed e' il motivo per cui IsAdministrator esiste.
        SeedLotto();
        await _harness.BatchServiceFor(Anna).BeginEditAsync(Lotto);

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Users.BothEditors).ForceUnlockAsync(Lotto));

        Assert.Equal(ProductionErrorKind.Forbidden, errore.Kind);
        Assert.Equal("anna.rossi@metra.it", await LockUser());
    }

    [Fact]
    public async Task Chi_scrive_la_produzione_senza_essere_amministratore_non_forza()
    {
        // Production.Editor scrive i lotti ma non porta via il lavoro di un collega: lo sblocco
        // forzato non si deduce dai permessi di scrittura.
        SeedLotto();
        await _harness.BatchServiceFor(Anna).BeginEditAsync(Lotto);

        var errore = await Assert.ThrowsAsync<ProductionException>(
            () => _harness.BatchServiceFor(Users.ProductionWriter).ForceUnlockAsync(Lotto));

        Assert.Equal(ProductionErrorKind.Forbidden, errore.Kind);
    }

    // ------------------------------------------------------------------ la regola, da sola

    [Theory]
    [InlineData(0, false)]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(120, true)]
    public void La_scadenza_del_blocco_si_misura_sull_istante_di_presa(int minuti, bool scaduto)
    {
        var adesso = new DateTime(2026, 9, 9, 12, 0, 0);

        Assert.Equal(scaduto, BatchLockPolicy.IsExpired(adesso.AddMinutes(-minuti), adesso));
    }

    [Fact]
    public void Un_blocco_senza_istante_risulta_scaduto()
    {
        Assert.True(BatchLockPolicy.IsExpired(null, DateTime.Now));
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

    private void SeedLotto(bool batchProcessed = true, bool erpImported = false)
    {
        _harness.Seed(new Batch
        {
            BatchId = Lotto,
            PressId = "MP1",
            DieId = "R22225/1",
            StartTs = Giorno.AddHours(8),
            StopTs = Giorno.AddHours(9),
            BilletCount = 1,
            IsBatchProcessed = batchProcessed,
            IsErpImported = erpImported,
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

    /// <summary>Sposta indietro l'istante del blocco, per non dover attendere l'ora vera.</summary>
    private async Task InvecchiaBlocco(TimeSpan eta)
    {
        await using var context = _harness.CreateContext();
        var batch = context.Batches.Single();
        batch.LockTs = DateTime.Now - eta;
        await context.SaveChangesAsync();
    }

    private async Task<bool> IsLocked()
    {
        await using var context = _harness.CreateContext();
        return context.Batches.Single().IsLock;
    }

    private async Task<string?> LockUser()
    {
        await using var context = _harness.CreateContext();
        return context.Batches.Single().LockUsr;
    }
}
