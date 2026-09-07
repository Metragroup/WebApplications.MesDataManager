using MesDataManager.Application.Production;
using MesDataManager.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace MesDataManager.Infrastructure.Production;

/// <summary>
/// I turni come li definisce l'impianto, dalla funzione <c>Press.ufn_GetShifts</c>.
/// <para>
/// La funzione lavora su una pressa alla volta, quindi si interroga una volta per pressa attiva:
/// sono quattro, e la risposta e' di tre righe ciascuna. Ricavare i turni dalle billette avrebbe
/// evitato queste chiamate, ma i turni senza produzione non comparirebbero e i fermi che cadono
/// prima della prima billetta resterebbero fuori da ogni conteggio.
/// </para>
/// </summary>
public sealed class PressShiftCalendar(IDbContextFactory<MesDbContext> contextFactory) : IShiftCalendar
{
    public async Task<IReadOnlyList<ShiftWindow>> GetShiftsAsync(
        DateOnly day,
        CancellationToken cancellationToken = default)
    {
        var from = day.ToDateTime(TimeOnly.MinValue);

        // La giornata di produzione sfora la mezzanotte: il turno di notte finisce il giorno dopo,
        // quindi alla funzione si chiede due giorni e si tengono le righe della giornata giusta.
        var to = from.AddDays(2);

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var presses = await context.Presses
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.PressId)
            .Select(p => p.PressId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var windows = new List<ShiftWindow>();

        foreach (var pressId in presses)
        {
            var rows = await context.PressShifts(pressId, from, to)
                .Where(s => s.ShiftDate == from.Date)
                .OrderBy(s => s.ExtendedDateStartTs)
                .Select(s => new { s.PressId, s.ShiftId, s.ExtendedDateStartTs, s.ExtendedDateEndTs })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            windows.AddRange(rows.Select(r => new ShiftWindow(
                r.PressId.TrimEnd(),
                r.ShiftId.TrimEnd(),
                r.ExtendedDateStartTs,

                // La fine estesa arriva al minuto prima dell'inizio del turno successivo
                // (13:59 contro 14:00): il minuto va restituito, altrimenti un fermo che
                // comincia in quei sessanta secondi non apparterrebbe a nessun turno.
                r.ExtendedDateEndTs.AddMinutes(1))));
        }

        return [.. windows.OrderBy(w => w.From).ThenBy(w => w.PressId, StringComparer.Ordinal)];
    }
}
