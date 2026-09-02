using MesDataManager.Application.Security;

using Microsoft.AspNetCore.Components.Authorization;

namespace MesDataManager.Web.Security;

/// <summary>
/// Ricava identita' e permessi dallo stato di autenticazione dei componenti.
/// <para>
/// Non legge <c>HttpContext</c> di proposito: in rendering interattivo esiste soltanto durante
/// il prerendering, e appena il circuito e' stabilito diventa nullo. Un contesto utente
/// costruito su <c>IHttpContextAccessor</c> risulterebbe quindi non autenticato subito dopo il
/// primo disegno della pagina, negando ogni permesso — compresa la lettura.
/// </para>
/// <para>
/// <see cref="AuthenticationStateProvider"/> invece e' popolato in entrambi i percorsi: dal
/// renderer nel rendering statico e dal circuito in quello interattivo.
/// </para>
/// </summary>
public sealed class AuthenticationStateUserContext(AuthenticationStateProvider stateProvider) : IUserContext
{
    public async ValueTask<UserPermissions> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var state = await stateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);

        return UserPermissions.From(state.User);
    }
}
