namespace MesDataManager.Domain.Entities;

/// <summary>
/// Ordine di produzione associato alle billette di un lotto
/// (<c>Press.BatchBilletProdOrders</c>).
/// <para>
/// E' il <b>primo</b> livello con cui si compone l'elenco degli ordini di un lotto: se qui non
/// c'e' nulla si ripiega su <see cref="BatchProdOrder"/>.
/// </para>
/// </summary>
public sealed class BatchBilletProdOrder
{
    public int BatchBilletProdOrdersId { get; set; }
    public string BatchId { get; set; } = null!;
    public string ProdId { get; set; } = null!;
    public string? EditStatusId { get; set; }
}
