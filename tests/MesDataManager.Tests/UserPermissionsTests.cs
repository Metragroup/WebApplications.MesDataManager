using System.Security.Claims;

using MesDataManager.Application.Security;

namespace MesDataManager.Tests;

/// <summary>
/// Mappatura dei ruoli Entra ID sui permessi. Nel vecchio progetto l'<c>AuthService</c>
/// restituiva sempre tutti i permessi: questo codice non ha un precedente da confrontare,
/// quindi va verificato qui.
/// </summary>
public sealed class UserPermissionsTests
{
    [Fact]
    public void Senza_principal_non_si_puo_fare_nulla()
    {
        var permissions = UserPermissions.From(null);

        Assert.False(permissions.IsAuthenticated);
        Assert.False(permissions.CanRead);
        Assert.False(permissions.CanInsert);
        Assert.False(permissions.CanUpdate);
        Assert.False(permissions.CanDelete);
        Assert.False(permissions.CanEditProduction);
    }

    [Fact]
    public void Un_identita_non_autenticata_equivale_all_assenza_di_identita()
    {
        var permissions = UserPermissions.From(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Same(UserPermissions.Anonymous, permissions);
    }

    [Fact]
    public void Chi_e_autenticato_ma_senza_ruoli_non_vede_i_dati()
    {
        // Il caso non dovrebbe presentarsi — Entra ID pretende l'assegnazione e assegnare
        // significa scegliere un ruolo — ma il controllo e' ripetuto qui: se quell'impostazione
        // venisse spostata, la lettura non deve aprirsi a tutto il tenant in silenzio.
        var permissions = UserPermissions.From(Principal());

        Assert.True(permissions.IsAuthenticated);
        Assert.False(permissions.CanRead);
        Assert.False(permissions.CanInsert);
        Assert.False(permissions.CanUpdate);
        Assert.False(permissions.CanDelete);
    }

    [Fact]
    public void Un_ruolo_non_riconosciuto_non_apre_la_lettura()
    {
        // Un ruolo di un'altra applicazione, o rimasto in un token dopo una rinomina, non deve
        // valere come consultazione.
        var permissions = UserPermissions.From(Principal("Qualcosa.Altro"));

        Assert.False(permissions.CanRead);
    }

    [Fact]
    public void Il_lettore_consulta_e_non_scrive()
    {
        var permissions = UserPermissions.From(Principal(AppRoles.Reader));

        Assert.True(permissions.CanRead);
        Assert.False(permissions.CanInsert);
        Assert.False(permissions.CanUpdate);
        Assert.False(permissions.CanDelete);
    }

    [Fact]
    public void Il_redattore_delle_anagrafiche_scrive_ed_elimina()
    {
        // Chi modifica puo' anche eliminare: e' una conseguenza della voce A2, chiusa il
        // 3 settembre 2026, non una dimenticanza. Il freno sull'eliminazione e' per anagrafica
        // (ArchiveDescriptor.PreventDelete), non per ruolo.
        var permissions = UserPermissions.From(Principal(AppRoles.ArchiveEditor));

        Assert.True(permissions.CanInsert);
        Assert.True(permissions.CanUpdate);
        Assert.True(permissions.CanDelete);
    }

    [Fact]
    public void L_amministratore_scrive_le_anagrafiche_senza_il_ruolo_di_ambito()
    {
        // Administrator vale su tutti gli ambiti: non deve servirgli anche Archive.Editor.
        var permissions = UserPermissions.From(Principal(AppRoles.Administrator));

        Assert.True(permissions.CanInsert);
        Assert.True(permissions.CanUpdate);
        Assert.True(permissions.CanDelete);
        Assert.True(permissions.IsAdministrator);
    }

    [Fact]
    public void Avere_entrambi_i_ruoli_di_scrittura_non_e_essere_amministratore()
    {
        // La somma di Archive.Editor e Production.Editor produce la stessa combinazione di
        // permessi dell'amministratore. Solo il ruolo distingue i due casi, ed e' quello che
        // decide chi puo' forzare lo sblocco di un lotto in modifica altrui.
        var permissions = UserPermissions.From(
            Principal(AppRoles.ArchiveEditor, AppRoles.ProductionEditor));

        Assert.True(permissions.CanDelete);
        Assert.True(permissions.CanEditProduction);
        Assert.False(permissions.IsAdministrator);
    }

    [Fact]
    public void Il_ruolo_della_produzione_non_scrive_le_anagrafiche()
    {
        // Production.Editor riguarda lotti, billette e fermate: sulle anagrafiche vale come
        // Reader. E' l'errore di assegnazione piu' probabile, quindi va verificato.
        var permissions = UserPermissions.From(Principal(AppRoles.ProductionEditor));

        Assert.True(permissions.CanRead);
        Assert.False(permissions.CanInsert);
        Assert.False(permissions.CanUpdate);
        Assert.False(permissions.CanDelete);
    }

    [Fact]
    public void Il_ruolo_della_produzione_scrive_i_dati_di_produzione()
    {
        var permissions = UserPermissions.From(Principal(AppRoles.ProductionEditor));

        Assert.True(permissions.CanEditProduction);
    }

    [Fact]
    public void Il_redattore_delle_anagrafiche_non_scrive_la_produzione()
    {
        // L'altra meta' della separazione degli ambiti: senza questo controllo i tre flag
        // generici avrebbero aperto anche i fermi macchina a chi gestisce le anagrafiche.
        var permissions = UserPermissions.From(Principal(AppRoles.ArchiveEditor));

        Assert.False(permissions.CanEditProduction);
    }

    [Fact]
    public void Il_lettore_non_scrive_la_produzione()
    {
        var permissions = UserPermissions.From(Principal(AppRoles.Reader));

        Assert.False(permissions.CanEditProduction);
    }

    [Fact]
    public void L_amministratore_scrive_la_produzione_senza_il_ruolo_di_ambito()
    {
        var permissions = UserPermissions.From(Principal(AppRoles.Administrator));

        Assert.True(permissions.CanEditProduction);
    }

    [Fact]
    public void I_ruoli_dei_due_ambiti_convivono()
    {
        // Chi lavora sia sulle anagrafiche sia sulla produzione ha entrambi i ruoli: quello
        // della produzione non deve togliere niente a quello delle anagrafiche.
        var permissions = UserPermissions.From(
            Principal(AppRoles.ArchiveEditor, AppRoles.ProductionEditor));

        Assert.True(permissions.CanInsert);
        Assert.True(permissions.CanUpdate);
        Assert.True(permissions.CanDelete);
    }

    [Fact]
    public void Il_nome_utente_viene_dal_claim_di_Entra_ID()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("preferred_username", "mario.rossi@metra.it"),
                new Claim(ClaimTypes.Name, "MARIO"),
                new Claim("name", "Mario Rossi"),
            ],
            authenticationType: "prova"));

        var permissions = UserPermissions.From(principal);

        Assert.Equal("mario.rossi@metra.it", permissions.UserName);
        Assert.Equal("Mario Rossi", permissions.DisplayName);
    }

    [Fact]
    public void In_mancanza_del_claim_preferito_si_ripiega_su_quelli_successivi()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Upn, "mario.rossi@metra.it")],
            authenticationType: "prova"));

        var permissions = UserPermissions.From(principal);

        Assert.Equal("mario.rossi@metra.it", permissions.UserName);
        Assert.Equal("mario.rossi@metra.it", permissions.DisplayName);
    }

    private static ClaimsPrincipal Principal(params string[] roles)
    {
        var claims = new List<Claim> { new("preferred_username", "utente@metra.it") };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims, authenticationType: "prova", nameType: ClaimTypes.Name, roleType: ClaimTypes.Role));
    }
}
