using MesDataManager.Domain.Abstractions;

namespace MesDataManager.Domain.Entities;

/// <summary>Causali di chiusura lotto (MasterData.PressBatchClosingReason).</summary>
public sealed class PressBatchClosingReason : IActivatable, IPositionable, IMasterControlled
{
    public short PressBatchClosingReasonId { get; set; }
    public short Position { get; set; }
    public string Description { get; set; } = null!;
    public string Result { get; set; } = null!;
    public bool IsActive { get; set; }
    public bool IsActiveMaster { get; set; }
}
