using MesDataManager.Domain.Abstractions;

namespace MesDataManager.Domain.Entities;

/// <summary>Causali di scarto cesta (MasterData.ModuleScrapReason).</summary>
public sealed class ModuleScrapReason : IActivatable, IPositionable, IMasterControlled
{
    public int ModuleScrapReasonId { get; set; }
    public short Position { get; set; }
    public string Description { get; set; } = null!;
    public string OprId { get; set; } = null!;
    public string Code { get; set; } = null!;
    public bool IsActive { get; set; }
    public bool IsActiveMaster { get; set; }
}
