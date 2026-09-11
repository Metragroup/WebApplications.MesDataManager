namespace MesDataManager.Domain.Entities;

/// <summary>
/// Rettifica delle quantita' di barre di un lotto (Press.BatchBarQty).
/// <para>
/// Non e' una misura: e' la correzione che l'operatore della sega registra quando il conteggio
/// automatico non corrisponde a quanto e' stato realmente incestato. Per questo
/// <see cref="Qty"/> ammette anche valori negativi.
/// </para>
/// <para>
/// Il pannello che le mostra compare solo se <see cref="Company.SawAllowAdjustments"/> e' vero,
/// come nel vecchio applicativo.
/// </para>
/// </summary>
public sealed class BatchBarQty
{
    public int BatchBarQtyId { get; set; }
    public string PressId { get; set; } = null!;
    public string BatchId { get; set; } = null!;

    /// <summary>Lunghezza della barra in millimetri.</summary>
    public decimal BarLength { get; set; }

    public string? ProdId { get; set; }

    /// <summary>Quantita' rettificata: positiva se manca, negativa se e' in eccesso.</summary>
    public int Qty { get; set; }

    /// <summary>A database ha valore predefinito <c>getdate()</c>: in inserimento va comunque valorizzata.</summary>
    public DateTime CreatedTs { get; set; }
}
