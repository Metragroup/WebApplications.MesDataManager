namespace MesDataManager.Web.Components.Production;

/// <summary>
/// I tre valori di una rettifica delle barre, come li restituisce
/// <c>BatchAdjustmentDialog</c>. L'istante di creazione non e' fra questi: lo assegna il modello.
/// </summary>
public sealed record BatchAdjustmentValues(decimal BarLength, string? ProdId, int Qty);
