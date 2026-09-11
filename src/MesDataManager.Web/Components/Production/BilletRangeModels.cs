namespace MesDataManager.Web.Components.Production;

/// <summary>Cosa sta chiedendo <c>BatchBilletRangeDialog</c>.</summary>
public enum BilletRangeMode
{
    /// <summary>Duplica una billetta: quante copie, da quale numero, in che intervallo.</summary>
    Duplicate,

    /// <summary>Rinumera le billette selezionate a partire da un numero.</summary>
    Renumber,
}

/// <summary>
/// Intervallo scelto. Per la rinumerazione contano solo <see cref="Count"/> e
/// <see cref="FromNo"/>: rinumerare non sposta niente nel tempo.
/// </summary>
public sealed record BilletRangeResult(int Count, short FromNo, DateTime From, DateTime To);
