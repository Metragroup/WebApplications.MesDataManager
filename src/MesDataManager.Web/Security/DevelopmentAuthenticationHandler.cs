using System.Security.Claims;
using System.Text.Encodings.Web;

using MesDataManager.Application.Security;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MesDataManager.Web.Security;

/// <summary>Opzioni dell'utente finto usato in sviluppo.</summary>
public sealed class DevelopmentAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "DevelopmentAuth";

    /// <summary>Nome mostrato in barra e scritto nei log delle operazioni.</summary>
    public string UserName { get; set; } = "sviluppatore.locale";

    public string DisplayName { get; set; } = "Utente di sviluppo";

    /// <summary>
    /// Ruoli attribuiti all'utente finto. Cambiarli in <c>appsettings.Development.json</c> e' il
    /// modo piu' rapido per provare che i permessi funzionino davvero:
    /// <list type="bullet">
    /// <item>solo <c>Reader</c>: nessun pulsante di scrittura, resta la scheda in
    /// consultazione;</item>
    /// <item>array vuoto: accesso negato, la griglia non si apre nemmeno in lettura;</item>
    /// <item>togliere <c>Production.Editor</c>: nessuna differenza, le pagine di produzione
    /// non esistono ancora.</item>
    /// </list>
    /// <para>
    /// L'elenco parte vuoto e non con i tre ruoli di scrittura, per quanto sarebbe stato piu'
    /// comodo: <c>Bind</c> fonde gli array per indice, quindi un predefinito non vuoto
    /// lascerebbe in coda i ruoli che si stavano togliendo. Con
    /// <c>"Roles": [ "Reader" ]</c> sopra un predefinito di tre voci si ottengono tre ruoli, di
    /// cui due invisibili in configurazione — e la prova dei permessi darebbe l'esito
    /// sbagliato senza dire perche'.
    /// </para>
    /// </summary>
    public string[] Roles { get; set; } = [];
}

/// <summary>
/// Autentica automaticamente ogni richiesta con un utente fittizio, per poter lavorare sulle
/// anagrafiche prima che esista la registrazione applicativa su Entra ID.
/// <para>
/// Non e' un percorso alternativo di login: e' un cortocircuito completo dell'autenticazione.
/// Per questo <see cref="AuthenticationSetup"/> lo registra solo in ambiente Development e
/// solo su richiesta esplicita in configurazione, e in qualunque altro ambiente l'avvio
/// dell'applicazione fallisce invece di degradare in silenzio.
/// </para>
/// </summary>
public sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<DevelopmentAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<DevelopmentAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>
        {
            new("preferred_username", Options.UserName),
            new("name", Options.DisplayName),
            new(ClaimTypes.Name, Options.UserName),
        };

        claims.AddRange(Options.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        // Il tipo di claim per i ruoli va dichiarato, altrimenti IsInRole non li trova
        // e i controlli sui permessi risultano tutti negativi.
        var identity = new ClaimsIdentity(
            claims,
            DevelopmentAuthenticationOptions.SchemeName,
            ClaimTypes.Name,
            ClaimTypes.Role);

        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            DevelopmentAuthenticationOptions.SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
