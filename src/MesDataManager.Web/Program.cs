using System.Globalization;

using MesDataManager.Infrastructure;
using MesDataManager.Web;
using MesDataManager.Web.Components;
using MesDataManager.Web.Security;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Microsoft.Identity.Web.UI;

using MudBlazor.Services;

using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------- logging
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

// ---------------------------------------------------------------------- chiavi di cifratura
// Queste chiavi proteggono il cookie di autenticazione e i cookie di correlazione di
// OpenID Connect. Con le impostazioni predefinite, sotto IIS finiscono nel registro
// dell'account dell'application pool: se il profilo utente non e' caricato diventano
// effimere, e a ogni riciclo del pool chi tenta di collegarsi riceve "Correlation failed"
// senza che nulla, nei log, dica perche'.
// Si configura solo in esercizio: in locale le impostazioni predefinite bastano.
var dataProtectionKeyPath = builder.Configuration["DataProtection:KeyPath"];

if (!string.IsNullOrWhiteSpace(dataProtectionKeyPath))
{
    var keys = builder.Services
        .AddDataProtection()
        // Dichiarato e non dedotto: il valore predefinito deriva dal percorso della cartella
        // dell'applicazione, che a ogni pubblicazione puo' cambiare, e cambiandolo tutti i
        // cookie emessi prima diventano illeggibili.
        .SetApplicationName("MesDataManager")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath));

    if (OperatingSystem.IsWindows())
    {
        // Le chiavi restano cifrate a riposo, legate all'account che le ha scritte: chi
        // leggesse la cartella non ne ricaverebbe nulla. Con un secondo server servirebbe
        // invece un certificato condiviso.
        keys.ProtectKeysWithDpapi();
    }
}

// ---------------------------------------------------------------------- autenticazione
// In esercizio: Entra ID via OpenID Connect, con i permessi presi dai ruoli dell'app
// registration (claim "roles") e non dai gruppi, cosi' l'applicazione non dipende dalla
// struttura organizzativa del tenant.
// In locale: utente fittizio, per lavorare prima che la registrazione esista.
// La scelta e' in Authentication:Mode; il dettaglio in Security/AuthenticationSetup.cs.
builder.Services.AddMesAuthentication(builder.Configuration, builder.Environment);

// ---------------------------------------------------------------------- localizzazione
// Le tre lingue dell'applicazione WinForms, con le stesse traduzioni.
var supportedCultures = new[] { "it", "en", "fr" };

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.SetDefaultCulture(supportedCultures[0])
        .AddSupportedCultures(supportedCultures)
        .AddSupportedUICultures(supportedCultures);

    // La lingua scelta resta in un cookie: sopravvive al riavvio del browser senza
    // richiedere una tabella di preferenze utente.
    options.RequestCultureProviders =
    [
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider(),
    ];
});

// ---------------------------------------------------------------------- presentazione
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();

// ---------------------------------------------------------------------- dati
builder.Services.AddMesInfrastructure(builder.Configuration);

var app = builder.Build();

// Sotto IIS il PathBase rispecchia la capitalizzazione con cui e' stata digitata la
// richiesta, non quella dell'alias configurato in IIS: ".../MesDataManager" e
// ".../mesdatamanager" restano entrambi validi, ma generano un PathBase diverso l'uno
// dall'altro, e con esso un redirect_uri diverso per Entra ID, che li confronta lettera
// per lettera (voce C4 del registro delle decisioni). Si normalizza qui, prima di
// qualunque altra pipeline — autenticazione compresa — cosi' l'applicazione vede sempre
// lo stesso PathBase indipendentemente da come e' stata raggiunta.
app.Use(async (ctx, next) =>
{
    var pathBase = ctx.Request.PathBase.Value;
    if (!string.IsNullOrEmpty(pathBase))
    {
        var lower = pathBase.ToLowerInvariant();
        if (!string.Equals(pathBase, lower, StringComparison.Ordinal))
        {
            var target = lower + ctx.Request.Path + ctx.Request.QueryString;
            ctx.Response.Redirect(target, permanent: true);
            return;
        }
    }

    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRequestLocalization();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// Uscita. Non si usa l'endpoint di Microsoft.Identity.Web.UI: quello rimanda a una Razor Page
// del pacchetto, e questa applicazione monta componenti Blazor (MapRazorComponents) e non
// Razor Pages, quindi quell'indirizzo non esiste e il ritorno da Entra ID finisce altrove.
// Qui i due passi sono espliciti: prima il cookie dell'applicazione, poi la sessione su
// Entra ID. Il secondo e' quello che conta per i ruoli — cancellare solo il cookie farebbe
// rientrare l'utente senza interazione, con lo stesso token e quindi gli stessi ruoli di prima.
if (app.Configuration.SupportsSignOut())
{
    // Anonimo di proposito: se il cookie e' scaduto o illeggibile l'uscita deve comunque poter
    // chiudere la sessione su Entra ID, invece di pretendere un accesso per poterlo revocare.
    app.MapGet($"/{AuthenticationSetup.SignOutPath}", async (HttpContext http) =>
    {
        var pathBase = http.Request.PathBase.Value?.TrimEnd('/') ?? string.Empty;

        // Indirizzo assoluto: e' l'unico che il gestore di OpenID Connect non riscrive, e
        // sotto IIS deve contenere il percorso virtuale dell'applicazione.
        var landing =
            $"{http.Request.Scheme}://{http.Request.Host}{pathBase}/{AuthenticationSetup.SignedOutPath}";

        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignOutAsync(
            OpenIdConnectDefaults.AuthenticationScheme,
            new AuthenticationProperties { RedirectUri = landing });
    }).AllowAnonymous();
}

// Cambio lingua: in Blazor Server la cultura si applica al circuito, quindi serve una
// richiesta HTTP vera e un ricaricamento della pagina.
app.MapGet("/culture/set", (string culture, string? redirectUri, HttpContext http) =>
{
    var pathBase = http.Request.PathBase.Value?.TrimEnd('/') ?? string.Empty;

    if (supportedCultures.Contains(culture, StringComparer.OrdinalIgnoreCase))
    {
        http.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(
                new RequestCulture(new CultureInfo(culture))),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                HttpOnly = false,
                SameSite = SameSiteMode.Lax,

                // Il cookie vale per questa applicazione, non per tutto il sito: su
                // itbsintra01 convivono altre applicazioni e il nome del cookie di cultura
                // e' quello predefinito di ASP.NET Core, quindi identico al loro.
                Path = string.IsNullOrEmpty(pathBase) ? "/" : pathBase,
            });
    }

    // redirectUri arriva dal client come percorso relativo alla base dell'applicazione.
    // Il TrimStart neutralizza sia la forma assoluta sia "//host", che sarebbe un rinvio
    // fuori sito; il PathBase va riaggiunto perche' LocalRedirect scrive l'indirizzo
    // cosi' com'e' e sotto IIS un "/" iniziale punterebbe alla radice del server.
    var target = $"{pathBase}/{redirectUri?.TrimStart('/')}";

    return Results.LocalRedirect(target);
}).AllowAnonymous();

await app.RunAsync();
