namespace MesDataManager.Domain.Entities;

/// <summary>Forni di invecchiamento (MasterData.Oven).</summary>
public sealed class Oven
{
    public string OvenId { get; set; } = null!;
    public string CompanyId { get; set; } = null!;
    public string AreaId { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string OvenOprId { get; set; } = null!;
    public short ModuleCapacity { get; set; }
    public string OvenWrkCtrId { get; set; } = null!;
}
