namespace MesDataManager.Domain.Entities;

/// <summary>Tipi di fermo macchina (MasterData.PressFailureType).</summary>
public sealed class PressFailureType
{
    public short PressFailureTypeId { get; set; }
    public string Description { get; set; } = null!;
}
