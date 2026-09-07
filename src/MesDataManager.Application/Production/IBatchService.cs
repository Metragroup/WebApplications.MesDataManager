namespace MesDataManager.Application.Production;

/// <summary>
/// Lettura dei lotti di estrusione. Questa prima tranche e' di sola consultazione: la modifica
/// del lotto e delle billette, le chiusure forzate, la marcatura per l'ERP e l'eliminazione
/// arriveranno con le tranche successive, che hanno bisogno di sapere cosa ricalcola
/// <c>usp_Batch_Elab</c>.
/// </summary>
public interface IBatchService
{
    Task<BatchPage> GetPageAsync(BatchListQuery query, CancellationToken cancellationToken = default);

    /// <summary>Testata e billette del lotto. Lancia <see cref="ProductionException"/> se non esiste.</summary>
    Task<BatchDetail> GetDetailAsync(string batchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Elenco dei lotti gia' chiusi a pressa e a sega, con i filtri della riconciliazione ERP.
    /// La modalita' "dettaglio lunghezza" cambia la sorgente, non solo le colonne: vedi
    /// <see cref="ProductionBatchQuery.LengthDetail"/>.
    /// </summary>
    Task<ProductionBatchPage> GetProductionPageAsync(
        ProductionBatchQuery query,
        CancellationToken cancellationToken = default);
}
