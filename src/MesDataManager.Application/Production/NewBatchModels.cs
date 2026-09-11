namespace MesDataManager.Application.Production;

/// <summary>
/// Dati per la creazione di un lotto a mano.
/// <para>
/// Un lotto inserito a mano nasce <b>chiuso</b>, a pressa e a sega, e con
/// <c>EditStatusID = 'N'</c>: non e' un lotto in corso da seguire, e' la ricostruzione di una
/// produzione che la raccolta dati non ha registrato. Cosi' faceva <c>FrmNewBatch</c>.
/// </para>
/// </summary>
public sealed record NewBatchRequest(
    string PressId,
    string DieCode,
    short? DieNumber,
    DateTime StartTs,
    DateTime StopTs,
    short ClosingReasonId,
    NewBilletsRequest Billets)
{
    /// <summary>
    /// Numero del lotto: sigla della pressa e istante di inizio a dodici cifre
    /// (<c>MP1260901080000</c>), come <c>BatchesPresenter.GenerateBatchNumber</c>.
    /// <para>
    /// Due lotti creati nello stesso secondo sulla stessa pressa avrebbero la stessa chiave: il
    /// salvataggio lo segnala come chiave duplicata, e non e' un caso da prevenire — e' un caso
    /// da riconoscere.
    /// </para>
    /// </summary>
    public string BatchId => $"{PressId.Trim()}{StartTs:yyMMddHHmmss}";
}

/// <summary>
/// Esito della marcatura "da riconciliare" su piu' lotti.
/// <para>
/// Non e' un conteggio: e' un resoconto. Chi seleziona quaranta lotti e ne marca trentasette
/// deve sapere quali tre sono rimasti indietro e perche', altrimenti li considera fatti.
/// </para>
/// </summary>
public sealed record BatchMarkResult(int Marked, IReadOnlyList<BatchMarkSkip> Skipped);

/// <summary>Un lotto che la marcatura ha saltato, col motivo.</summary>
public sealed record BatchMarkSkip(string BatchId, BatchMarkSkipReason Reason);

/// <summary>
/// Perche' un lotto non e' stato segnato da riconciliare.
/// <para>
/// Il vecchio applicativo, prima di marcare, <b>rieseguiva la diagnostica</b> su ogni riga
/// selezionata: su una selezione ampia significava altrettante chiamate al servizio. Deciso
/// l'8 settembre 2026 di non rilanciarla e di leggere l'esito che i lotti hanno gia' — saltando
/// quelli che non ce l'hanno, invece di calcolarlo per loro.
/// </para>
/// </summary>
public enum BatchMarkSkipReason
{
    /// <summary>Diagnostica mai eseguita: manca il presupposto per dire che il lotto e' a posto.</summary>
    NoDiagnostics,

    /// <summary>Diagnostica in errore: il lotto ha problemi noti e non va all'ERP.</summary>
    DiagnosticsError,

    /// <summary>Gia' riconciliato: e' lo stato terminale, non c'e' niente da segnare.</summary>
    AlreadyImported,

    /// <summary>In modifica da qualcuno: si tocca solo dopo che ha finito.</summary>
    Locked,

    /// <summary>Il lotto non esiste piu': la selezione veniva da un elenco non piu' attuale.</summary>
    NotFound,
}
