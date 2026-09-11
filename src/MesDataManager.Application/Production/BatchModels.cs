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

/// <summary>
/// Il lotto come lo mostra la scheda di dettaglio: testata piu' le cinque raccolte che
/// compongono le altre schede.
/// <para>
/// E' una fotografia di sola lettura. La modifica non passa da qui ma da
/// <c>BatchEditModel</c>: questo record descrive cio' che si vede, non cio' che si scrive.
/// </para>
/// </summary>
public sealed record BatchDetail(
    string BatchId,
    string PressId,
    string? DieId,
    string? DieCode,
    short? DieNumber,
    short? PressBatchClosingReasonId,
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
    DateTime? LockTs,
    string? EditStatusId,
    decimal? KgRaw,
    decimal? KgSheared,
    decimal? KgExtruded,
    decimal? KgCut,
    decimal? ItemMeterWeightMasterData,
    decimal? ItemMeterWeightMes,
    decimal? ItemMeterWeightTest,
    decimal? ItemMeterWeight,
    string? LegacyDiagStatus,
    DateTime? LegacyDiagTs,
    string? LegacyDiagMsg,
    string? SvcDiagStatus,
    DateTime? SvcDiagTs,
    string? SvcDiagMsg,
    string? SvcDiagJson,
    string? UsrDiagStatus,
    DateTime? UsrDiagTs,
    string? UsrDiagMsg,
    string? UsrDiagJson,
    IReadOnlyList<BatchBilletRow> Billets,
    IReadOnlyList<BatchAdjustmentRow> Adjustments,
    IReadOnlyList<ModuleTransRow> ModuleTransactions,
    IReadOnlyList<BatchProdOrderRow> ProductionOrders,
    bool AllowAdjustments)
{
    /// <summary>
    /// Il lotto viene dalla produzione e non e' stato inserito a mano. Serve alla seconda
    /// conferma dell'eliminazione, che il vecchio applicativo chiedeva proprio in questo caso
    /// (<c>BatchesPresenter.IsOriginalBatch</c>).
    /// </summary>
    public bool IsFromProduction => EditStatusId?.Trim() != "N";

    /// <summary>
    /// L'esito che vale adesso, con la stessa precedenza delle funzioni
    /// <c>EF.ufn_BatchByLength(Shift)</c>: prima il vecchio applicativo finche' le sue colonne
    /// esistono, poi l'utente, poi il servizio.
    /// <para>
    /// I tre gruppi di campi non si sovrascrivono a vicenda — ognuno ha il suo produttore —
    /// quindi "lo stato della diagnostica" e' una lettura, non un campo.
    /// </para>
    /// </summary>
    public string? DiagnosticsStatus => LegacyDiagStatus ?? UsrDiagStatus ?? SvcDiagStatus;

    /// <summary>Vero se almeno un produttore ha lasciato un esito.</summary>
    public bool HasDiagnostics =>
        !string.IsNullOrWhiteSpace(LegacyDiagStatus) ||
        !string.IsNullOrWhiteSpace(SvcDiagStatus) ||
        !string.IsNullOrWhiteSpace(UsrDiagStatus);
}

/// <summary>
/// Riga delle rettifiche delle barre (<c>Press.BatchBarQty</c>). <see cref="Qty"/> puo' essere
/// negativa: e' una correzione, non una misura.
/// </summary>
public sealed record BatchAdjustmentRow(
    int Id,
    decimal BarLength,
    string? ProdId,
    int Qty,
    DateTime CreatedTs);

/// <summary>
/// Riga delle transazioni di incestamento. I quattro campi da <see cref="OprNum"/> in giu' non
/// stanno su <c>Module_ModuleTrans</c>: arrivano dall'ultimo passo lavorato del ciclo e dalla
/// somma degli scarti.
/// </summary>
public sealed record ModuleTransRow(
    long Id,
    string ModuleId,
    decimal? BarLength,
    string? ProdId,
    int Qty,
    DateTime CreatedTs,
    int? OprNum,
    string? OprId,
    string? WrkCtrId,
    int ScrapQty);

/// <summary>
/// Riga dell'elenco ordini di produzione del lotto. Le due leghe convivono e possono differire:
/// quella dell'ordine di vendita e quella dell'ordine di produzione.
/// </summary>
public sealed record BatchProdOrderRow(
    string ProdId,
    string? CustomerName,
    string? SalesAlloyId,
    string? ProdAlloyId,
    string? HeatTreatment);

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

/// <summary>
/// Esito del salvataggio di un lotto.
/// <para>
/// <see cref="Recalculated"/> distingue due situazioni che non vanno confuse: le modifiche sono
/// salvate in entrambi i casi, ma se il ricalcolo del MES non e' riuscito i valori di riepilogo
/// — pesi, conteggi, tempi di ciclo — sono ancora quelli di prima. Va detto a chi ha salvato:
/// vedrebbe numeri che non corrispondono a cio' che ha appena scritto.
/// </para>
/// </summary>
public sealed record BatchSaveResult(BatchDetail Detail, bool Recalculated);
