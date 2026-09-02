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
        var connectionString = configuration.GetConnectionString("MesDatabase")
            ?? throw new InvalidOperationException(
                "Manca la stringa di connessione 'MesDatabase' nella configurazione.");

        services.AddDbContext<MesDbContext>(options => options
            .UseSqlServer(connectionString, sql => sql
                .EnableRetryOnFailure()
                .CommandTimeout(30))
            // Il database non e' gestito dall'applicazione: qualunque scostamento dello schema
            // deve emergere come errore in fase di query, non essere corretto in automatico.
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        services.AddMemoryCache();

        services.AddSingleton<IArchiveCatalog, ArchiveCatalog>();
        services.AddScoped<IArchiveService, ArchiveService>();
        services.AddScoped<ILookupProvider, LookupProvider>();

        return services;
    }
}
