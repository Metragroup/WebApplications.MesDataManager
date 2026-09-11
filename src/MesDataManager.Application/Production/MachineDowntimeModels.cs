namespace MesDataManager.Application.Production;

/// <summary>Riga della griglia Fermi macchina, gia' risolta con le descrizioni di causale e tipo.</summary>
/// <param name="DowntimeCode">
/// Codice del fermo, composto da pressa e istante di fine (<c>MP1250910103607</c>). Non e' la
/// chiave — quella e' <paramref name="Id"/> — ma e' il riferimento con cui il fermo viene
/// nominato in reparto, ed e' per questo che la scheda del lotto lo mostra.
/// </param>
public sealed record MachineDowntimeRow(
    int Id,
    string PressId,
    DateTime StartTs,
    DateTime StopTs,
    TimeSpan Duration,
    string DowntimeCode,
    short ReasonId,
    string ReasonDescription,
    short TypeId,
    string TypeDescription);

/// <summary>
/// Criteri di lettura dei fermi macchina. <see cref="PressId"/> e <see cref="DowntimeReasonId"/>
/// nulli valgono "tutte/tutti": il filtro pressa e la causale sono opzionali, il tipo no — una
/// riga di <c>BatchDowntime</c> ha sempre un tipo.
/// </summary>
public sealed record MachineDowntimeQuery(
    string? PressId,
    short DowntimeTypeId,
    short? DowntimeReasonId,
    DateTime From,
    DateTime To,
    int PageNumber = 1,
    int PageSize = 25);

/// <summary>Una pagina di fermi, con il totale delle righe che soddisfano il filtro.</summary>
public sealed record MachineDowntimePage(IReadOnlyList<MachineDowntimeRow> Rows, int TotalCount);

/// <summary>
/// Dati modificabili di un fermo. <see cref="Id"/> nullo significa inserimento: codice, tipo di
/// guasto e stato di modifica li assegna il servizio, non l'operatore.
/// </summary>
public sealed record MachineDowntimeEditModel(
    int? Id,
    string PressId,
    DateTime StartTs,
    DateTime StopTs,
    short DowntimeReasonId,
    short DowntimeTypeId);
