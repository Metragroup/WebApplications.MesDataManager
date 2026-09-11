using MesDataManager.Application.Archives;

namespace MesDataManager.Application.Production;

/// <summary>Motivo per cui un'operazione sui dati di produzione non e' andata a buon fine.</summary>
public enum ProductionErrorKind
{
    Unknown,
    NotFound,
    Validation,
    PeriodOverlap,
    Forbidden,

    /// <summary>
    /// Lo stato del dato non consente l'operazione, e non e' colpa di cio' che l'operatore ha
    /// scritto: lotto in modifica da un altro, elaborazione in corso, lotto gia' riconciliato,
    /// blocco perduto. Va distinto da <see cref="Validation"/> perche' non si corregge
    /// cambiando un campo, e da <see cref="Forbidden"/> perche' non dipende dai permessi.
    /// </summary>
    Conflict,
}

/// <summary>
/// Errore di dominio dell'area produzione, sullo stesso modello di <see cref="ArchiveException"/>
/// — mirror deliberato e non condiviso: gli ambiti Archivi e Produzione restano separati anche
/// nei permessi (<c>Archive.Editor</c> contro <c>Production.Editor</c>), e mescolare le due
/// eccezioni in un tipo comune romperebbe quella separazione.
/// <para>
/// Ogni errore si costruisce da una delle factory statiche qui sotto: sono l'elenco completo dei
/// messaggi che l'area produzione puo' mostrare, e un test le percorre per riflessione
/// verificando che ognuna sia tradotta nelle tre lingue.
/// </para>
/// </summary>
public sealed class ProductionException(
    ProductionErrorKind kind,
    string messageKey,
    string? field = null,
    object[]? messageArguments = null,
    Exception? innerException = null)
    : Exception(messageKey, innerException)
{
    public ProductionErrorKind Kind { get; } = kind;

    /// <summary>Chiave di risorsa del messaggio da mostrare.</summary>
    public string MessageKey { get; } = messageKey;

    /// <summary>Campo che ha causato l'errore, quando individuabile.</summary>
    public string? Field { get; } = field;

    public object[] MessageArguments { get; } = messageArguments ?? [];

    public static ProductionException Required(string field) =>
        new(ProductionErrorKind.Validation, ResourceKeys.Error("RequiredField"), field);

    public static ProductionException InvalidValue(string field) =>
        new(ProductionErrorKind.Validation, ResourceKeys.Error("InvalidValue"), field);

    /// <summary>Il periodo scelto supera il massimo ammesso per il tipo di fermo selezionato.</summary>
    public static ProductionException PeriodTooWide(string downtimeTypeDescription, int maxDays) =>
        new(
            ProductionErrorKind.Validation,
            ResourceKeys.Error("DowntimePeriodTooWide"),
            messageArguments: [downtimeTypeDescription, maxDays]);

    /// <summary>La data di fine precede quella di inizio.</summary>
    public static ProductionException PeriodInvalid() =>
        new(ProductionErrorKind.Validation, ResourceKeys.Error("PeriodInvalid"));

    /// <summary>Il periodo interrogato supera il massimo consentito dalla pagina.</summary>
    public static ProductionException PeriodTooWide(int maxDays) =>
        new(ProductionErrorKind.Validation, ResourceKeys.Error("PeriodTooWide"), messageArguments: [maxDays]);

    /// <summary>
    /// Il periodo si sovrappone a uno o piu' fermi gia' registrati sulla stessa pressa
    /// (mirror di <c>RepositoryService.BatchDowntimePeriodNotValid</c> del vecchio applicativo).
    /// </summary>
    public static ProductionException PeriodOverlap(IReadOnlyCollection<int> conflictingIds) =>
        new(
            ProductionErrorKind.PeriodOverlap,
            ResourceKeys.Error("DowntimePeriodOverlap"),
            messageArguments: [string.Join(", ", conflictingIds)]);

    public static ProductionException Forbidden() =>
        new(ProductionErrorKind.Forbidden, ResourceKeys.Error("AccessDenied"));

    public static ProductionException NotFound() =>
        new(ProductionErrorKind.NotFound, ResourceKeys.Error("RecordNotFound"));

    public static ProductionException SaveFailed(Exception? innerException = null) =>
        new(ProductionErrorKind.Unknown, ResourceKeys.Error("SaveFailed"), innerException: innerException);

    /// <summary>
    /// Il lotto e' in modifica da un altro utente. Il messaggio riporta chi e da quando, come
    /// nel vecchio applicativo: senza quei due dati l'operatore non sa a chi chiedere.
    /// </summary>
    public static ProductionException BatchLocked(string? lockUser, DateTime? lockTs) =>
        new(
            ProductionErrorKind.Conflict,
            ResourceKeys.Error("BatchLocked"),
            messageArguments: [lockUser ?? "?", lockTs?.ToString("g") ?? "?"]);

    /// <summary>
    /// L'elaborazione del lotto non e' conclusa (<c>IsBatchProcessed</c> falso): finche' le
    /// procedure di raccolta dati ci stanno lavorando il lotto non si modifica, come nel vecchio
    /// applicativo.
    /// </summary>
    public static ProductionException BatchProcessing() =>
        new(ProductionErrorKind.Conflict, ResourceKeys.Error("BatchProcessing"));

    /// <summary>Il lotto e' gia' passato all'ERP: e' lo stato terminale e congela il lotto.</summary>
    public static ProductionException BatchAlreadyReconciled() =>
        new(ProductionErrorKind.Conflict, ResourceKeys.Error("BatchAlreadyReconciled"));

    /// <summary>
    /// Il periodo del lotto nuovo si sovrappone a uno o piu' lotti della stessa pressa. Il
    /// controllo del vecchio applicativo era incompleto: non vedeva il lotto nuovo che ne
    /// inghiotte uno esistente.
    /// </summary>
    public static ProductionException BatchPeriodOverlap(IReadOnlyCollection<string> conflictingIds) =>
        new(
            ProductionErrorKind.PeriodOverlap,
            ResourceKeys.Error("BatchPeriodOverlap"),
            messageArguments: [string.Join(", ", conflictingIds)]);

    /// <summary>
    /// Esiste gia' un lotto con quel numero: sulla stessa pressa, nello stesso secondo. Il
    /// numero e' pressa piu' istante, quindi e' un caso da riconoscere, non da prevenire.
    /// </summary>
    public static ProductionException BatchAlreadyExists() =>
        new(ProductionErrorKind.Conflict, ResourceKeys.Error("BatchAlreadyExists"));

    /// <summary>
    /// Il servizio di diagnostica non ha risposto, o ha risposto in un modo che non si sa
    /// leggere. Non c'e' ripiego: le regole locali del vecchio applicativo non sono state
    /// riportate, di proposito (decisione dell'8 settembre 2026).
    /// </summary>
    public static ProductionException DiagnosticsUnavailable(Exception? innerException = null) =>
        new(
            ProductionErrorKind.Unknown,
            ResourceKeys.Error("DiagnosticsUnavailable"),
            innerException: innerException);

    /// <summary>
    /// Il blocco non e' piu' dell'utente: gliel'ha portato via un amministratore, oppure e'
    /// scaduto e un altro l'ha preso. Le modifiche in sospeso sono perse, e va detto adesso e
    /// non dopo aver finto un salvataggio riuscito.
    /// </summary>
    public static ProductionException BatchLockLost(string? lockUser) =>
        new(
            ProductionErrorKind.Conflict,
            ResourceKeys.Error("BatchLockLost"),
            messageArguments: [lockUser ?? "?"]);
}
