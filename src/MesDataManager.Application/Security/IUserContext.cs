using System.Security.Claims;

namespace MesDataManager.Application.Security;

/// <summary>
/// Ruoli applicativi. Vanno mappati sui ruoli dell'app registration di Entra ID
/// (claim "roles") e non su gruppi di dominio, per non dipendere dal tenant.
/// </summary>
/// <remarks>
/// Un ruolo globale, uno di sola consultazione e uno per ambito di scrittura. Servono tutti:
/// senza almeno un ruolo non si vedono i dati, quindi assegnare l'applicazione a qualcuno
/// significa sempre scegliergli un ruolo.
/// </remarks>
public static class AppRoles
{
    /// <summary>Controllo completo: ogni ambito, presente e futuro.</summary>
    public const string Administrator = "Administrator";

    /// <summary>
    /// Consultazione di tutti i dati, anagrafiche e produzione. Non e' un ambito: la lettura non
    /// si divide, si divide la scrittura.
    /// </summary>
    public const string Reader = "Reader";

    /// <summary>Gestione delle anagrafiche: inserimento, modifica ed eliminazione.</summary>
    public const string ArchiveEditor = "Archive.Editor";

    /// <summary>
    /// Gestione dei dati di produzione: lotti, billette, fermate.
    /// <para>
    /// Sulle anagrafiche non concede nulla, ed e' il motivo per cui l'ambito sta nel nome del
    /// ruolo: vedi <see cref="UserPermissions.CanEditProduction"/>, che e' un permesso separato
    /// da <see cref="UserPermissions.CanInsert"/>/<see cref="UserPermissions.CanUpdate"/>/
    /// <see cref="UserPermissions.CanDelete"/> proprio per non mescolare i due ambiti.
    /// </para>
    /// </summary>
    public const string ProductionEditor = "Production.Editor";
}

/// <summary>
/// Istantanea di identita' e permessi dell'utente corrente. E' un valore, non un servizio:
/// una volta ricavata resta valida per l'operazione in corso e si puo' leggere da markup
/// sincrono senza dover interrogare di nuovo il provider di autenticazione.
/// </summary>
public sealed record UserPermissions
{
    /// <summary>Nessun permesso: e' cio' che si ottiene fuori da una sessione autenticata.</summary>
    public static readonly UserPermissions Anonymous = new();

    public string UserName { get; init; } = "anonimo";

    public string? DisplayName { get; init; }

    public bool IsAuthenticated { get; init; }

    public bool CanRead { get; init; }

    public bool CanInsert { get; init; }

    public bool CanUpdate { get; init; }

    public bool CanDelete { get; init; }

    /// <summary>
    /// Scrive i dati di produzione (fermi macchina e, in futuro, lotti e billette). Separato da
    /// <see cref="CanInsert"/>/<see cref="CanUpdate"/>/<see cref="CanDelete"/>, che restano
    /// validi solo per le anagrafiche: senza questa separazione <c>Production.Editor</c>
    /// otterrebbe per errore anche la scrittura sulle anagrafiche. Stesso modello "chi modifica
    /// puo' anche eliminare" gia' usato per le anagrafiche: nessun permesso intermedio.
    /// </summary>
    public bool CanEditProduction { get; init; }

    /// <summary>
    /// Deriva i permessi dai claim.
    /// <para>
    /// Le anagrafiche le scrivono <c>Archive.Editor</c> e <c>Administrator</c>: inserimento,
    /// modifica ed eliminazione stanno insieme, un ruolo che modifica puo' anche eliminare.
    /// <c>Production.Editor</c> su questi tre non concede nulla — vede <see cref="CanEditProduction"/>
    /// per l'ambito produzione. Deciso il 3 settembre 2026, vedi <c>docs/decisioni-aperte.md</c>,
    /// voce A2.
    /// </para>
    /// <para>
    /// Per leggere serve un ruolo qualsiasi fra quelli noti: chi e' autenticato ma non ne ha
    /// nessuno non vede i dati, nemmeno con l'indirizzo diretto. Entra ID lo ferma prima
    /// (<c>Assignment required = Yes</c>), ma il controllo e' ripetuto qui di proposito —
    /// altrimenti la riservatezza dipenderebbe da un interruttore nel portale, e un domani
    /// spostato per un altro motivo aprirebbe le anagrafiche a tutto il tenant in silenzio.
    /// </para>
    /// </summary>
    public static UserPermissions From(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated is not true)
        {
            return Anonymous;
        }

        var userName =
            principal.FindFirst("preferred_username")?.Value
            ?? principal.FindFirst(ClaimTypes.Upn)?.Value
            ?? principal.FindFirst(ClaimTypes.Name)?.Value
            ?? "anonimo";

        var canWriteArchives =
            principal.IsInRole(AppRoles.ArchiveEditor) ||
            principal.IsInRole(AppRoles.Administrator);

        var isAdministrator = principal.IsInRole(AppRoles.Administrator);
        var canEditProduction = principal.IsInRole(AppRoles.ProductionEditor) || isAdministrator;

        var canRead =
            canWriteArchives ||
            canEditProduction ||
            principal.IsInRole(AppRoles.Reader);

        return new UserPermissions
        {
            UserName = userName,
            DisplayName = principal.FindFirst("name")?.Value ?? userName,
            IsAuthenticated = true,
            CanRead = canRead,
            CanInsert = canWriteArchives,
            CanUpdate = canWriteArchives,
            CanDelete = canWriteArchives,
            CanEditProduction = canEditProduction,
        };
    }
}

/// <summary>
/// Sorgente dell'identita' corrente. Sostituisce l'<c>AuthService</c> WinForms, che restituiva
/// permessi costanti.
/// <para>
/// E' asincrona di proposito: l'implementazione lato Blazor Server legge lo stato di
/// autenticazione del circuito, che si ottiene con un'attesa. Un'interfaccia sincrona
/// costringerebbe a bloccare un thread oppure a leggere <c>HttpContext</c>, che in rendering
/// interattivo non e' disponibile.
/// </para>
/// </summary>
public interface IUserContext
{
    ValueTask<UserPermissions> GetCurrentAsync(CancellationToken cancellationToken = default);
}
