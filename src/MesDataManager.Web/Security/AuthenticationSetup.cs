using MesDataManager.Application.Security;

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;

namespace MesDataManager.Web.Security;

/// <summary>Modalita' di autenticazione selezionabile da configurazione.</summary>
public enum AuthenticationMode
{
    /// <summary>Microsoft Entra ID via OpenID Connect. E' la modalita' di esercizio.</summary>
    EntraId,

    /// <summary>Utente fittizio, solo per lo sviluppo locale.</summary>
    Development,
}

/// <summary>Configura l'autenticazione scegliendo fra Entra ID e utente di sviluppo.</summary>
public static class AuthenticationSetup
{
    public static IServiceCollection AddMesAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var mode = configuration.GetValue("Authentication:Mode", AuthenticationMode.EntraId);

        if (mode is AuthenticationMode.Development)
        {
            // Difesa in profondita': la modalita' di sviluppo salta l'autenticazione del tutto,
            // quindi non deve poter arrivare in produzione per una configurazione dimenticata.
            // Meglio un'applicazione che non parte di una che parte senza controlli.
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Authentication:Mode = Development e' ammesso solo in ambiente Development. " +
                    $"Ambiente corrente: {environment.EnvironmentName}. " +
                    "Configurare AzureAd e passare a Authentication:Mode = EntraId.");
            }

            services
                .AddAuthentication(DevelopmentAuthenticationOptions.SchemeName)
                .AddScheme<DevelopmentAuthenticationOptions, DevelopmentAuthenticationHandler>(
                    DevelopmentAuthenticationOptions.SchemeName,
                    options => configuration
                        .GetSection("Authentication:DevelopmentUser")
                        .Bind(options));
        }
        else
        {
            var azureAd = configuration.GetSection("AzureAd");

            // Il messaggio di errore di OpenID Connect su un tenant inesistente ("IDX20807")
            // non dice quale impostazione manca: meglio intercettarlo prima.
            foreach (var key in (string[])["TenantId", "ClientId"])
            {
                var value = azureAd[key];
                if (string.IsNullOrWhiteSpace(value) || value.StartsWith("COMPILARE", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"AzureAd:{key} non e' configurato. Compilare la sezione AzureAd con i dati " +
                        "della registrazione applicativa, oppure impostare " +
                        "Authentication:Mode = Development in appsettings.Development.json.");
                }
            }

            services
                .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApp(azureAd);
        }

        services.AddAuthorization(options =>
        {
            // Nessuna pagina e' raggiungibile senza autenticazione.
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        services.AddCascadingAuthenticationState();
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, EntraUserContext>();

        return services;
    }

    /// <summary>
    /// In modalita' di sviluppo non esistono gli endpoint di Microsoft.Identity.Web.UI,
    /// quindi la voce "Esci" non ha una destinazione valida e va nascosta.
    /// </summary>
    public static bool SupportsSignOut(this IConfiguration configuration) =>
        configuration.GetValue("Authentication:Mode", AuthenticationMode.EntraId) is AuthenticationMode.EntraId;
}
