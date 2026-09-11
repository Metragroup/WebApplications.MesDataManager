namespace MesDataManager.Web.Components.Production;

/// <summary>
/// Quale dato di billetta sta cambiando <c>BatchBilletValueDialog</c>. Nel vecchio applicativo
/// erano quattro finestre distinte; qui distinguono un controllo solo.
/// </summary>
public enum BilletField
{
    Casting,
    BarLength,
    BilletLength,
    ProdOrder,
}

/// <summary>
/// Valore convalidato che il controllo restituisce. Solo uno dei campi e' valorizzato, secondo
/// <see cref="Field"/>: la lega accompagna la colata perche' ne deriva, e non si sceglie a parte.
/// </summary>
public sealed record BilletValueResult(
    BilletField Field,
    string? Text,
    string? AlloyId,
    decimal? Number);
