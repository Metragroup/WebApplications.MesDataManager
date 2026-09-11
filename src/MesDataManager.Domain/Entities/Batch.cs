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

    /// <summary>Stato della matrice fotografato sul lotto. Lo scrive la raccolta dati, non questa applicazione.</summary>
    public string? DieStatusId { get; set; }
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

    /// <summary>
    /// Esito della diagnostica <b>del servizio</b>: <c>OK</c>, <c>ATT</c> (avvisi) o <c>ERR</c>.
    /// <para>
    /// Lo scrive il servizio di diagnostica quando elabora il lotto per conto suo. Questa
    /// applicazione non lo tocca <b>mai</b>, nemmeno quando e' vuoto: un lotto senza esito del
    /// servizio e' un buco nel calcolo automatico, e riempirlo con l'esito di una diagnostica
    /// chiesta a mano cancellerebbe proprio l'informazione che serve a trovarlo.
    /// </para>
    /// </summary>
    public string? SvcDiagStatus { get; set; }

    public DateTime? SvcDiagTs { get; set; }

    /// <summary>Referto leggibile del servizio, dal campo <c>diagnostics.message</c> della risposta.</summary>
    public string? SvcDiagMsg { get; set; }

    /// <summary>Risposta del servizio conservata intera e verbatim (<c>nvarchar(max)</c>).</summary>
    public string? SvcDiagJson { get; set; }

    /// <summary>
    /// Esito dell'ultima diagnostica <b>chiesta da un utente</b> da questa applicazione. E'
    /// l'unico gruppo di campi che l'applicazione scrive.
    /// </summary>
    public string? UsrDiagStatus { get; set; }

    public DateTime? UsrDiagTs { get; set; }

    /// <summary>
    /// Referto leggibile, come arriva dal servizio (<c>diagnostics.message</c>). Non si compone
    /// piu' a partire dal JSON: messaggio e risposta si conservano separati.
    /// </summary>
    public string? UsrDiagMsg { get; set; }

    /// <summary>Risposta del servizio conservata intera e verbatim (<c>nvarchar(max)</c>).</summary>
    public string? UsrDiagJson { get; set; }

    /// <summary>
    /// Esito della diagnostica del <b>vecchio applicativo</b> (colonna <c>DiagnosticsStatus</c>),
    /// che continua a girare e a scrivere qui.
    /// <para>
    /// Questa applicazione la legge e non la scrive mai: e' il dato di un altro produttore. Le
    /// tre colonne restano a database finche' il vecchio applicativo e' in servizio.
    /// </para>
    /// </summary>
    public string? LegacyDiagStatus { get; set; }

    public DateTime? LegacyDiagTs { get; set; }

    /// <summary>Referto del vecchio applicativo: testo, non JSON.</summary>
    public string? LegacyDiagMsg { get; set; }

    /// <summary>
    /// L'esito che vale adesso. Non e' una colonna: e' la lettura dei tre produttori, nello stesso
    /// ordine della funzione <c>EF.ufn_BatchByLengthShift</c> — <b>prima il vecchio</b>
    /// applicativo finche' esiste, poi l'utente, poi il servizio.
    /// <para>
    /// L'ordine e' una decisione di transizione, non una gerarchia di qualita': finche' il vecchio
    /// applicativo e' in servizio, e' lui a dire come sta il lotto. Ne discende che su un lotto
    /// gia' diagnosticato da quello, una riesecuzione fatta da qui non cambia il valore che gli
    /// elenchi mostrano — si vede nella scheda, che tiene i produttori distinti.
    /// </para>
    /// </summary>
    public string? CurrentDiagStatus => LegacyDiagStatus ?? UsrDiagStatus ?? SvcDiagStatus;

    /// <summary><c>N</c> se il lotto e' stato inserito a mano, altrimenti viene dalla produzione.</summary>
    public string? EditStatusId { get; set; }
}
