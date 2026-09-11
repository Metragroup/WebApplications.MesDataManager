namespace MesDataManager.Domain.Entities;

/// <summary>
/// Ordine di produzione rilasciato sul lotto all'avvio dell'estrusione
/// (<c>Press.BatchProdOrders</c>).
/// <para>
/// E' il <b>secondo</b> livello con cui si compone l'elenco degli ordini di un lotto: si usa solo
/// se non ci sono ordini sulle billette (<see cref="BatchBilletProdOrder"/>).
/// </para>
/// </summary>
public sealed class BatchProdOrder
{
    public int BatchProdOrdersId { get; set; }
    public string BatchId { get; set; } = null!;
    public string ProdId { get; set; } = null!;
}
