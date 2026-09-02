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
    }

    [Fact]
    public void Un_identita_non_autenticata_equivale_all_assenza_di_identita()
    {
        var permissions = UserPermissions.From(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Same(UserPermissions.Anonymous, permissions);
    }

    [Fact]
    public void Chi_e_autenticato_legge_anche_senza_ruoli()
    {
        // Scelta consapevole: CanRead coincide con l'essere autenticati e il ruolo
        // Archive.Reader non e' ancora richiesto. Vedi docs/decisioni-aperte.md, voce A2.
        var permissions = UserPermissions.From(Principal());

        Assert.True(permissions.CanRead);
        Assert.False(permissions.CanInsert);
        Assert.False(permissions.CanUpdate);
        Assert.False(permissions.CanDelete);
    }

    [Fact]
    public void Il_redattore_inserisce_e_modifica_ma_non_elimina()
    {
        var permissions = UserPermissions.From(Principal(AppRoles.ArchiveEditor));

        Assert.True(permissions.CanInsert);
        Assert.True(permissions.CanUpdate);
        Assert.False(permissions.CanDelete);
    }

    [Fact]
    public void L_amministratore_puo_tutto_senza_bisogno_degli_altri_ruoli()
    {
        var permissions = UserPermissions.From(Principal(AppRoles.ArchiveAdministrator));

        Assert.True(permissions.CanInsert);
        Assert.True(permissions.CanUpdate);
        Assert.True(permissions.CanDelete);
    }

    [Fact]
    public void Il_solo_ruolo_di_lettura_non_abilita_la_scrittura()
    {
        var permissions = UserPermissions.From(Principal(AppRoles.ArchiveReader));

        Assert.True(permissions.CanRead);
        Assert.False(permissions.CanInsert);
        Assert.False(permissions.CanUpdate);
        Assert.False(permissions.CanDelete);
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
