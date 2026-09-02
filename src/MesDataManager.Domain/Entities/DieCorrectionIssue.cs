namespace MesDataManager.Domain.Entities;

/// <summary>Problemi matrice (MasterData.DieCorrectionIssue).</summary>
public sealed class DieCorrectionIssue
{
    public int DieCorrectionIssueId { get; set; }
    public string Name { get; set; } = null!;
}
