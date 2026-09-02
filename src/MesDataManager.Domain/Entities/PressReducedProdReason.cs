using MesDataManager.Domain.Abstractions;

namespace MesDataManager.Domain.Entities;

/// <summary>Causali di produzione ridotta (MasterData.PressReducedProdReason).</summary>
public sealed class PressReducedProdReason : IActivatable, IPositionable, IMasterControlled
{
    public short PressReducedProdReasonId { get; set; }
    public short Position { get; set; }
    public string Description { get; set; } = null!;
    public bool IsActive { get; set; }
    public bool IsActiveMaster { get; set; }
}
