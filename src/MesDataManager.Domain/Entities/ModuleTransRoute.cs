namespace MesDataManager.Domain.Entities;

/// <summary>
/// Passo del ciclo di lavorazione di una cesta (vista <c>EF.Module_ModuleTransRoute</c>).
/// <para>
/// Di una transazione interessa <b>l'ultimo passo lavorato</b>: si filtra
/// <see cref="IsProcessed"/> e si ordina per <see cref="OprNumPriority"/> prendendo l'ultimo,
/// come faceva <c>RepositoryService.GetModuleTransDtos</c>. E' da qui che la scheda del lotto
/// ricava numero operazione, operazione e centro di lavoro.
/// </para>
/// </summary>
public sealed class ModuleTransRoute
{
    public int ModuleTransRouteId { get; set; }
    public long ModuleTransId { get; set; }

    public int OprNum { get; set; }
    public string OprId { get; set; } = null!;
    public string WrkCtrId { get; set; } = null!;

    public bool IsProcessed { get; set; }

    /// <summary>Ordine dei passi. Nullable a database, quindi l'ordinamento deve tollerarne l'assenza.</summary>
    public int? OprNumPriority { get; set; }
}
