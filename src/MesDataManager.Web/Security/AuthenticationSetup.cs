using System.Security.Claims;

using MesDataManager.Application.Security;

using Microsoft.AspNetCore.Authentication.Cookies;
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
    /// <summary>
    /// Percorso dell'uscita, relativo alla base dell'applicazione. Cancella il cookie e chiude
    /// la sessione su Entra ID; lo monta <c>Program.cs</c>.
    /// </summary>
    public const string SignOutPath = "uscita";

    /// <summary>
    /// Percorso su cui si atterra a uscita avvenuta. E' l'unica pagina raggiungibile senza
    /// autenticazione — deve esserlo, altrimenti l'uscita finirebbe con una richiesta di
    /// accesso — e la dichiarazione sta nella pagina stessa, con <c>[AllowAnonymous]</c>.
    /// </summary>
    public const string SignedOutPath = "uscita/eseguita";

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

            // Il nome predefinito del cookie (".AspNetCore.Cookies") e' lo stesso in ogni
            // applicazione ASP.NET Core. Sotto IIS questa convive con altre applicazioni sullo
            // stesso host: un cookie omonimo scritto da un'altra alla radice del sito
            // arriverebbe anche qui, dove non e' decifrabile, e rimanderebbe al login chi era
            // gia' collegato. Il percorso del cookie lo restringe comunque il path base.
            services.Configure<CookieAuthenticationOptions>(
                CookieAuthenticationDefaults.AuthenticationScheme,
                options => options.Cookie.Name = ".MesDataManager.Auth");

            LogRolesOnSignIn(services);

            // Dove si atterra a uscita avvenuta, cioe' al ritorno da Entra ID sul
            // SignedOutCallbackPath. Il valore predefinito e' la radice dell'applicazione, che
            // pretende l'autenticazione: l'uscita finirebbe con una richiesta di accesso, e chi
            // ha la sessione di Windows valida rientrerebbe subito senza accorgersi di essere
            // uscito. Vale anche per l'endpoint di uscita di Microsoft.Identity.Web.UI, che
            // rimanda a una Razor Page del pacchetto qui non montata.
            services.Configure<OpenIdConnectOptions>(
                OpenIdConnectDefaults.AuthenticationScheme,
                options => options.SignedOutRedirectUri = $"/{SignedOutPath}");
        }

        services.AddAuthorization(options =>
        {
            // Nessuna pagina e' raggiungibile senza autenticazione.
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        services.AddCascadingAuthenticationState();

        // Serve a Microsoft.Identity.Web per gli endpoint di accesso e uscita. Non va usato
        // per leggere l'utente dai componenti: vedi AuthenticationStateUserContext.
        services.AddHttpContextAccessor();

        services.AddScoped<IUserContext, AuthenticationStateUserContext>();

        return services;
    }

    /// <summary>
    /// Scrive nel log, a ogni accesso, i ruoli arrivati nel token.
    /// <para>
    /// Quando un utente dice di non avere i permessi che gli sono stati assegnati nel portale,
    /// le due cause — l'assegnazione che non e' finita nel token, oppure l'applicazione che non
    /// la legge — si distinguono solo vedendo cosa e' arrivato davvero. Senza questa riga il log
    /// dice che il token e' valido e nient'altro, perche' il contenuto e' considerato dato
    /// personale e resta nascosto.
    /// </para>
    /// <para>
    /// La configurazione va registrata dopo <c>AddMicrosoftIdentityWebApp</c>: le azioni di
    /// configurazione si eseguono nell'ordine di registrazione, quindi qui
    /// <c>OnTokenValidated</c> contiene gia' il gestore della libreria, che va richiamato prima
    /// del nostro — non sostituito.
    /// </para>
    /// </summary>
    private static void LogRolesOnSignIn(IServiceCollection services) =>
        services.Configure<OpenIdConnectOptions>(
            OpenIdConnectDefaults.AuthenticationScheme,
            options =>
            {
                var inner = options.Events.OnTokenValidated;

                options.Events.OnTokenValidated = async context =>
                {
                    await inner(context).ConfigureAwait(false);

                    var principal = context.Principal;

                    var roles = principal is null
                        ? []
                        : principal.Claims
                            .Where(claim =>
                                claim.Type == ClaimTypes.Role ||
                                claim.Type == AppRoles.RolesClaimType)
                            .Select(claim => $"{claim.Value} ({claim.Type})")
                            .ToArray();

                    context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger(typeof(AuthenticationSetup).FullName!)
                        .LogInformation(
                            "Accesso di {User}: ruoli nel token = {Roles}.",
                            principal?.FindFirst("preferred_username")?.Value ?? "(sconosciuto)",
                            roles.Length is 0 ? "(nessuno)" : string.Join("; ", roles));
                };
            });

    /// <summary>
    /// In modalita' di sviluppo non esistono gli endpoint di Microsoft.Identity.Web.UI,
    /// quindi la voce "Esci" non ha una destinazione valida e va nascosta.
    /// </summary>
    public static bool SupportsSignOut(this IConfiguration configuration) =>
        configuration.GetValue("Authentication:Mode", AuthenticationMode.EntraId) is AuthenticationMode.EntraId;
}
