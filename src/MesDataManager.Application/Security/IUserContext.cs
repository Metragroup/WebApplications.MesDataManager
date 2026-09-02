using System.Security.Claims;

namespace MesDataManager.Application.Security;

/// <summary>
/// Ruoli applicativi. Vanno mappati sui ruoli dell'app registration di Entra ID
/// (claim "roles") e non su gruppi di dominio, per non dipendere dal tenant.
/// </summary>
public static class AppRoles
{
    /// <summary>Consultazione delle anagrafiche.</summary>
    public const string ArchiveReader = "Archive.Reader";

    /// <summary>Inserimento e modifica.</summary>
    public const string ArchiveEditor = "Archive.Editor";

    /// <summary>Eliminazione e operazioni sulle anagrafiche governate dall'ERP.</summary>
    public const string ArchiveAdministrator = "Archive.Administrator";
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
    /// Deriva i permessi dai claim. I ruoli sono gerarchici: un amministratore puo' fare tutto
    /// quello che puo' fare un editor, per non dover assegnare tre ruoli alla stessa persona.
    /// <para>
    /// La lettura coincide con l'essere autenticati: il ruolo <c>Archive.Reader</c> esiste ma non
    /// e' ancora richiesto da nulla. Vedi <c>docs/decisioni-aperte.md</c>, voce A2.
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

        var isEditor = principal.IsInRole(AppRoles.ArchiveEditor);
        var isAdministrator = principal.IsInRole(AppRoles.ArchiveAdministrator);

        return new UserPermissions
        {
            UserName = userName,
            DisplayName = principal.FindFirst("name")?.Value ?? userName,
            IsAuthenticated = true,
            CanRead = true,
            CanInsert = isEditor || isAdministrator,
            CanUpdate = isEditor || isAdministrator,
            CanDelete = isAdministrator,
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
