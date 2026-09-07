namespace MesDataManager.Domain.Entities;

/// <summary>
/// Turno di una pressa, dalla funzione tabellare <c>Press.ufn_GetShifts</c>.
/// <para>
/// La funzione e' la definizione di turno dell'impianto: quando il calendario
/// (<c>MasterData.DateShift</c>) non e' compilato ripiega su tre turni di otto ore, e lo dichiara
/// con <see cref="IsFromCalendar"/>.
/// </para>
/// <para>
/// Le due finestre non sono equivalenti. Quelle di calendario sono le nominali e possono lasciare
/// scoperti gli intervalli fra un turno e il successivo; quelle estese sono allungate fino a
/// combaciare, quindi ogni istante della giornata appartiene a un turno e uno solo. Per attribuire
/// un fermo a un turno servono queste ultime, altrimenti i fermi che cadono nelle fessure non
/// verrebbero contati da nessuna parte.
/// </para>
/// </summary>
public sealed class PressShift
{
    public string PressId { get; set; } = null!;
    public DateTime ShiftDate { get; set; }
    public string ShiftId { get; set; } = null!;

    public DateTime ExtendedDateStartTs { get; set; }
    public DateTime ExtendedDateEndTs { get; set; }

    public DateTime CalendarDateStartTs { get; set; }
    public DateTime CalendarDateEndTs { get; set; }

    /// <summary>Vero se il turno viene dal calendario, falso se e' il ripiego standard.</summary>
    public bool IsFromCalendar { get; set; }
}
