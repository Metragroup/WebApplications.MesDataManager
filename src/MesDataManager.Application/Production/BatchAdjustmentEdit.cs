namespace MesDataManager.Application.Production;

/// <summary>
/// Una rettifica delle barre in modifica (<c>Press.BatchBarQty</c>).
/// <para>
/// Come per le billette i valori correnti stanno accanto a quelli di partenza, e
/// <see cref="IsModified"/> si ottiene confrontando.
/// </para>
/// </summary>
public sealed class BatchAdjustmentEdit
{
    private readonly BatchAdjustmentRow? _original;

    internal BatchAdjustmentEdit(BatchAdjustmentRow original, int localId)
    {
        _original = original;
        LocalId = localId;

        BarLength = original.BarLength;
        ProdId = original.ProdId;
        Qty = original.Qty;
        CreatedTs = original.CreatedTs;
    }

    internal BatchAdjustmentEdit(int localId, DateTime createdTs)
    {
        LocalId = localId;
        CreatedTs = createdTs;
    }

    /// <summary>Identita' interna, stabile anche per le righe che a database non esistono ancora.</summary>
    public int LocalId { get; }

    public int? Id => _original?.Id;

    public bool IsNew => _original is null;

    /// <summary>Lunghezza della barra in millimetri.</summary>
    public decimal BarLength { get; set; }

    public string? ProdId { get; set; }

    /// <summary>
    /// Quantita' rettificata. Puo' essere <b>negativa</b>: e' una correzione di un conteggio
    /// sbagliato, non una misura, e il segno e' l'informazione principale.
    /// </summary>
    public int Qty { get; set; }

    /// <summary>Istante di creazione: sulle righe nuove lo assegna il modello, non l'operatore.</summary>
    public DateTime CreatedTs { get; }

    public bool IsModified =>
        _original is { } o &&
        (BarLength != o.BarLength ||
         Qty != o.Qty ||
         !string.Equals(
             string.IsNullOrWhiteSpace(ProdId) ? null : ProdId.Trim(),
             string.IsNullOrWhiteSpace(o.ProdId) ? null : o.ProdId.Trim(),
             StringComparison.OrdinalIgnoreCase));
}
