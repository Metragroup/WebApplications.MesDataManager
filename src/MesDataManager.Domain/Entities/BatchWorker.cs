namespace MesDataManager.Domain.Entities;

/// <summary>
/// Presenza di un operatore su un lotto (<c>Press.BatchWorker</c>).
/// <para>
/// Questa applicazione non la mostra e non la modifica: serve all'<b>eliminazione</b> del lotto,
/// che deve cancellarne le righe. Il vecchio applicativo non lo faceva — l'EDMX non modellava la
/// tabella — e lasciava presenze orfane.
/// </para>
/// </summary>
public sealed class BatchWorker
{
    public int BatchWorkerId { get; set; }
    public string BatchId { get; set; } = null!;
    public short WorkerId { get; set; }
    public DateTime? StartTs { get; set; }
    public DateTime? StopTs { get; set; }
}
