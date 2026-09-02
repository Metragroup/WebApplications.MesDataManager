using System.Linq.Expressions;
using System.Reflection;

using Microsoft.EntityFrameworkCore;

namespace MesDataManager.Infrastructure.Archives;

/// <summary>
/// Costruisce filtri e ordinamenti a partire dai nomi di campo dei descrittori. Serve perche'
/// il servizio anagrafiche lavora su tabelle diverse con lo stesso codice: le espressioni
/// vengono composte a runtime e tradotte in SQL, cosi' filtro e ordinamento restano a database.
/// </summary>
internal static class ArchiveQueryBuilder
{
    private static readonly MethodInfo LikeMethod = typeof(DbFunctionsExtensions).GetMethod(
        nameof(DbFunctionsExtensions.Like),
        [typeof(DbFunctions), typeof(string), typeof(string)])!;

    /// <summary>Restringe alle sole voci attive.</summary>
    public static IQueryable<TEntity> WhereActive<TEntity>(IQueryable<TEntity> source)
        where TEntity : class
    {
        var entity = Expression.Parameter(typeof(TEntity), "e");
        var body = Expression.Equal(
            Expression.Property(entity, "IsActive"),
            Expression.Constant(true));

        return source.Where(Expression.Lambda<Func<TEntity, bool>>(body, entity));
    }

    /// <summary>
    /// Applica una ricerca "contiene" in OR sui campi indicati. Usa LIKE, quindi la
    /// sensibilita' a maiuscole e accenti segue il collation della colonna.
    /// </summary>
    public static IQueryable<TEntity> WhereMatches<TEntity>(
        IQueryable<TEntity> source,
        IReadOnlyList<string> fields,
        string term)
        where TEntity : class
    {
        if (fields.Count == 0 || string.IsNullOrWhiteSpace(term))
        {
            return source;
        }

        var pattern = Expression.Constant($"%{Escape(term.Trim())}%");
        var functions = Expression.Constant(EF.Functions);
        var entity = Expression.Parameter(typeof(TEntity), "e");

        Expression? predicate = null;
        foreach (var field in fields)
        {
            var property = typeof(TEntity).GetProperty(field);
            if (property is null || property.PropertyType != typeof(string))
            {
                continue;
            }

            var like = Expression.Call(
                LikeMethod,
                functions,
                Expression.Property(entity, property),
                pattern);

            predicate = predicate is null ? like : Expression.OrElse(predicate, like);
        }

        return predicate is null
            ? source
            : source.Where(Expression.Lambda<Func<TEntity, bool>>(predicate, entity));
    }

    /// <summary>Applica l'ordinamento predefinito dichiarato dal descrittore.</summary>
    public static IQueryable<TEntity> OrderByFields<TEntity>(
        IQueryable<TEntity> source,
        IReadOnlyList<string> fields)
        where TEntity : class
    {
        var ordered = source;
        for (var i = 0; i < fields.Count; i++)
        {
            var property = typeof(TEntity).GetProperty(fields[i]);
            if (property is null)
            {
                continue;
            }

            var entity = Expression.Parameter(typeof(TEntity), "e");
            var selector = Expression.Lambda(Expression.Property(entity, property), entity);

            var method = i == 0 ? nameof(Queryable.OrderBy) : nameof(Queryable.ThenBy);
            var call = Expression.Call(
                typeof(Queryable),
                method,
                [typeof(TEntity), property.PropertyType],
                ordered.Expression,
                Expression.Quote(selector));

            ordered = (IOrderedQueryable<TEntity>)ordered.Provider.CreateQuery<TEntity>(call);
        }

        return ordered;
    }

    /// <summary>Neutralizza i caratteri jolly di LIKE inseriti dall'utente.</summary>
    private static string Escape(string term) => term
        .Replace("[", "[[]", StringComparison.Ordinal)
        .Replace("%", "[%]", StringComparison.Ordinal)
        .Replace("_", "[_]", StringComparison.Ordinal);
}
