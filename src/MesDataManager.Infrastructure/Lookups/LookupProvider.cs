using MesDataManager.Application.Lookups;
using MesDataManager.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace MesDataManager.Infrastructure.Lookups;

/// <summary>
/// Fornisce gli elenchi per i campi che puntano a un'altra tabella. Gli elenchi sono piccoli e
/// cambiano raramente, quindi restano in cache per pochi minuti: evita una query a ogni
/// apertura del form senza rischiare di mostrare dati troppo vecchi.
/// </summary>
public sealed class LookupProvider(
    IDbContextFactory<MesDbContext> contextFactory,
    IMemoryCache cache) : ILookupProvider
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyList<LookupItem>> GetAsync(
        string lookupKey,
        CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<IReadOnlyList<LookupItem>>($"lookup:{lookupKey}", out var cached) && cached is not null)
        {
            return cached;
        }

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<LookupItem> items = lookupKey switch
        {
            LookupKeys.Companies => await context.Companies
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.Description)
                .Select(c => new LookupItem(c.CompanyId, c.CompanyId + " - " + c.Description))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),

            LookupKeys.Ovens => await context.Ovens
                .AsNoTracking()
                .OrderBy(o => o.OvenId)
                .Select(o => new LookupItem(o.OvenId, o.OvenId + " - " + o.Description))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),

            LookupKeys.Presses => await context.Presses
                .AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.PressId)
                .Select(p => new LookupItem(p.PressId, p.PressId + " - " + p.Description))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),

            LookupKeys.Modules => await context.Modules
                .AsNoTracking()
                .OrderBy(m => m.ModuleId)
                .Select(m => new LookupItem(m.ModuleId, m.ModuleId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),

            LookupKeys.DowntimeTypes => await context.PressDowntimeTypes
                .AsNoTracking()
                .OrderBy(t => t.PressDowntimeTypeId)
                .Select(t => new LookupItem(t.PressDowntimeTypeId.ToString(), t.Description))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),

            // Stesso filtro del vecchio RepositoryService.GetPressDowntimeReasons: solo le
            // causali attive e con il master attivo.
            LookupKeys.DowntimeReasons => await context.PressDowntimeReasons
                .AsNoTracking()
                .Where(r => r.IsActive && r.IsActiveMaster)
                .OrderBy(r => r.Description)
                .Select(r => new LookupItem(r.PressDowntimeReasonId.ToString(), r.Description))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),

            _ => [],
        };

        cache.Set($"lookup:{lookupKey}", items, CacheLifetime);
        return items;
    }
}
