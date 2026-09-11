namespace MesDataManager.Domain.Entities;

/// <summary>
/// Transazione di incestamento (vista <c>EF.Module_ModuleTrans</c>): una cesta riempita con le
/// barre di un lotto.
/// <para>
/// E' una vista in sola lettura sui dati di raccolta, non una tabella di questa applicazione: le
/// transazioni si consultano, non si modificano — cosi' era anche nel vecchio applicativo.
/// </para>
/// <para>
/// Attenzione: le colonne che la scheda mostra come "Oper. n.", "Operazione", "Centro di lavoro"
/// e "Qta scarto" <b>non stanno qui</b>. Le prime tre vengono dall'ultimo passo lavorato di
/// <see cref="ModuleTransRoute"/>, la quarta dalla somma di <see cref="ModuleTransScrap"/>.
/// </para>
/// </summary>
public sealed class ModuleTrans
{
    public long ModuleTransId { get; set; }

    /// <summary>Numero della cesta, cioe' cio' che la scheda chiama "numero cesta".</summary>
    public string ModuleId { get; set; } = null!;

    public string PressId { get; set; } = null!;
    public string? BatchId { get; set; }
    public string? ProdId { get; set; }
    public string? ItemId { get; set; }
    public string? DieId { get; set; }

    /// <summary>Lunghezza della barra in millimetri.</summary>
    public decimal? BarLength { get; set; }

    public int Qty { get; set; }
    public DateTime CreatedTs { get; set; }
}
