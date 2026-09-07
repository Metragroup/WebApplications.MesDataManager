using MesDataManager.Application.Lookups;
using MesDataManager.Application.Production;
using MesDataManager.Application.Security;
using MesDataManager.Domain.Entities;
using MesDataManager.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace MesDataManager.Infrastructure.Production;

/// <summary>
/// Indicatori del pannello di apertura: due conteggi di stato sui lotti e i fermi della giornata
/// divisi per pressa e turno.
/// <para>
/// I fermi non portano ne' turno ne' lotto: hanno solo pressa e istanti. L'attribuzione al turno
/// si fa quindi confrontando l'inizio del fermo con le finestre dei turni, che arrivano dalla
/// definizione dell'impianto (<see cref="IShiftCalendar"/>) e non da una regola inventata qui.
/// </para>
/// </summary>
public sealed class HomeIndicatorService(
    IDbContextFactory<MesDbContext> contextFactory,
    IUserContext user,
    IShiftCalendar shifts,
    ILookupProvider lookups) : IHomeIndicatorService
{
    public async Task<HomeIndicators> GetAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!permissions.CanRead)
        {
            throw ProductionException.Forbidden();
        }

        var activity = await ActivityAsync(cancellationToken).ConfigureAwait(false);
        var windows = await shifts.GetShiftsAsync(day, cancellationToken).ConfigureAwait(false);

        if (windows.Count == 0)
        {
            return new HomeIndicators(day, activity, [], []);
        }

        var (macro, micro) = await DowntimesAsync(windows, cancellationToken).ConfigureAwait(false);

        return new HomeIndicators(day, activity, macro, micro);
    }

    /// <summary>
    /// Lotti aperti e lotti da riconciliare, per pressa. Il conteggio degli aperti usa le stesse
    /// condizioni della pagina "Lotti in corso" — almeno una billetta vera e lotto non ancora
    /// importato in ERP — altrimenti l'indicatore conterebbe lotti che quella pagina non mostra,
    /// e il numero non tornerebbe con l'elenco che si apre cliccandolo.
    /// </summary>
    private async Task<IReadOnlyList<PressActivity>> ActivityAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var open = await context.Batches
            .AsNoTracking()
            .Where(b => !b.IsErpImported)
            .Where(b => !b.IsPressClosed || !b.IsSawClosed)
            .Where(b => context.BatchBillets.Any(x => x.BatchId == b.BatchId && x.TypeId == BatchBilletType.Real))
            .GroupBy(b => b.PressId)
            .Select(g => new { PressId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var toReconcile = await context.Batches
            .AsNoTracking()
            .Where(b => b.IsErpMarked && !b.IsErpImported)
            .GroupBy(b => b.PressId)
            .Select(g => new { PressId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Una pressa compare se ha almeno uno dei due conteggi: quelle a zero su entrambi non
        // dicono niente e allungherebbero il pannello.
        return
        [
            .. open.Select(o => o.PressId)
                .Concat(toReconcile.Select(t => t.PressId))
                .Distinct()
                .Order(StringComparer.Ordinal)
                .Select(pressId => new PressActivity(
                    pressId.TrimEnd(),
                    open.FirstOrDefault(o => o.PressId == pressId)?.Count ?? 0,
                    toReconcile.FirstOrDefault(t => t.PressId == pressId)?.Count ?? 0))
        ];
    }

    /// <summary>
    /// Fermi della giornata, contati e sommati per pressa e turno, separando macrofermi e
    /// microfermi.
    /// <para>
    /// L'attribuzione avviene in memoria e non a database: le finestre sono una dozzina e i fermi
    /// di una giornata qualche centinaio, mentre spingere il confronto in SQL richiederebbe una
    /// giunzione per intervalli su una tabella da 4,59 milioni di righe. Dal database arriva solo
    /// la giornata, delimitata dalle finestre stesse.
    /// </para>
    /// </summary>
    private async Task<(IReadOnlyList<ShiftDowntimeTotals> Macro, IReadOnlyList<ShiftDowntimeTotals> Micro)>
        DowntimesAsync(IReadOnlyList<ShiftWindow> windows, CancellationToken cancellationToken)
    {
        var macrofermo = await ResolveTypeAsync(MachineDowntimePeriodPolicy.Macrofermo, cancellationToken)
            .ConfigureAwait(false);
        var microfermo = await ResolveTypeAsync(MachineDowntimePeriodPolicy.Microfermo, cancellationToken)
            .ConfigureAwait(false);

        var from = windows.Min(w => w.From);
        var to = windows.Max(w => w.To);
        var presses = windows.Select(w => w.PressId).Distinct().ToList();

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var downtimes = await context.BatchDowntimes
            .AsNoTracking()
            .Where(d => d.StartTs >= from && d.StartTs < to)
            .Where(d => presses.Contains(d.PressId))
            .Where(d => d.DowntimeType == macrofermo || d.DowntimeType == microfermo)
            .Select(d => new { d.PressId, d.DowntimeType, d.StartTs, d.StopTs })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byShift = downtimes
            .Select(d => new
            {
                d.DowntimeType,
                Duration = d.StopTs > d.StartTs ? d.StopTs - d.StartTs : TimeSpan.Zero,
                Shift = windows.FirstOrDefault(w =>
                    w.PressId == d.PressId && d.StartTs >= w.From && d.StartTs < w.To),
            })
            .Where(x => x.Shift is not null)
            .ToList();

        // Una riga per ogni turno della giornata, anche senza fermi: la scheda della pressa
        // mostra tutti i turni e uno zero dice "nessun fermo", mentre un turno assente
        // lascerebbe il dubbio che il dato manchi.
        IReadOnlyList<ShiftDowntimeTotals> Totals(byte type) =>
        [
            .. windows
                .OrderBy(w => w.PressId, StringComparer.Ordinal)
                .ThenBy(w => w.From)
                .Select(w =>
                {
                    var rows = byShift.Where(x => x.DowntimeType == type && x.Shift == w).ToList();

                    return new ShiftDowntimeTotals(
                        w.PressId,
                        w.ShiftId,
                        rows.Count,
                        rows.Aggregate(TimeSpan.Zero, (sum, x) => sum + x.Duration));
                })
        ];

        return (Totals(macrofermo), Totals(microfermo));
    }

    /// <summary>
    /// Id del tipo di fermo, cercato per descrizione: gli id delle due righe di
    /// <c>MasterData.PressDowntimeType</c> dipendono dai dati e non sono una costante.
    /// </summary>
    private async Task<byte> ResolveTypeAsync(string description, CancellationToken cancellationToken)
    {
        var types = await lookups.GetAsync(LookupKeys.DowntimeTypes, cancellationToken).ConfigureAwait(false);
        var match = types.FirstOrDefault(t => t.Text == description);

        return match is not null && byte.TryParse(match.Value, out var id)
            ? id
            : throw ProductionException.InvalidValue(nameof(MachineDowntimeQuery.DowntimeTypeId));
    }
}
