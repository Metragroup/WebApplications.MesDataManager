using MesDataManager.Domain.Abstractions;

namespace MesDataManager.Domain.Entities;

/// <summary>Presse di estrusione (MasterData.Press).</summary>
public sealed class Press : IActivatable
{
    public string PressId { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string CompanyId { get; set; } = null!;
    public decimal BilletDiameter { get; set; }
    public decimal BilletMeterWeight { get; set; }
    public int BilletDeadTime { get; set; }
    public decimal MinHourKg { get; set; }
    public string? Note { get; set; }
    public decimal BackMillimeterWeight { get; set; }
    public bool PressMonitorIsActive { get; set; }
    public decimal? PressMonitorMin { get; set; }
    public decimal? PressMonitorMax { get; set; }
    public decimal? PressMonitorStep { get; set; }
    public decimal? PressMonitorThreshold1 { get; set; }
    public decimal? PressMonitorThreshold2 { get; set; }
    public bool IsActive { get; set; }
    public string? SawPlcIp { get; set; }
    public int? SawPlcPort { get; set; }
    public decimal LogWeightTolerancePerc { get; set; }
    public string SawOprId { get; set; } = null!;
    public string SawWrkCtrId { get; set; } = null!;
    public bool HasMes { get; set; }
}
