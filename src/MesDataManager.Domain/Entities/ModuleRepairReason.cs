using MesDataManager.Domain.Abstractions;

namespace MesDataManager.Domain.Entities;

/// <summary>Causali di riparazione cesta (MasterData.ModuleRepairReason).</summary>
public sealed class ModuleRepairReason : IActivatable, IPositionable, IMasterControlled
{
    public short ModuleRepairReasonId { get; set; }
    public short Position { get; set; }
    public string Description { get; set; } = null!;
    public bool IsActive { get; set; }
    public bool IsActiveMaster { get; set; }
}
