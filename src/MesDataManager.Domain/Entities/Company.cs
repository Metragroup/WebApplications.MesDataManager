using MesDataManager.Domain.Abstractions;

namespace MesDataManager.Domain.Entities;

/// <summary>Societa'/stabilimenti. Usata solo come lookup, non gestita a UI (MasterData.Company).</summary>
public sealed class Company : IActivatable
{
    public string CompanyId { get; set; } = null!;
    public string Description { get; set; } = null!;
    public bool IsActive { get; set; }

    /// <summary>
    /// Se la sega ammette rettifiche delle quantita' di barre. Governa la presenza del pannello
    /// Rettifiche nella scheda del lotto, come nel vecchio applicativo (che leggeva la prima
    /// societa' attiva). Nullable a database: assente vale "non ammesse".
    /// </summary>
    public bool? SawAllowAdjustments { get; set; }
}
