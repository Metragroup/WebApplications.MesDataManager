namespace MesDataManager.Application.Archives;

/// <summary>Motivo per cui un'operazione su un'anagrafica non e' andata a buon fine.</summary>
public enum ArchiveErrorKind
{
    Unknown,
    NotFound,
    Validation,
    DuplicateKey,
    ForeignKeyViolation,
    Forbidden,
}

/// <summary>
/// Errore di dominio che la UI puo' tradurre in un messaggio comprensibile: la chiave di
/// risorsa viaggia con l'eccezione, cosi' la presentazione resta localizzabile.
/// <para>
/// Ogni errore si costruisce da una delle factory statiche qui sotto e mai passando una chiave
/// a mano: sono l'elenco completo dei messaggi che l'applicazione puo' mostrare, e un test le
/// percorre per riflessione verificando che ognuna sia tradotta in tutte le lingue.
/// </para>
/// </summary>
public sealed class ArchiveException(
    ArchiveErrorKind kind,
    string messageKey,
    string? field = null,
    object[]? messageArguments = null,
    Exception? innerException = null)
    : Exception(messageKey, innerException)
{
    public ArchiveErrorKind Kind { get; } = kind;

    /// <summary>Chiave di risorsa del messaggio da mostrare.</summary>
    public string MessageKey { get; } = messageKey;

    /// <summary>Campo che ha causato l'errore, quando individuabile.</summary>
    public string? Field { get; } = field;

    public object[] MessageArguments { get; } = messageArguments ?? [];

    public static ArchiveException Required(string field) =>
        new(ArchiveErrorKind.Validation, ResourceKeys.Error("RequiredField"), field);

    public static ArchiveException TooLong(string field, int maxLength) =>
        new(ArchiveErrorKind.Validation, ResourceKeys.Error("MaxLengthExceeded"), field, [maxLength]);

    /// <summary>
    /// Il valore non entra nel tipo della colonna. Senza questo controllo la conversione
    /// lanciava <see cref="OverflowException"/>, che la UI non sa tradurre.
    /// </summary>
    public static ArchiveException OutOfRange(string field, object minimum, object maximum) =>
        new(ArchiveErrorKind.Validation, ResourceKeys.Error("ValueOutOfRange"), field, [minimum, maximum]);

    /// <summary>Il valore non e' convertibile nel tipo della colonna.</summary>
    public static ArchiveException InvalidValue(string field, Exception? innerException = null) =>
        new(ArchiveErrorKind.Validation, ResourceKeys.Error("InvalidValue"), field, innerException: innerException);

    /// <summary>
    /// Su un'anagrafica allineata dall'ERP il flag "Attivo" e' stato alzato su una voce che a
    /// monte non e' abilitata. Riusa il testo dell'omonimo suggerimento mostrato nel form.
    /// </summary>
    public static ArchiveException MasterActivationLocked(string field) =>
        new(ArchiveErrorKind.Validation, ResourceKeys.Message("IsActiveLockedHint"), field);

    public static ArchiveException Forbidden() =>
        new(ArchiveErrorKind.Forbidden, ResourceKeys.Error("AccessDenied"));

    public static ArchiveException NotFound() =>
        new(ArchiveErrorKind.NotFound, ResourceKeys.Error("RecordNotFound"));

    /// <summary>
    /// La chiave di anagrafica richiesta non e' nel catalogo: URL inventato o descrittore
    /// rimosso. Non riguarda un campo, quindi il messaggio resta senza etichetta davanti.
    /// </summary>
    public static ArchiveException ArchiveNotFound() =>
        new(ArchiveErrorKind.NotFound, ResourceKeys.Error("ArchiveNotFound"));

    public static ArchiveException DuplicateKey(Exception? innerException = null) =>
        new(ArchiveErrorKind.DuplicateKey, ResourceKeys.Error("DuplicateKey"), innerException: innerException);

    public static ArchiveException ForeignKeyViolation(Exception? innerException = null) =>
        new(
            ArchiveErrorKind.ForeignKeyViolation,
            ResourceKeys.Error("ForeignKeyViolation"),
            innerException: innerException);

    public static ArchiveException SaveFailed(Exception? innerException = null) =>
        new(ArchiveErrorKind.Unknown, ResourceKeys.Error("SaveFailed"), innerException: innerException);
}
