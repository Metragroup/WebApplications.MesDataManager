namespace MesDataManager.Domain.Entities;

/// <summary>
/// Tipi di fermo macchina — Macrofermo/Microfermo (MasterData.PressDowntimeType). Due righe
/// fisse, senza posizione ne' attivazione: non e' un'anagrafica gestibile, e' un lookup di sola
/// lettura per il filtro Tipo della pagina Fermi.
/// </summary>
public sealed class PressDowntimeType
{
    public short PressDowntimeTypeId { get; set; }
    public string Description { get; set; } = null!;
}
