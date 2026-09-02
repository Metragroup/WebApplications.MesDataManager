using MesDataManager.Application.Archives;
using MesDataManager.Application.Lookups;
using MesDataManager.Infrastructure.Archives;
using MesDataManager.Infrastructure.Lookups;
using MesDataManager.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MesDataManager.Infrastructure;

/// <summary>Registrazione dei servizi di infrastruttura.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Collega il database MES e i servizi che lavorano sulle anagrafiche. Non esegue e non
    /// genera migration: il database e' pre-esistente e resta di proprieta' del MES.
    /// </summary>
    public static IServiceCollection AddMesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MesDatabase");

        // Il valore predefinito in appsettings.json e' una stringa vuota, non un'assenza:
        // controllare solo il null lascerebbe passare la configurazione non compilata e
        // l'errore arriverebbe alla prima query, come "ConnectionString non inizializzata".
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Manca la stringa di connessione 'MesDatabase'. In sviluppo impostarla con " +
                "'dotnet user-secrets set \"ConnectionStrings:MesDatabase\" \"...\"' dalla " +
                "cartella src\\MesDataManager.Web.");
        }

        // Factory e non contesto registrato: in Blazor Server i servizi con ambito vivono
        // quanto il circuito, cioe' quanto l'intera sessione dell'utente. Un DbContext con
        // quella durata sarebbe condiviso fra tutti i componenti della pagina — due
        // operazioni sovrapposte lo fanno cadere — e terrebbe aperta la connessione.
        // Con la factory ogni operazione crea e smaltisce il proprio contesto.
        services.AddDbContextFactory<MesDbContext>(options => options
            .UseSqlServer(connectionString, sql => sql
                .EnableRetryOnFailure()
                .CommandTimeout(30)));

        services.AddMemoryCache();

        services.AddSingleton<IArchiveCatalog, ArchiveCatalog>();
        services.AddScoped<IArchiveService, ArchiveService>();
        services.AddScoped<ILookupProvider, LookupProvider>();

        return services;
    }
}
