using MesDataManager.Application.Archives;
using MesDataManager.Application.Lookups;
using MesDataManager.Application.Production;
using MesDataManager.Infrastructure.Archives;
using MesDataManager.Infrastructure.Lookups;
using MesDataManager.Infrastructure.Persistence;
using MesDataManager.Infrastructure.Production;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
        services.AddScoped<IMachineDowntimeService, MachineDowntimeService>();
        services.AddScoped<IBatchService, BatchService>();
        services.AddScoped<IShiftCalendar, PressShiftCalendar>();
        services.AddScoped<IHomeIndicatorService, HomeIndicatorService>();

        // Diagnostica: l'indirizzo del servizio sta in configurazione e non e' un segreto — e'
        // un servizio di stabilimento raggiungibile solo dalla rete interna. I due valori si
        // leggono a mano invece di legarli con il binder, che vorrebbe un pacchetto in piu' per
        // due stringhe.
        var diagnosticsSection = configuration.GetSection(DiagnosticsOptions.SectionName);
        var diagnosticsOptions = new DiagnosticsOptions
        {
            BaseUrl = diagnosticsSection["BaseUrl"] ?? string.Empty,
            TimeoutSeconds = int.TryParse(diagnosticsSection["TimeoutSeconds"], out var seconds) && seconds > 0
                ? seconds
                : 30,
        };

        services.AddSingleton(Options.Create(diagnosticsOptions));

        services.AddHttpClient<IDiagnosticsClient, DiagnosticsClient>((provider, http) =>
        {
            var options = provider
                .GetRequiredService<IOptions<DiagnosticsOptions>>()
                .Value;

            http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        services.AddScoped<IBatchDiagnosticsService, BatchDiagnosticsService>();

        return services;
    }
}
