using MesDataManager.Domain.Abstractions;

namespace MesDataManager.Domain.Entities;

/// <summary>Societa'/stabilimenti. Usata solo come lookup, non gestita a UI (MasterData.Company).</summary>
public sealed class Company : IActivatable
{
    public string CompanyId { get; set; } = null!;
    public string Description { get; set; } = null!;
    public bool IsActive { get; set; }
}
