using System.Globalization;

using MesDataManager.Infrastructure;
using MesDataManager.Web;
using MesDataManager.Web.Components;
using MesDataManager.Web.Security;

using Microsoft.AspNetCore.Localization;
using Microsoft.Identity.Web.UI;

using MudBlazor.Services;

using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------- logging
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

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

// Cambio lingua: in Blazor Server la cultura si applica al circuito, quindi serve una
// richiesta HTTP vera e un ricaricamento della pagina.
app.MapGet("/culture/set", (string culture, string redirectUri, HttpContext http) =>
{
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
            });
    }

    return Results.LocalRedirect(string.IsNullOrWhiteSpace(redirectUri) ? "/" : redirectUri);
}).AllowAnonymous();

await app.RunAsync();
