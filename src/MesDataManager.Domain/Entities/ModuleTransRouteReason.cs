using MesDataManager.Domain.Abstractions;

namespace MesDataManager.Domain.Entities;

/// <summary>Causali di ciclo cesta (MasterData.ModuleTransRouteReason).</summary>
public sealed class ModuleTransRouteReason : IActivatable, IPositionable, IMasterControlled
{
    public int ModuleTransRouteReasonId { get; set; }
    public short Position { get; set; }
    public string Description { get; set; } = null!;
    public string OprId { get; set; } = null!;
    public string Code { get; set; } = null!;
    public bool IsActive { get; set; }
    public bool IsActiveMaster { get; set; }
}
