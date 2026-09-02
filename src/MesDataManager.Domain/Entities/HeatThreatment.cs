namespace MesDataManager.Domain.Entities;

/// <summary>Trattamenti termici (MasterData.HeatThreatment).</summary>
public sealed class HeatThreatment
{
    public string HeatThreatmentId { get; set; } = null!;
    public string Description { get; set; } = null!;
    public decimal DurationMinutes { get; set; }
}
