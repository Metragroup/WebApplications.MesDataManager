using MesDataManager.Web;

using Microsoft.Extensions.Configuration;

namespace MesDataManager.Tests;

/// <summary>
/// Server e database mostrati in fondo al menu. Sono un'informazione di cortesia, ma leggono la
/// stringa di connessione: quello che non deve succedere e' che ne esca altro, o che una stringa
/// malformata faccia cadere la pagina.
/// </summary>
public sealed class DatabaseIdentityTests
{
    [Fact]
    public void Server_e_database_arrivano_dalla_stringa_di_connessione()
    {
        var identity = DatabaseIdentity.From(
            @"Server=ITBSDB01\SCADA2014;Database=MES40_RDP_TEST;Integrated Security=True");

        Assert.Equal(@"ITBSDB01\SCADA2014", identity.Server);
        Assert.Equal("MES40_RDP_TEST", identity.Database);
    }

    [Fact]
    public void Le_credenziali_non_escono_dalla_stringa_di_connessione()
    {
        // Oggi la connessione e' a autenticazione integrata, ma se un domani passasse da utente
        // e password quei valori non devono comparire a schermo.
        var identity = DatabaseIdentity.From(
            "Server=srv;Database=db;User ID=mes_app;Password=segretissima");

        Assert.Equal("srv", identity.Server);
        Assert.Equal("db", identity.Database);
        Assert.DoesNotContain("segretissima", identity.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mes_app", identity.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Senza_stringa_di_connessione_resta_il_segnaposto(string? connectionString)
    {
        var identity = DatabaseIdentity.From(connectionString);

        Assert.Same(DatabaseIdentity.Unknown, identity);
    }

    [Fact]
    public void Una_stringa_malformata_non_fa_cadere_la_pagina()
    {
        // Che la connessione funzioni si scopre alla prima query, con un errore vero: qui si
        // mostra un segnaposto e si va avanti.
        var identity = DatabaseIdentity.From("questa non e' una stringa di connessione");

        Assert.Same(DatabaseIdentity.Unknown, identity);
    }

    [Fact]
    public void Una_stringa_senza_database_mostra_il_segnaposto_solo_su_quello()
    {
        var identity = DatabaseIdentity.From("Server=srv;Integrated Security=True");

        Assert.Equal("srv", identity.Server);
        Assert.Equal(DatabaseIdentity.Unknown.Database, identity.Database);
    }

    [Fact]
    public void La_stringa_si_legge_dalla_configurazione_col_nome_giusto()
    {
        // E' lo stesso nome che usa l'infrastruttura per aprire la connessione: se qui si
        // leggesse un'altra chiave, il menu direbbe un database e l'applicazione ne userebbe
        // un altro.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MesDatabase"] = "Server=srv;Database=db",
            })
            .Build();

        var identity = DatabaseIdentity.FromConfiguration(configuration);

        Assert.Equal("srv", identity.Server);
        Assert.Equal("db", identity.Database);
    }
}
