using MesDataManager.Infrastructure.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MesDataManager.Tests.Support;

/// <summary>
/// Crea su SQLite una tabella per ogni entita' che il modello mappa a una <b>vista</b> di SQL
/// Server.
/// <para>
/// Serve perche' <c>EnsureCreated</c> ignora le viste — giustamente: a database le crea il MES,
/// non questa applicazione — e quindi una query su <c>EF.Module_ModuleTrans</c> fallirebbe nei
/// test con "no such table", pur essendo corretta in esercizio.
/// </para>
/// <para>
/// Le colonne e i tipi si ricavano dal modello, non da un DDL scritto a mano: una vista mappata
/// in futuro riceve la sua tabella di prova senza che nessuno debba ricordarsene, e una colonna
/// rinominata non lascia indietro una tabella di test che non combacia piu'.
/// </para>
/// <para>
/// Resta fuori quello che una vista di SQL Server ha e una tabella SQLite no: viste che
/// aggregano, colonne calcolate, e il fatto che una vista non sia scrivibile. Su quest'ultimo
/// punto il modello e' comunque piu' severo dei test — EF rifiuta il salvataggio di un'entita'
/// mappata a una vista.
/// </para>
/// </summary>
internal static class ViewTables
{
    public static void Create(MesDbContext context)
    {
        foreach (var entityType in context.Model.GetEntityTypes())
        {
            if (entityType.GetViewName() is not { } viewName)
            {
                continue;
            }

            var view = StoreObjectIdentifier.View(viewName, entityType.GetViewSchema());

            var columns = entityType.GetProperties()
                .Select(p => $"\"{p.GetColumnName(view)}\" {StoreType(p)}")
                .ToList();

            if (columns.Count == 0)
            {
                continue;
            }

            // SQLite non conosce gli schemi: il nome della tabella e' quello della vista, come
            // conferma l'errore che si otteneva senza questa classe ("no such table:
            // Module_ModuleTrans", non "EF.Module_ModuleTrans").
            //
            // Il DDL si compone per concatenazione perche' i nomi non sono parametrizzabili. Non
            // e' un rischio di injection: nomi e tipi arrivano dal modello EF compilato, non da
            // un input. La stringa passa da una variabile per non far scattare EF1002, che
            // guarda la forma dell'argomento e non la sua provenienza.
            var ddl = $"CREATE TABLE IF NOT EXISTS \"{viewName}\" ({string.Join(", ", columns)})";
            context.Database.ExecuteSqlRaw(ddl);
        }
    }

    /// <summary>
    /// Inserisce una riga in una tabella creata da <see cref="Create"/>.
    /// <para>
    /// Serve perche' EF <b>rifiuta</b> di salvare un'entita' mappata a una vista — ed e' una
    /// proprieta' voluta, verificata da <c>MesDbContextMappingTests</c>: impedisce a una
    /// scrittura distratta di finire sui dati della raccolta dati del MES. Nei test i dati di
    /// partenza vanno quindi scritti fuori dal tracciamento, come li scriverebbe il MES.
    /// </para>
    /// </summary>
    public static void Insert(MesDbContext context, object entity)
    {
        var entityType = context.Model.FindEntityType(entity.GetType())
            ?? throw new InvalidOperationException($"{entity.GetType().Name} non e' nel modello.");

        var viewName = entityType.GetViewName()
            ?? throw new InvalidOperationException($"{entityType.DisplayName()} non e' una vista.");

        var view = StoreObjectIdentifier.View(viewName, entityType.GetViewSchema());

        var properties = entityType.GetProperties()
            .Where(p => p.PropertyInfo is not null)
            .ToList();

        var columns = properties.Select(p => $"\"{p.GetColumnName(view)}\"");
        var placeholders = properties.Select((_, i) => $"@p{i}");
        // I parametri sono SqliteParameter e non valori nudi: un null passato come DBNull in
        // un object[] fa fallire ExecuteSqlRaw, che cerca una mappatura di tipo per DBNull.
        var values = properties
            .Select((p, i) => (object)new SqliteParameter($"p{i}", p.PropertyInfo!.GetValue(entity) ?? DBNull.Value))
            .ToArray();

        // Concatenazione per gli identificatori, parametri per i valori: gli stessi non sono
        // parametrizzabili, e arrivano dal modello compilato.
        var sql = $"INSERT INTO \"{viewName}\" ({string.Join(", ", columns)}) " +
                  $"VALUES ({string.Join(", ", placeholders)})";

        context.Database.ExecuteSqlRaw(sql, values);
    }

    /// <summary>Vero se l'entita' e' mappata a una vista, e va quindi seminata con SQL.</summary>
    public static bool IsView(MesDbContext context, Type clrType) =>
        context.Model.FindEntityType(clrType)?.GetViewName() is not null;

    /// <summary>
    /// Tipo di colonna secondo il provider configurato sul contesto, cioe' SQLite: prenderlo dal
    /// modello evita di tradurre a mano i tipi di SQL Server, che e' esattamente l'errore che
    /// aveva fatto cadere la suite con <c>varchar(max)</c>.
    /// </summary>
    private static string StoreType(IProperty property) =>
        property.GetRelationalTypeMapping().StoreType;
}
