namespace MesDataManager.Domain.Entities;

/// <summary>
/// Ordine di produzione dell'ERP (vista <c>EF.NPOPRODUCTIONTAG</c>), quello che il vecchio
/// applicativo chiamava "cartellino".
/// <para>
/// Sono mappate le sole colonne che la scheda del lotto mostra, piu' la lega di produzione: la
/// vista ne ha una cinquantina.
/// </para>
/// <para>
/// Le due leghe convivono di proposito e possono differire: <see cref="SalesAlloyId"/> e' quella
/// dell'ordine di vendita, <see cref="ProdAlloyId"/> quella dell'ordine di produzione — ed e'
/// quest'ultima che il vecchio applicativo confrontava con la lega delle billette.
/// </para>
/// </summary>
public sealed class ProductionTag
{
    public string ProdId { get; set; } = null!;
    public string? CustName { get; set; }
    public string? SalesAlloyId { get; set; }
    public string? ProdAlloyId { get; set; }
    public string? SalesHeatTreatment { get; set; }
}
