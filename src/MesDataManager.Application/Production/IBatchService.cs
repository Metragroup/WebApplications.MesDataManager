namespace MesDataManager.Application.Production;

/// <summary>
/// Lettura e modifica dei lotti di estrusione.
/// <para>
/// La modifica e' governata da un blocco pessimistico su riga: si entra con
/// <see cref="BeginEditAsync"/>, si esce salvando o con <see cref="CancelEditAsync"/>. Le
/// modifiche in sospeso <b>non stanno qui</b>: vivono nel circuito di chi le sta facendo e
/// arrivano al servizio tutte insieme al salvataggio, in una sola transazione.
/// </para>
/// </summary>
public interface IBatchService
{
    Task<BatchPage> GetPageAsync(BatchListQuery query, CancellationToken cancellationToken = default);

    /// <summary>Testata e billette del lotto. Lancia <see cref="ProductionException"/> se non esiste.</summary>
    Task<BatchDetail> GetDetailAsync(string batchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Elenco dei lotti gia' chiusi a pressa e a sega, con i filtri della riconciliazione ERP.
    /// La modalita' "dettaglio lunghezza" cambia la sorgente, non solo le colonne: vedi
    /// <see cref="ProductionBatchQuery.LengthDetail"/>.
    /// </summary>
    Task<ProductionBatchPage> GetProductionPageAsync(
        ProductionBatchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prende il lotto in modifica e ne restituisce la fotografia aggiornata.
    /// <para>
    /// Le precondizioni sono quelle del vecchio applicativo: elaborazione conclusa
    /// (<c>IsBatchProcessed</c>) e lotto non ancora riconciliato (<c>IsErpImported</c>), piu' il
    /// permesso di scrittura sulla produzione. Un lotto gia' bloccato da altri viene rifiutato
    /// con <see cref="ProductionException.BatchLocked"/>, che riporta chi e da quando — a meno
    /// che il blocco non sia scaduto (<see cref="BatchLockPolicy"/>), nel qual caso si prende.
    /// </para>
    /// <para>
    /// Rientrare nel proprio blocco e' consentito e non e' un errore: nel web ricaricare la
    /// pagina e' un gesto normale, e trovare "lotto bloccato da te stesso" sarebbe un vicolo
    /// cieco.
    /// </para>
    /// </summary>
    Task<BatchDetail> BeginEditAsync(string batchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rilascia il blocco, se e' ancora dell'utente corrente. E' idempotente e non solleva:
    /// viene chiamata anche quando il circuito muore, dove non c'e' nessuno a cui riferire un
    /// errore, e su un blocco che nel frattempo e' passato a un altro non c'e' nulla da fare.
    /// </summary>
    Task CancelEditAsync(string batchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sblocca un lotto in modifica altrui. Solo per <c>Administrator</c>: fa perdere le
    /// modifiche non salvate dell'altro utente, che le scoprira' perse al primo salvataggio.
    /// </summary>
    Task ForceUnlockAsync(string batchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Controlla una matrice prima di assegnarla: esistenza fra i centri di lavoro dell'ERP e
    /// stato d'uso. Non scrive niente — l'assegnazione resta in sospeso nel modello di modifica
    /// fino al salvataggio.
    /// </summary>
    Task<DieValidation> ValidateDieAsync(
        string dieCode,
        short? dieNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cerca una colata dal codice che l'operatore digita sulla billetta, e ne restituisce la
    /// lega. Nullo se il codice non esiste.
    /// <para>
    /// La lega non si scrive a mano: deriva dalla colata, come nel vecchio applicativo
    /// (<c>GetAlloyCasting</c>).
    /// </para>
    /// </summary>
    Task<CastingLookup?> FindCastingAsync(
        string castingCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Se l'ordine di produzione esiste fra i cartellini dell'ERP. Mirror di
    /// <c>RepositoryService.CheckProdIDExist</c>.
    /// </summary>
    Task<bool> ProdOrderExistsAsync(string prodId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scrive tutte le modifiche in sospeso in <b>una</b> transazione, rilascia il blocco e
    /// restituisce il lotto riletto.
    /// <para>
    /// Dentro la transazione, nell'ordine: testata, billette (cancellate, modificate, aggiunte),
    /// riallineamento dei marcatori di apertura e chiusura, rettifiche, effetto della diagnostica
    /// sulle presse senza MES, rilascio del blocco.
    /// </para>
    /// <para>
    /// Il salvataggio <b>non</b> ricalcola il lotto: lo rimette in coda mettendo
    /// <c>IsBatchProcessed</c> a falso, e il ricalcolo lo fa il MES col suo lavoro pianificato,
    /// che ogni cinque minuti esegue <c>usp_Batch_Elab</c> su tutti i lotti in coda. Fino ad
    /// allora i valori di riepilogo — pesi, conteggi, tempi di ciclo — restano quelli di prima e
    /// il lotto <b>non e' modificabile</b> (<see cref="BeginEditAsync"/>): sono le due cose da
    /// dire a chi ha salvato. Il vecchio applicativo chiamava invece la procedura sul momento, e
    /// il salvataggio durava quaranta secondi.
    /// </para>
    /// <para>
    /// Dopo il commit resta una sola chiamata, <c>usp_LogScaleImportUpdateByBatchID</c>, che
    /// costa 109 millisecondi: riallinea i log di pesatura, e il lavoro pianificato non la fa.
    /// </para>
    /// <para>
    /// Il lotto si <b>rilegge</b> comunque alla fine: la fotografia restituita e' quella vera, e
    /// porta l'attesa di elaborazione appena messa a database.
    /// </para>
    /// <para>
    /// Se nel frattempo il blocco e' passato a un altro — sblocco forzato di un amministratore, o
    /// scadenza piu' presa da un collega — il salvataggio non avviene e viene sollevata
    /// <see cref="ProductionException.BatchLockLost"/>: le modifiche restano nel circuito e
    /// l'operatore sa di averle perse.
    /// </para>
    /// </summary>
    Task<BatchDetail> SaveAsync(BatchEditModel edit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Segna o annulla la marcatura "da riconciliare" sui lotti indicati, e riferisce quali ha
    /// saltato e perche' (<see cref="BatchMarkSkipReason"/>).
    /// <para>
    /// Segnare vuole una diagnostica non in errore, un lotto non ancora importato e non in
    /// modifica. Annullare non ha limitazioni: togliere un lotto dalla coda dell'ERP e' sempre
    /// lecito, come nel vecchio applicativo.
    /// </para>
    /// </summary>
    Task<BatchMarkResult> MarkForErpAsync(
        IReadOnlyCollection<string> batchIds,
        bool marked,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea un lotto a mano con le sue billette e restituisce il numero assegnato.
    /// <para>
    /// Controlla la matrice come il cambio matrice e verifica che il periodo non si sovrapponga
    /// a un altro lotto della stessa pressa. Il lotto nasce chiuso a pressa e a sega, con i
    /// marcatori di apertura e chiusura, e <b>da elaborare</b>: lo prendera' il lavoro
    /// pianificato del MES, come per il salvataggio (vedi <see cref="SaveAsync"/>).
    /// </para>
    /// </summary>
    Task<string> CreateAsync(NewBatchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Elimina un lotto e tutto quello che gli appartiene, e avvisa l'ERP.
    /// <para>
    /// La cascata comprende ordini di lotto e di billetta, billette, <b>presenze degli
    /// operatori</b> e rettifiche; poi il lotto. Il messaggio verso l'ERP
    /// (<c>NPOPackingManager.processBatchDeletion</c>) parte nella stessa transazione: e' l'unica
    /// azione irreversibile verso l'esterno di tutto il modulo, e non deve poter partire per un
    /// lotto che poi resta.
    /// </para>
    /// <para>
    /// Un lotto gia' riconciliato non si elimina — il vecchio applicativo lo permetteva.
    /// </para>
    /// </summary>
    Task DeleteAsync(string batchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forza la chiusura di pressa e/o sega richiamando <c>usp_Batch_PressClose</c> e
    /// <c>usp_Batch_SawClose</c>. Sono procedure del MES: questa applicazione decide quando
    /// chiamarle, non cosa facciano.
    /// </summary>
    Task ForceCloseAsync(
        string batchId,
        bool press,
        bool saw,
        CancellationToken cancellationToken = default);
}
