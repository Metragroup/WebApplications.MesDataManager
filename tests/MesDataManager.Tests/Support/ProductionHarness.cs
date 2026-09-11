using MesDataManager.Application.Production;
using MesDataManager.Application.Security;
using MesDataManager.Infrastructure.Lookups;
using MesDataManager.Infrastructure.Persistence;
using MesDataManager.Infrastructure.Production;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace MesDataManager.Tests.Support;

/// <summary>Turni prestabiliti, al posto della funzione di SQL Server.</summary>
internal sealed class FakeShiftCalendar(IReadOnlyList<ShiftWindow> shifts) : IShiftCalendar
{
    public Task<IReadOnlyList<ShiftWindow>> GetShiftsAsync(
        DateOnly day,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(shifts);
}

/// <summary>
/// Banco di prova dei fermi macchina su SQLite in memoria, sullo stesso modello di
/// <see cref="ArchiveHarness"/>. Il servizio riceve il <see cref="LookupProvider"/> vero: la
/// regola sul periodo massimo dipende dalla descrizione del tipo letta da database, quindi un
/// lookup finto salterebbe proprio il passaggio da verificare.
/// <para>
/// Cio' che SQLite non riproduce sono i tipi di colonna di SQL Server e il comportamento su
/// volumi reali: qui si verificano filtri, paginazione e regole, non i piani di esecuzione.
/// </para>
/// </summary>
internal sealed class ProductionHarness : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<MesDbContext> _options;

    public ProductionHarness()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<MesDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = CreateContext();
        context.Database.EnsureCreated();

        // EnsureCreated non crea le viste: senza queste tabelle la scheda del lotto non e'
        // verificabile qui, perche' incestamento e ordini di produzione le interrogano.
        ViewTables.Create(context);
    }

    public MesDbContext CreateContext() => new(_options);

    public IMachineDowntimeService ServiceFor(UserPermissions permissions) =>
        new MachineDowntimeService(
            new Factory(_options),
            new FakeUserContext(permissions),
            new LookupProvider(new Factory(_options), new MemoryCache(new MemoryCacheOptions())),
            NullLogger<MachineDowntimeService>.Instance);

    /// <summary>Servizio dei lotti configurato con i permessi indicati.</summary>
    public IBatchService BatchServiceFor(UserPermissions permissions) =>
        new BatchService(
            new Factory(_options),
            new FakeUserContext(permissions),
            NullLogger<BatchService>.Instance);

    /// <summary>
    /// Diagnostica con un client finto al posto del servizio HTTP: cio' che l'applicazione fa
    /// con la risposta — conservarla, inserire le billette mancanti, decidere l'esito — non ha
    /// bisogno di rete per essere verificato.
    /// </summary>
    public IBatchDiagnosticsService DiagnosticsServiceFor(
        UserPermissions permissions,
        IDiagnosticsClient client) =>
        new BatchDiagnosticsService(
            new Factory(_options),
            new FakeUserContext(permissions),
            client,
            NullLogger<BatchDiagnosticsService>.Instance);

    /// <summary>
    /// Indicatori di apertura con un calendario turni finto: la funzione di SQL Server che
    /// fornisce i turni veri non esiste su SQLite, mentre l'aggregazione va verificata qui.
    /// </summary>
    public IHomeIndicatorService HomeIndicatorServiceFor(
        UserPermissions permissions,
        IReadOnlyList<ShiftWindow> shifts) =>
        new HomeIndicatorService(
            new Factory(_options),
            new FakeUserContext(permissions),
            new FakeShiftCalendar(shifts),
            new LookupProvider(new Factory(_options), new MemoryCache(new MemoryCacheOptions())));

    /// <summary>
    /// Scrive i dati di partenza. Le entita' mappate a viste non passano dal tracciamento — EF
    /// rifiuta di salvarle, giustamente — e vengono inserite con SQL, come farebbe il MES.
    /// </summary>
    public void Seed(params object[] entities)
    {
        using var context = CreateContext();

        foreach (var entity in entities)
        {
            if (ViewTables.IsView(context, entity.GetType()))
            {
                ViewTables.Insert(context, entity);
            }
            else
            {
                context.Add(entity);
            }
        }

        context.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private sealed class Factory(DbContextOptions<MesDbContext> options) : IDbContextFactory<MesDbContext>
    {
        public MesDbContext CreateDbContext() => new(options);
    }
}
