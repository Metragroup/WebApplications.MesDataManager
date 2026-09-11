namespace MesDataManager.Domain.Entities;

/// <summary>
/// Scarto registrato su una transazione di incestamento (vista
/// <c>EF.Module_ModuleTransScrap</c>). Della scheda del lotto interessa solo la <b>somma</b>
/// delle quantita' per transazione, non il dettaglio delle causali.
/// </summary>
public sealed class ModuleTransScrap
{
    public int ModuleTransScrapId { get; set; }
    public long ModuleTransId { get; set; }
    public int Qty { get; set; }
}
