using System.Security.Claims;

using MesDataManager.Application.Security;
using MesDataManager.Web.Security;

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace MesDataManager.Tests;

/// <summary>
/// Come i ruoli di Entra ID arrivano fino ai permessi. E' il punto in cui un'assegnazione
/// corretta nel portale puo' comunque risultare assente nell'applicazione: la claim che manda
/// Entra ID (<c>roles</c>) non e' quella su cui <c>IsInRole</c> cerca, e la traduzione fra le
/// due dipende da un valore predefinito della libreria.
/// </summary>
public sealed class EntraRoleClaimTests
{
    /// <summary>Ambiente diverso da Development: e' la modalita' Entra ID a dover essere provata.</summary>
    private sealed class ProductionEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = ".";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "MesDataManager";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = ".";
        public string EnvironmentName { get; set; } = "Production";
    }

    [Fact]
    public void La_traduzione_della_claim_roles_di_Entra_ID_resta_attiva()
    {
        // Le due impostazioni vanno lette insieme: IsInRole cerca nel tipo di claim indicato da
        // RoleClaimType, e la claim "roles" del token finisce in quel tipo solo se
        // MapInboundClaims e' acceso. Se un aggiornamento della libreria spegnesse la
        // mappatura, o cambiasse RoleClaimType, nessun ruolo risulterebbe assegnato: non un
        // errore, ma un'applicazione che nega tutto. Il permesso non dipende piu' solo da
        // questo — vedi Il_ruolo_vale_anche_quando_la_claim_non_e_tradotta — ma se la coppia
        // cambia e' bene saperlo qui e non dagli utenti.
        var options = OpenIdConnectOptionsFromSetup();

        Assert.True(options.MapInboundClaims);
        Assert.Equal(ClaimsIdentity.DefaultRoleClaimType, options.TokenValidationParameters.RoleClaimType);
    }

    [Fact]
    public void Il_nome_utente_viene_letto_dalla_claim_di_Entra_ID()
    {
        // Microsoft.Identity.Web imposta preferred_username come claim del nome: e' la stessa
        // che UserPermissions.From cerca per prima.
        var options = OpenIdConnectOptionsFromSetup();

        Assert.Equal("preferred_username", options.TokenValidationParameters.NameClaimType);
    }

    [Fact]
    public void Il_ruolo_vale_anche_quando_la_claim_non_e_tradotta()
    {
        // Il token cosi' come lo manda Entra ID, senza la traduzione in ClaimTypes.Role:
        // l'identita' non dichiara nessun tipo di claim per i ruoli, quindi IsInRole non trova
        // nulla. I permessi devono comunque risultare, altrimenti un cambio di valore
        // predefinito nella libreria si presenterebbe come un'applicazione in sola lettura.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("preferred_username", "utente@metra.it"),
                new Claim(AppRoles.RolesClaimType, AppRoles.Administrator),
            ],
            authenticationType: "prova"));

        Assert.False(principal.IsInRole(AppRoles.Administrator));

        var permissions = UserPermissions.From(principal);

        Assert.True(permissions.CanRead);
        Assert.True(permissions.CanInsert);
        Assert.True(permissions.CanUpdate);
        Assert.True(permissions.CanDelete);
        Assert.True(permissions.CanEditProduction);
    }

    /// <summary>
    /// Le opzioni di OpenID Connect come le costruisce l'applicazione: la configurazione e'
    /// quella di esercizio a meno dei valori, che qui non vengono usati per collegarsi.
    /// </summary>
    private static OpenIdConnectOptions OpenIdConnectOptionsFromSetup()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000000",
                ["AzureAd:ClientId"] = "00000000-0000-0000-0000-000000000001",
                ["AzureAd:CallbackPath"] = "/signin-oidc",
            })
            .Build();

        var services = new ServiceCollection();

        // Microsoft.Identity.Web li pretende entrambi mentre configura le opzioni.
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        services.AddMesAuthentication(configuration, new ProductionEnvironment());

        return services
            .BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
    }
}
