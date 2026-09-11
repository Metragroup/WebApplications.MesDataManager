namespace MesDataManager.Application.Production;

/// <summary>
/// Diagnostica di un lotto: chiama il servizio esterno, conserva la risposta e inserisce le
/// billette che il servizio ritiene mancanti.
/// <para>
/// Il servizio e' la fonte di verita': se non risponde, l'operazione si ferma con un messaggio.
/// Le ~340 righe di regole locali del vecchio applicativo <b>non</b> sono state riportate —
/// deciso l'8 settembre 2026 — perche' due copie delle stesse regole divergono, e quella locale
/// era gia' l'unica a non essere aggiornata.
/// </para>
/// </summary>
public interface IBatchDiagnosticsService
{
    /// <summary>
    /// Esegue la diagnostica e ne scrive l'esito sul lotto.
    /// <para>
    /// Precondizioni: scrittura sulla produzione, e se il lotto e' in modifica solo l'utente che
    /// lo sta modificando. Un <c>Reader</c> non la esegue: la diagnostica <b>scrive</b>.
    /// </para>
    /// </summary>
    /// <param name="ignoreManualAddedBillets">
    /// Se il servizio deve ignorare le billette aggiunte a mano. Dalla scheda del lotto si passa
    /// <c>false</c>, cioe' si chiede di controllare il lotto <b>come e' adesso</b>, dopo gli
    /// aggiustamenti: e' il caso d'uso per cui il parametro esiste.
    /// </param>
    Task<BatchDiagnosticsOutcome> RunAsync(
        string batchId,
        bool ignoreManualAddedBillets = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Esito di un'esecuzione della diagnostica.
/// </summary>
/// <param name="Status">
/// <c>OK</c>, <c>ATT</c> o <c>ERR</c>, come lo scrive <c>Batch.DiagnosticsStatus</c>.
/// </param>
/// <param name="Report">La risposta letta in forma strutturata, per la scheda.</param>
/// <param name="InsertedBillets">
/// Quante billette mancanti sono state inserite o aggiornate. Vale la pena dirlo: il lotto ha
/// piu' billette di prima e non e' stato l'operatore ad aggiungerle.
/// </param>
public sealed record BatchDiagnosticsOutcome(
    string Status,
    DiagnosticsReport Report,
    int InsertedBillets);

/// <summary>
/// Il client del servizio di diagnostica. Sta dietro un'interfaccia per una ragione precisa:
/// cio' che questa applicazione fa <b>con</b> la risposta — conservarla, inserire le billette
/// mancanti, decidere le chiusure — si verifica senza rete e senza servizio.
/// </summary>
public interface IDiagnosticsClient
{
    /// <summary>
    /// Interroga il servizio e restituisce la risposta <b>come arriva</b>, senza
    /// riserializzarla: quel testo e' cio' che verra' conservato.
    /// </summary>
    /// <exception cref="ProductionException">Se il servizio non risponde o risponde male.</exception>
    Task<string> AnalyzeAsync(
        string batchId,
        bool ignoreManualAddedBillets,
        CancellationToken cancellationToken = default);
}
