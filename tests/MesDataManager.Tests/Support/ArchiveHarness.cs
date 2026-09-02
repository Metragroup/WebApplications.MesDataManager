using MesDataManager.Application.Archives;
using MesDataManager.Application.Security;
using MesDataManager.Infrastructure.Archives;
using MesDataManager.Infrastructure.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MesDataManager.Tests.Support;

/// <summary>Permessi finti, per provare la matrice di autorizzazione senza un tenant.</summary>
internal sealed class FakeUserContext(UserPermissions permissions) : IUserContext
{
    public ValueTask<UserPermissions> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        new(permissions);
}

/// <summary>Insiemi di permessi ricorrenti nei test.</summary>
internal static class Users
{
    public static readonly UserPermissions Anonymous = UserPermissions.Anonymous;

    public static readonly UserPermissions Reader = new()
    {
        UserName = "lettore",
        IsAuthenticated = true,
        CanRead = true,
    };

    public static readonly UserPermissions Editor = new()
    {
        UserName = "redattore",
        IsAuthenticated = true,
        CanRead = true,
        CanInsert = true,
        CanUpdate = true,
    };

    public static readonly UserPermissions Administrator = new()
    {
        UserName = "amministratore",
        IsAuthenticated = true,
        CanRead = true,
        CanInsert = true,
        CanUpdate = true,
        CanDelete = true,
    };
}

/// <summary>
/// Banco di prova del servizio anagrafiche su SQLite in memoria.
/// <para>
/// SQLite e' un provider relazionale vero: il servizio percorre lo stesso codice che usa con
/// SQL Server — change tracker, LIKE, vincoli di chiave — quindi le regole si verificano senza
/// un database di sviluppo. Cio' che SQLite non riproduce sono i tipi di colonna e i codici di
/// errore di SQL Server: quelli restano verificabili solo sul database reale.
/// </para>
/// </summary>
internal sealed class ArchiveHarness : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<MesDbContext> _options;

    public ArchiveHarness()
    {
        // La connessione va tenuta aperta: chiuderla cancella il database in memoria.
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<MesDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public IArchiveCatalog Catalog { get; } = new ArchiveCatalog();

    public MesDbContext CreateContext() => new(_options);

    /// <summary>Servizio configurato con i permessi indicati.</summary>
    public IArchiveService ServiceFor(UserPermissions permissions) =>
        new ArchiveService(
            new Factory(_options),
            Catalog,
            new FakeUserContext(permissions),
            NullLogger<ArchiveService>.Instance);

    /// <summary>Inserisce dati di partenza aggirando il servizio, per non presupporne le regole.</summary>
    public void Seed(params object[] entities)
    {
        using var context = CreateContext();
        context.AddRange(entities);
        context.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private sealed class Factory(DbContextOptions<MesDbContext> options) : IDbContextFactory<MesDbContext>
    {
        public MesDbContext CreateDbContext() => new(options);
    }
}
