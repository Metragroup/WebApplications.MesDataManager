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
}
