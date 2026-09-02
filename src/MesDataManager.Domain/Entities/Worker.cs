using MesDataManager.Domain.Abstractions;

namespace MesDataManager.Domain.Entities;

/// <summary>Operatori (MasterData.Worker).</summary>
public sealed class Worker : IActivatable
{
    public short WorkerId { get; set; }
    public string EmplId { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string CompanyId { get; set; } = null!;
    public bool IsLineSupervisor { get; set; }
    public bool IsPressSupervisor { get; set; }
    public bool IsPressOperator { get; set; }
    public bool IsSawOperator { get; set; }
    public bool IsOvenOperator { get; set; }
    public bool IsRollingOperator { get; set; }
    public bool IsPaintOperator { get; set; }
    public bool IsPackingOperator { get; set; }
    public bool IsCorrectionOperator { get; set; }
    public bool IsMachiningOperator { get; set; }
    public bool IsActive { get; set; }
}
