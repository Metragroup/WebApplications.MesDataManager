namespace MesDataManager.Domain.Entities;

/// <summary>
/// Riga del lotto scomposto per lunghezza barra e turno, restituita dalla funzione tabellare
/// <c>EF.ufn_BatchByLengthShift(@startTs, @stopTs)</c>.
/// <para>
/// Non e' una tabella e non e' scrivibile: e' la vista che alimenta la modalita' "dettaglio
/// lunghezza" dell'elenco dati di produzione. Un lotto compare qui una volta per ogni
/// combinazione di lunghezza barra e turno attraversati — nella settimana dell'8 aprile 2024,
/// 264 lotti chiusi diventano 366 righe.
/// </para>
/// </summary>
public sealed class BatchByLengthShift
{
    public string BatchId { get; set; } = null!;
    public string PressId { get; set; } = null!;
    public string? DieId { get; set; }

    public decimal? BarLength { get; set; }
    public string? AlloyAndTreatment { get; set; }
    public string? ShiftId { get; set; }
    public DateTime? ShiftDate { get; set; }

    public int? BilletCount { get; set; }
    public int? BarCount { get; set; }
    public short? PressBatchClosingReasonId { get; set; }

    public DateTime? StartTs { get; set; }
    public DateTime? StopTs { get; set; }

    public decimal? KgExtruded { get; set; }
    public decimal? KgCut { get; set; }

    public bool IsErpMarked { get; set; }
    public bool IsErpImported { get; set; }
    public bool IsSampling { get; set; }
    public bool IsLock { get; set; }

    public string? DiagnosticsStatus { get; set; }
}
