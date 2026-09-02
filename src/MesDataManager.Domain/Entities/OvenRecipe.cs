namespace MesDataManager.Domain.Entities;

/// <summary>Ricette del forno di invecchiamento (MasterData.OvenRecipe).</summary>
public sealed class OvenRecipe
{
    public int OvenRecipeId { get; set; }
    public string OvenId { get; set; } = null!;
    public int RecipeId { get; set; }
    public decimal? Temperature { get; set; }
    public int? MinRamp { get; set; }
    public int? MinCycle { get; set; }
    public int? MinCycleMax { get; set; }
    public int? MinCooling { get; set; }
    public string Description { get; set; } = null!;
}
