namespace MesDataManager.Application.Production;

/// <summary>Distingue i lotti di produzione da quelli di campionatura.</summary>
public enum BatchTypeFilter
{
    Any,
    Production,
    Sampling,
}

/// <summary>Stato rispetto alla riconciliazione con l'ERP.</summary>
public enum BatchReconciliationFilter
{
    Any,

    /// <summary>Gia' importati dall'ERP.</summary>
    Reconciled,

    /// <summary>Segnati da riconciliare e non ancora importati: e' la coda di lavoro.</summary>
    ToReconcile,

    /// <summary>Non ancora importati, segnati o no.</summary>
    NotReconciled,
}

/// <summary>
/// Criteri dell'elenco dati di produzione, cioe' dei lotti gia' chiusi a pressa e a sega.
/// <para>
/// <see cref="LengthDetail"/> non e' un filtro ma la scelta della sorgente: senza, una riga per
/// lotto; con, una riga per ogni combinazione di lunghezza barra e turno, letta dalla funzione
/// tabellare. Le due sorgenti hanno colonne diverse, ed e' il motivo per cui la casella sta
/// insieme ai filtri e non fra le impostazioni della griglia.
/// </para>
/// </summary>
public sealed record ProductionBatchQuery(
    string? PressId,
    DateTime From,
    DateTime To,
    BatchTypeFilter Type = BatchTypeFilter.Any,
    BatchReconciliationFilter Status = BatchReconciliationFilter.NotReconciled,
    string? BatchId = null,
    string? DieId = null,
    bool LengthDetail = false,
    int PageNumber = 1,
    int PageSize = 25);

/// <summary>Una pagina dell'elenco, con il totale delle righe che soddisfano il filtro.</summary>
public sealed record ProductionBatchPage(IReadOnlyList<ProductionBatchRow> Rows, int TotalCount);

/// <summary>
/// Riga dell'elenco dati di produzione. I quattro campi del dettaglio — lunghezza barra, turno,
/// data turno e lega/trattamento — sono valorizzati solo in quella modalita'.
/// </summary>
public sealed record ProductionBatchRow(
    string BatchId,
    string PressId,
    string? DieId,
    decimal? BarLength,
    string? ShiftId,
    DateTime? ShiftDate,
    string? AlloyAndTreatment,
    int? BilletCount,
    int? BarCount,
    DateTime? StartTs,
    DateTime? StopTs,
    string? ClosingReasonDescription,
    decimal? KgExtruded,
    decimal? KgCut,
    bool IsErpMarked,
    bool IsErpImported,
    bool IsLock,
    string? DiagnosticsStatus)
{
    /// <summary>Durata dell'estrusione, troncata al secondo come nel vecchio applicativo.</summary>
    public TimeSpan? ExtrusionTime => StartTs is { } start && StopTs is { } stop && stop > start
        ? new TimeSpan(0, 0, (int)(stop - start).TotalSeconds)
        : null;

    /// <summary>
    /// Kg tagliati su kg estrusi: quanto del materiale estruso e' diventato barra utile. Senza
    /// estrusi non e' zero, e' indefinita — mostrarla come 0% direbbe una cosa falsa.
    /// </summary>
    public decimal? Efficiency => KgExtruded is > 0 && KgCut is { } cut
        ? cut / KgExtruded.Value
        : null;
}

/// <summary>
/// Periodo massimo interrogabile sull'elenco dati di produzione: una settimana, come per i
/// macrofermi. Serve perche' senza limite la query attraversa 240 mila lotti e, in modalita'
/// dettaglio, altrettante righe scomposte per turno.
/// </summary>
public static class ProductionBatchPeriodPolicy
{
    public const int MaxDays = 7;

    /// <summary>
    /// Convalida il periodo, con conteggio inclusivo dei giorni di calendario: <c>dal == al</c>
    /// vale un giorno.
    /// </summary>
    public static void Validate(DateTime from, DateTime to)
    {
        if (to.Date < from.Date)
        {
            throw ProductionException.PeriodInvalid();
        }

        if ((to.Date - from.Date).Days + 1 > MaxDays)
        {
            throw ProductionException.PeriodTooWide(MaxDays);
        }
    }
}
