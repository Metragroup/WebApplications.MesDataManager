using MesDataManager.Application.Lookups;
using MesDataManager.Application.Production;
using MesDataManager.Application.Security;
using MesDataManager.Domain.Entities;
using MesDataManager.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MesDataManager.Infrastructure.Production;

/// <summary>
/// Implementazione dei fermi macchina. A differenza del servizio anagrafiche non c'e' un
/// catalogo di metadati dietro: la tabella e' una sola, i filtri sono tipizzati e la query resta
/// scritta a mano perche' deve restare sargable su una tabella da 4,59 milioni di righe.
/// </summary>
public sealed class MachineDowntimeService(
    IDbContextFactory<MesDbContext> contextFactory,
    IUserContext user,
    ILookupProvider lookups,
    ILogger<MachineDowntimeService> logger) : IMachineDowntimeService
{
    public async Task<MachineDowntimePage> GetPageAsync(
        MachineDowntimeQuery query,
        CancellationToken cancellationToken = default)
    {
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!permissions.CanRead)
        {
            throw ProductionException.Forbidden();
        }

        var typeDescription = await ResolveTypeDescriptionAsync(query.DowntimeTypeId, cancellationToken)
            .ConfigureAwait(false);
        MachineDowntimePeriodPolicy.Validate(query.From, query.To, typeDescription);

        // Il periodo arriva come coppia di date: il confronto usa un estremo superiore
        // esclusivo (il giorno dopo "al") invece di normalizzare "al" a 23:59:59, stesso
        // risultato del vecchio RepositoryService ma senza il valore magico.
        var from = query.From.Date;
        var toExclusive = query.To.Date.AddDays(1);
        var downtimeType = (byte)query.DowntimeTypeId;

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var filtered = context.BatchDowntimes
            .AsNoTracking()
            .Where(d => d.DowntimeType == downtimeType)
            .Where(d => d.StopTs >= from && d.StartTs < toExclusive);

        if (query.PressId is { } pressId)
        {
            filtered = filtered.Where(d => d.PressId == pressId);
        }

        if (query.DowntimeReasonId is { } reasonId)
        {
            filtered = filtered.Where(d => d.DowntimeReasonId == reasonId);
        }

        var totalCount = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await WithDescriptions(context, filtered)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new MachineDowntimePage(rows, totalCount);
    }

    public async Task<IReadOnlyList<MachineDowntimeRow>> GetForBatchAsync(
        string pressId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!permissions.CanRead)
        {
            throw ProductionException.Forbidden();
        }

        var macrofermo = await ResolveMacrofermoIdAsync(cancellationToken).ConfigureAwait(false);

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Stessa sovrapposizione usata dal report: il fermo interessa il lotto se i due
        // intervalli hanno un istante in comune.
        var filtered = context.BatchDowntimes
            .AsNoTracking()
            .Where(d => d.PressId == pressId)
            .Where(d => d.DowntimeType == macrofermo)
            .Where(d => d.StopTs >= from && d.StartTs <= to);

        return await WithDescriptions(context, filtered)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveAsync(MachineDowntimeEditModel model, CancellationToken cancellationToken = default)
    {
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!permissions.CanEditProduction)
        {
            throw ProductionException.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(model.PressId))
        {
            throw ProductionException.Required(nameof(model.PressId));
        }

        if (model.StopTs <= model.StartTs)
        {
            throw ProductionException.PeriodInvalid();
        }

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await EnsureNoOverlapAsync(context, model, cancellationToken).ConfigureAwait(false);

        BatchDowntime entity;

        if (model.Id is { } id)
        {
            entity = await context.BatchDowntimes
                .SingleOrDefaultAsync(d => d.BatchDowntimeId == id, cancellationToken)
                .ConfigureAwait(false)
                ?? throw ProductionException.NotFound();

            entity.PressId = model.PressId;
            entity.StartTs = model.StartTs;
            entity.StopTs = model.StopTs;
            entity.DowntimeReasonId = model.DowntimeReasonId;
            entity.DowntimeType = (byte)model.DowntimeTypeId;
            entity.DowntimeCode = GenerateDowntimeCode(model.PressId, model.StopTs);
            entity.EditStatusId = "M";
        }
        else
        {
            entity = new BatchDowntime
            {
                PressId = model.PressId,
                StartTs = model.StartTs,
                StopTs = model.StopTs,
                DowntimeReasonId = model.DowntimeReasonId,
                DowntimeType = (byte)model.DowntimeTypeId,
                DowntimeCode = GenerateDowntimeCode(model.PressId, model.StopTs),
                FailureType = 0,
                EditStatusId = "N",
            };
            context.BatchDowntimes.Add(entity);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Fermi macchina: salvataggio non riuscito.");
            throw ProductionException.SaveFailed(ex);
        }

        logger.LogInformation(
            "Fermi macchina: {Action} il fermo {Id} da parte di {User}.",
            model.Id is null ? "inserito" : "modificato",
            entity.BatchDowntimeId,
            permissions.UserName);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!permissions.CanEditProduction)
        {
            throw ProductionException.Forbidden();
        }

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var entity = await context.BatchDowntimes
            .SingleOrDefaultAsync(d => d.BatchDowntimeId == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw ProductionException.NotFound();

        context.BatchDowntimes.Remove(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Fermi macchina: eliminato il fermo {Id} da parte di {User}.",
            id,
            permissions.UserName);
    }

    /// <summary>
    /// Aggiunge a una selezione di fermi le descrizioni di causale e tipo, ordinando dal piu'
    /// recente. La causale e' un join a sinistra: uno storico puo' riferire una causale
    /// disattivata, o non piu' presente, e la riga deve comunque comparire.
    /// </summary>
    private static IQueryable<MachineDowntimeRow> WithDescriptions(
        MesDbContext context,
        IQueryable<BatchDowntime> filtered) =>
        from d in filtered
        join r in context.PressDowntimeReasons on d.DowntimeReasonId equals r.PressDowntimeReasonId
            into reasonJoin
        from reason in reasonJoin.DefaultIfEmpty()
        join t in context.PressDowntimeTypes on d.DowntimeType equals (byte)t.PressDowntimeTypeId
        orderby d.StartTs descending
        select new MachineDowntimeRow(
            d.BatchDowntimeId,
            d.PressId,
            d.StartTs,
            d.StopTs,
            d.StopTs > d.StartTs ? d.StopTs - d.StartTs : TimeSpan.Zero,
            d.DowntimeCode,
            d.DowntimeReasonId,
            reason == null ? "" : reason.Description,
            t.PressDowntimeTypeId,
            t.Description);

    /// <summary>
    /// Id del tipo "Macrofermo", cercato per descrizione: l'id delle due righe di
    /// <c>MasterData.PressDowntimeType</c> dipende dai dati e non e' una costante.
    /// </summary>
    private async Task<byte> ResolveMacrofermoIdAsync(CancellationToken cancellationToken)
    {
        var types = await lookups.GetAsync(LookupKeys.DowntimeTypes, cancellationToken).ConfigureAwait(false);
        var match = types.FirstOrDefault(t => t.Text == MachineDowntimePeriodPolicy.Macrofermo);

        return match is not null && byte.TryParse(match.Value, out var id)
            ? id
            : throw ProductionException.InvalidValue(nameof(MachineDowntimeQuery.DowntimeTypeId));
    }

    /// <summary>
    /// Mirror di <c>RepositoryService.BatchDowntimePeriodNotValid</c> del vecchio applicativo:
    /// due fermi sulla stessa pressa non possono coprire lo stesso istante.
    /// </summary>
    private static async Task EnsureNoOverlapAsync(
        MesDbContext context,
        MachineDowntimeEditModel model,
        CancellationToken cancellationToken)
    {
        var overlapping = await context.BatchDowntimes
            .AsNoTracking()
            .Where(d => d.PressId == model.PressId)
            .Where(d => model.Id == null || d.BatchDowntimeId != model.Id)
            .Where(d => d.StartTs < model.StopTs && d.StopTs > model.StartTs)
            .Select(d => d.BatchDowntimeId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (overlapping.Count > 0)
        {
            throw ProductionException.PeriodOverlap(overlapping);
        }
    }

    private async Task<string> ResolveTypeDescriptionAsync(short downtimeTypeId, CancellationToken cancellationToken)
    {
        var types = await lookups.GetAsync(LookupKeys.DowntimeTypes, cancellationToken).ConfigureAwait(false);
        var match = types.FirstOrDefault(t => t.Value == downtimeTypeId.ToString());

        return match?.Text ?? throw ProductionException.InvalidValue(nameof(MachineDowntimeQuery.DowntimeTypeId));
    }

    /// <summary>Mirror di <c>DowntimesPresenter.GenerateDowntimeCode</c> del vecchio applicativo.</summary>
    private static string GenerateDowntimeCode(string pressId, DateTime endTime) =>
        $"{pressId}{endTime:yyMMddHHmmss}";
}
