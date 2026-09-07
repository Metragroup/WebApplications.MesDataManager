namespace MesDataManager.Application.Production;

/// <summary>
/// Stato di chiusura con cui filtrare i lotti aperti. Sono le tre varianti del vecchio
/// applicativo: la chiusura avviene in due momenti distinti, prima la pressa e poi la sega.
/// </summary>
public enum BatchCloseState
{
    /// <summary>Tutti i lotti con almeno una delle due chiusure mancante.</summary>
    Any,

    /// <summary>In corso: ne' pressa ne' sega hanno chiuso.</summary>
    Running,

    /// <summary>Parzialmente chiusi: una sola delle due chiusure e' avvenuta.</summary>
    PartiallyClosed,
}

/// <summary>Riga dell'elenco dei lotti.</summary>
public sealed record BatchRow(
    string BatchId,
    string PressId,
    string? DieId,
    short? BilletCount,
    int? BarCount,
    DateTime? StartTs,
    DateTime? StopTs,
    bool IsPressClosed,
    DateTime? PressClosedTs,
    bool IsSawClosed,
    DateTime? SawClosedTs,
    string? DiagnosticsStatus,
    bool IsLock);

/// <summary>
/// Criteri di lettura dei lotti aperti. Non c'e' filtro di periodo, come nel vecchio applicativo:
/// i lotti aperti sono pochi e uno aperto da mesi e' proprio l'anomalia da vedere.
/// </summary>
public sealed record BatchListQuery(
    string? PressId,
    BatchCloseState CloseState = BatchCloseState.Any,
    int PageNumber = 1,
    int PageSize = 25);

/// <summary>Una pagina di lotti, con il totale delle righe che soddisfano il filtro.</summary>
public sealed record BatchPage(IReadOnlyList<BatchRow> Rows, int TotalCount);

/// <summary>Testata del lotto, come la mostra la scheda di dettaglio.</summary>
public sealed record BatchDetail(
    string BatchId,
    string PressId,
    string? DieId,
    short? BilletCount,
    int RealBilletCount,
    int? BarCount,
    string? ClosingReasonDescription,
    bool IsSampling,
    DateTime? StartTs,
    DateTime? StopTs,
    bool IsPressClosed,
    DateTime? PressClosedTs,
    bool IsSawClosed,
    DateTime? SawClosedTs,
    bool IsBatchProcessed,
    bool IsErpMarked,
    bool IsErpImported,
    bool IsLock,
    string? LockUsr,
    decimal? KgRaw,
    decimal? KgSheared,
    decimal? KgExtruded,
    decimal? KgCut,
    decimal? ItemMeterWeightMasterData,
    decimal? ItemMeterWeightMes,
    decimal? ItemMeterWeightTest,
    decimal? ItemMeterWeight,
    string? DiagnosticsStatus,
    DateTime? DiagnosticsTs,
    string? DiagnosticsMsg,
    IReadOnlyList<BatchBilletRow> Billets);

/// <summary>Riga della griglia billette. Solo billette vere: i marcatori non compaiono.</summary>
public sealed record BatchBilletRow(
    int Id,
    short BilletNo,
    DateTime? StartTs,
    DateTime? StopTs,
    string? ShiftId,
    decimal? MmBarSet,
    int? MmBilletAct,
    decimal? KgExtruded,
    string? Billet1CastingId,
    string? Billet1AlloyId,
    decimal? Billet1Kg,
    string? Billet2CastingId,
    string? Billet2AlloyId,
    decimal? Billet2Kg,
    string? ProdId,
    string? EditStatusId);
