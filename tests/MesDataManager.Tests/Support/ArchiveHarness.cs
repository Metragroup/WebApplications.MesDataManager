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

    /// <summary>
    /// Scrive ma non elimina. Non corrisponde a nessun ruolo Entra ID: e' una combinazione di
    /// permessi, e serve a verificare che il servizio controlli <c>CanDelete</c> per conto suo
    /// invece di dedurlo dal poter modificare.
    /// </summary>
    public static readonly UserPermissions Writer = new()
    {
        UserName = "redattore",
        IsAuthenticated = true,
        CanRead = true,
        CanInsert = true,
        CanUpdate = true,
    };

    /// <summary>
    /// Scrive i dati di produzione e non le anagrafiche: e' <c>Production.Editor</c>. Serve a
    /// verificare che i due ambiti restino separati.
    /// </summary>
    public static readonly UserPermissions ProductionWriter = new()
    {
        UserName = "produzione",
        IsAuthenticated = true,
        CanRead = true,
        CanEditProduction = true,
    };

    public static readonly UserPermissions Administrator = new()
    {
        UserName = "amministratore",
        IsAuthenticated = true,
        CanRead = true,
        CanInsert = true,
        CanUpdate = true,
        CanDelete = true,
        CanEditProduction = true,
        IsAdministrator = true,
    };

    /// <summary>
    /// Ha insieme <c>Archive.Editor</c> e <c>Production.Editor</c>: ne esce la stessa
    /// combinazione di permessi dell'amministratore, <b>senza</b> esserlo. Serve a verificare che
    /// lo sblocco forzato di un lotto guardi il ruolo e non la somma dei permessi.
    /// </summary>
    public static readonly UserPermissions BothEditors = new()
    {
        UserName = "doppio.ruolo",
        IsAuthenticated = true,
        CanRead = true,
        CanInsert = true,
        CanUpdate = true,
        CanDelete = true,
        CanEditProduction = true,
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
