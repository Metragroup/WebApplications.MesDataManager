using MesDataManager.Application.Security;
using MesDataManager.Web.Security;

using Microsoft.Extensions.Configuration;

namespace MesDataManager.Tests;

/// <summary>
/// L'utente finto dello sviluppo locale e' anche il banco di prova dei permessi: se i suoi ruoli
/// non sono quelli scritti in configurazione, la prova da' l'esito sbagliato e nessuno se ne
/// accorge. Questo e' l'unico test che tocca la modalita' di sviluppo, e c'e' per quel motivo.
/// </summary>
public sealed class DevelopmentAuthenticationOptionsTests
{
    [Fact]
    public void L_elenco_dei_ruoli_configurato_sostituisce_il_predefinito_e_non_si_somma()
    {
        // Regressione del 4 settembre 2026. Il predefinito erano i tre ruoli di scrittura, e
        // Bind fonde gli array per indice: "Roles": [ "Reader" ] sostituiva la voce 0 e
        // lasciava in coda Archive.Editor e Production.Editor. Chi provava la sola
        // consultazione vedeva i pulsanti di modifica e concludeva che i permessi non
        // funzionassero, mentre erano i ruoli a non essere quelli dichiarati.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:DevelopmentUser:Roles:0"] = AppRoles.Reader,
            })
            .Build();

        var options = new DevelopmentAuthenticationOptions();

        configuration.GetSection("Authentication:DevelopmentUser").Bind(options);

        Assert.Equal(new[] { AppRoles.Reader }, options.Roles);
    }
}
