namespace MesDataManager.Application.Production;

/// <summary>
/// Finestra di un turno su una pressa. <see cref="To"/> e' escluso: i turni si susseguono senza
/// fessure, quindi l'istante di confine appartiene al turno che comincia.
/// </summary>
public sealed record ShiftWindow(string PressId, string ShiftId, DateTime From, DateTime To);

/// <summary>
/// I turni dell'impianto. Sta dietro un'interfaccia perche' arrivano da una funzione di SQL
/// Server: cosi' l'aggregazione degli indicatori resta verificabile senza database reale.
/// </summary>
public interface IShiftCalendar
{
    /// <summary>
    /// Turni delle presse attive per la giornata di produzione indicata, in ordine di inizio.
    /// La giornata e' quella del turno, non quella del calendario: il turno di notte che scavalca
    /// la mezzanotte appartiene al giorno in cui e' cominciato.
    /// </summary>
    Task<IReadOnlyList<ShiftWindow>> GetShiftsAsync(DateOnly day, CancellationToken cancellationToken = default);
}

/// <summary>Lotti aperti e da riconciliare di una pressa: la sezione "Attivita' in corso".</summary>
public sealed record PressActivity(string PressId, int OpenBatches, int ToReconcile);

/// <summary>
/// Fermi di un turno su una pressa: quanti sono e quanto sono durati in tutto. Una riga per
/// pressa e turno, per ciascuno dei due tipi di fermo.
/// </summary>
public sealed record ShiftDowntimeTotals(string PressId, string ShiftId, int Count, TimeSpan Total);

/// <summary>
/// Contenuto del pannello di apertura. Le due sezioni rispondono a due domande diverse: cosa sta
/// succedendo adesso, e come e' andata la giornata.
/// </summary>
public sealed record HomeIndicators(
    DateOnly Day,
    IReadOnlyList<PressActivity> Activity,
    IReadOnlyList<ShiftDowntimeTotals> Macro,
    IReadOnlyList<ShiftDowntimeTotals> Micro)
{
    public static HomeIndicators Empty(DateOnly day) => new(day, [], [], []);

    /// <summary>
    /// Presse presenti negli indicatori dei fermi, nell'ordine in cui compaiono: sono le schede
    /// da disegnare. Ce n'e' una per ogni pressa che ha turni nella giornata, anche se non ha
    /// avuto fermi.
    /// </summary>
    public IReadOnlyList<string> DowntimePresses =>
        [.. Macro.Concat(Micro).Select(r => r.PressId).Distinct().Order(StringComparer.Ordinal)];

    /// <summary>Turni di una pressa, in ordine di inizio: sono le righe dentro la scheda.</summary>
    public static IEnumerable<ShiftDowntimeTotals> ForPress(
        IReadOnlyList<ShiftDowntimeTotals> rows,
        string pressId) =>
        rows.Where(r => r.PressId == pressId);
}

/// <summary>Indicatori del pannello di apertura.</summary>
public interface IHomeIndicatorService
{
    /// <summary>
    /// Calcola gli indicatori per la giornata di produzione indicata. I lotti aperti e quelli da
    /// riconciliare sono uno stato del momento e non dipendono dalla giornata; i fermi si.
    /// </summary>
    Task<HomeIndicators> GetAsync(DateOnly day, CancellationToken cancellationToken = default);
}
