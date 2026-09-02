using System.Security.Claims;

using MesDataManager.Application.Security;

namespace MesDataManager.Web.Security;

/// <summary>
/// Legge identita' e permessi dai claim del token Entra ID. I ruoli sono gerarchici:
/// un amministratore puo' fare tutto quello che puo' fare un editor, e cosi' via, per non
/// dover assegnare tre ruoli alla stessa persona.
/// </summary>
public sealed class EntraUserContext(IHttpContextAccessor accessor) : IUserContext
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public string UserName =>
        Principal?.FindFirst("preferred_username")?.Value
        ?? Principal?.FindFirst(ClaimTypes.Upn)?.Value
        ?? Principal?.FindFirst(ClaimTypes.Name)?.Value
        ?? "anonimo";

    public string? DisplayName => Principal?.FindFirst("name")?.Value ?? UserName;

    public bool CanRead => IsAuthenticated;

    public bool CanInsert => HasAnyRole(AppRoles.ArchiveEditor, AppRoles.ArchiveAdministrator);

    public bool CanUpdate => HasAnyRole(AppRoles.ArchiveEditor, AppRoles.ArchiveAdministrator);

    public bool CanDelete => HasAnyRole(AppRoles.ArchiveAdministrator);

    private bool HasAnyRole(params string[] roles) =>
        Principal is not null && roles.Any(Principal.IsInRole);
}
