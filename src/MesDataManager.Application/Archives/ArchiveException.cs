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
        new(ArchiveErrorKind.Validation, "RequiredField", field);

    public static ArchiveException TooLong(string field, int maxLength) =>
        new(ArchiveErrorKind.Validation, "MaxLengthExceeded", field, [maxLength]);

    public static ArchiveException Forbidden() =>
        new(ArchiveErrorKind.Forbidden, "AccessDenied");

    public static ArchiveException NotFound() =>
        new(ArchiveErrorKind.NotFound, "RecordNotFound");
}
