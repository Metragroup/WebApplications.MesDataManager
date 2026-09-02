using MesDataManager.Domain.Abstractions;

namespace MesDataManager.Domain.Entities;

/// <summary>Causali di fermo verniciatura (MasterData.PaintingDowntimeReason).</summary>
public sealed class PaintingDowntimeReason : IActivatable, IPositionable, IMasterControlled
{
    public short PaintingDowntimeReasonId { get; set; }
    public short Position { get; set; }
    public string Description { get; set; } = null!;
    public bool IsActive { get; set; }
    public bool IsActiveMaster { get; set; }
}
