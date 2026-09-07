namespace MesDataManager.Domain.Entities;

/// <summary>
/// Lotto di estrusione (Press.Batch). Dato transazionale, non un'anagrafica: nessuna delle
/// interfacce di <c>Domain.Abstractions</c> si applica.
/// <para>
/// La chiave e' assegnata dall'applicazione, non generata dal database: e' <c>PressID</c> seguito
/// da <c>yyMMddHHmmss</c>. Non ricavare da essa la data di inizio — sui dati reali i due valori
/// possono differire di qualche secondo.
/// </para>
/// <para>
/// Sono mappate le colonne non nullable e quelle effettivamente mostrate. La tabella ne ha una
/// sessantina: le altre entreranno quando serviranno, cosi' le letture non trascinano colonne
/// che nessuno guarda.
/// </para>
/// </summary>
public sealed class Batch
{
    public string BatchId { get; set; } = null!;
    public int BatchStatusId { get; set; }
    public string PressId { get; set; } = null!;
    public string? DieId { get; set; }
    public string? DieCode { get; set; }
    public short? DieNumber { get; set; }
    public DateTime? StartTs { get; set; }
    public DateTime? StopTs { get; set; }
    public short? BilletCount { get; set; }
    public int? BarCount { get; set; }

    public bool IsProdOrderProcessed { get; set; }
    public bool IsPressClosed { get; set; }
    public DateTime? PressClosedTs { get; set; }
    public bool IsSawClosed { get; set; }
    public DateTime? SawClosedTs { get; set; }

    /// <summary>Lotto di campionatura invece di produzione.</summary>
    public bool IsSampling { get; set; }

    /// <summary>
    /// Elaborazione del lotto conclusa. Lo scrivono le procedure di elaborazione, non questa
    /// applicazione: finche' e' falso il lotto e' in lavorazione e nel vecchio applicativo non
    /// era modificabile.
    /// </summary>
    public bool IsBatchProcessed { get; set; }

    /// <summary>Segnato "da riconciliare": e' la coda di lavoro che l'ERP legge.</summary>
    public bool IsErpMarked { get; set; }

    /// <summary>Riconciliato dall'ERP: e' lo stato terminale, e congela il lotto.</summary>
    public bool IsErpImported { get; set; }

    public bool IsDwhImported { get; set; }

    /// <summary>
    /// Lock pessimistico persistito su riga, con utente e istante. Nel vecchio applicativo non
    /// aveva scadenza: un blocco orfano restava tale a tempo indeterminato.
    /// </summary>
    public bool IsLock { get; set; }

    public DateTime? LockTs { get; set; }
    public string? LockUsr { get; set; }

    public short? PressBatchClosingReasonId { get; set; }

    public decimal? KgRaw { get; set; }
    public decimal? KgSheared { get; set; }
    public decimal? KgExtruded { get; set; }
    public decimal? KgCut { get; set; }

    public decimal? ItemMeterWeightMasterData { get; set; }
    public decimal? ItemMeterWeightMes { get; set; }
    public decimal? ItemMeterWeightTest { get; set; }
    public decimal? ItemMeterWeight { get; set; }

    /// <summary>Esito della diagnostica: <c>OK</c>, <c>ATT</c> (avvisi) o <c>ERR</c>.</summary>
    public string? DiagnosticsStatus { get; set; }

    public DateTime? DiagnosticsTs { get; set; }
    public string? DiagnosticsMsg { get; set; }

    /// <summary><c>N</c> se il lotto e' stato inserito a mano, altrimenti viene dalla produzione.</summary>
    public string? EditStatusId { get; set; }
}
