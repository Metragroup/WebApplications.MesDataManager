using MesDataManager.Application.Archives;
using MesDataManager.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace MesDataManager.Infrastructure.Archives;

/// <summary>
/// Operazioni sul database indipendenti dal tipo di entita'. Il servizio anagrafiche ne
/// risolve una istanza per descrittore, cosi' il codice CRUD e' scritto una volta sola.
/// </summary>
internal interface IEntityAccessor
{
    Task<IReadOnlyList<ArchiveRow>> ListAsync(
        MesDbContext context,
        ArchiveDescriptor descriptor,
        ArchiveQuery query,
        CancellationToken cancellationToken);

    Task<object?> FindAsync(
        MesDbContext context,
        ArchiveDescriptor descriptor,
        ArchiveRow row,
        CancellationToken cancellationToken);

    object CreateInstance();

    void Add(MesDbContext context, object entity);

    void Remove(MesDbContext context, object entity);
}

/// <summary>Implementazione tipizzata, istanziata per riflessione da <see cref="EntityAccessorFactory"/>.</summary>
internal sealed class EntityAccessor<TEntity> : IEntityAccessor
    where TEntity : class, new()
{
    public async Task<IReadOnlyList<ArchiveRow>> ListAsync(
        MesDbContext context,
        ArchiveDescriptor descriptor,
        ArchiveQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<TEntity> source = context.Set<TEntity>().AsNoTracking();

        if (descriptor.SupportsActiveFilter && !query.IncludeInactive)
        {
            source = ArchiveQueryBuilder.WhereActive(source);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            source = ArchiveQueryBuilder.WhereMatches(source, descriptor.SearchableFields, query.Search);
        }

        source = ArchiveQueryBuilder.OrderByFields(source, descriptor.DefaultSort);

        var entities = await source.ToListAsync(cancellationToken).ConfigureAwait(false);

        return [.. entities.Select(entity => ToRow(entity, descriptor))];
    }

    public async Task<object?> FindAsync(
        MesDbContext context,
        ArchiveDescriptor descriptor,
        ArchiveRow row,
        CancellationToken cancellationToken)
    {
        var keyValues = descriptor.KeyFields
            .Select(field => FieldValueConverter.Coerce(
                row[field.Name],
                typeof(TEntity).GetProperty(field.Name)!.PropertyType))
            .ToArray();

        return await context.Set<TEntity>().FindAsync(keyValues, cancellationToken).ConfigureAwait(false);
    }

    public object CreateInstance() => new TEntity();

    public void Add(MesDbContext context, object entity) => context.Set<TEntity>().Add((TEntity)entity);

    public void Remove(MesDbContext context, object entity) => context.Set<TEntity>().Remove((TEntity)entity);

    /// <summary>Proietta l'entita' nei soli campi dichiarati dal descrittore.</summary>
    private static ArchiveRow ToRow(TEntity entity, ArchiveDescriptor descriptor)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in descriptor.Fields)
        {
            values[field.Name] = typeof(TEntity).GetProperty(field.Name)?.GetValue(entity);
        }

        return new ArchiveRow(values);
    }
}

/// <summary>Crea e mette in cache un accessor per ogni tipo di entita' del catalogo.</summary>
internal static class EntityAccessorFactory
{
    private static readonly Dictionary<Type, IEntityAccessor> Cache = [];
    private static readonly Lock Gate = new();

    public static IEntityAccessor For(Type entityType)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(entityType, out var cached))
            {
                return cached;
            }

            var accessor = (IEntityAccessor)Activator.CreateInstance(
                typeof(EntityAccessor<>).MakeGenericType(entityType))!;

            Cache[entityType] = accessor;
            return accessor;
        }
    }
}
