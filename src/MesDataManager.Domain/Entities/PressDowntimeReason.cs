using MesDataManager.Domain.Abstractions;

namespace MesDataManager.Domain.Entities;

/// <summary>Causali di fermo macchina (MasterData.PressDowntimeReason).</summary>
public sealed class PressDowntimeReason : IActivatable, IPositionable, IMasterControlled
{
    public short PressDowntimeReasonId { get; set; }
    public short Position { get; set; }
    public string Description { get; set; } = null!;
    public bool IsElt { get; set; }
    public bool IsMec { get; set; }
    public bool IsProd { get; set; }
    public int OeeClass { get; set; }
    public int AvailabilityClass { get; set; }
    public bool IsActive { get; set; }
    public bool IsPressUnavailable { get; set; }
    public bool IsActiveMaster { get; set; }
}
