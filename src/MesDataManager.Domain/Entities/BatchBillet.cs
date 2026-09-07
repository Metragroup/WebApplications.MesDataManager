namespace MesDataManager.Domain.Entities;

/// <summary>
/// Billetta di un lotto (Press.BatchBillet), 4,95 milioni di righe.
/// <para>
/// Come per <see cref="Batch"/> sono mappate le colonne non nullable e quelle mostrate. Le non
/// nullable servono anche in sola lettura: <see cref="SecCycle"/>,
/// <see cref="BatchBilletRawId"/>, <see cref="DieId"/>, <see cref="TypeId"/> e
/// <see cref="BilletNo"/> non hanno un valore predefinito a database, quindi un futuro
/// inserimento dovra' valorizzarle tutte.
/// </para>
/// </summary>
public sealed class BatchBillet
{
    public int BatchBilletId { get; set; }
    public int BatchBilletRawId { get; set; }
    public string BatchId { get; set; } = null!;
    public string PressId { get; set; } = null!;
    public string DieId { get; set; } = null!;

    /// <summary>Vedi <see cref="BatchBilletType"/>.</summary>
    public byte TypeId { get; set; }

    public short BilletNo { get; set; }
    public int SecCycle { get; set; }

    public DateTime? StartTs { get; set; }
    public DateTime? StopTs { get; set; }

    public string? ShiftId { get; set; }

    public decimal? MmBarSet { get; set; }
    public int? MmBilletAct { get; set; }

    public decimal? KgSheared { get; set; }
    public decimal? KgExtruded { get; set; }

    /// <summary>
    /// Una billetta puo' essere composta da due tronconi di colate diverse: da qui la coppia di
    /// gruppi <c>Billet1_*</c> e <c>Billet2_*</c>. La lega deriva dalla colata e non si imposta
    /// a mano.
    /// </summary>
    public string? Billet1CastingId { get; set; }

    public string? Billet1AlloyId { get; set; }
    public decimal? Billet1Kg { get; set; }
    public string? Billet2CastingId { get; set; }
    public string? Billet2AlloyId { get; set; }
    public decimal? Billet2Kg { get; set; }

    public string? ProdId { get; set; }

    /// <summary><c>N</c> nuova, <c>M</c> modificata a mano, <c>A</c> aggiunta dalla diagnostica.</summary>
    public string? EditStatusId { get; set; }
}

/// <summary>
/// Valori di <see cref="BatchBillet.TypeId"/>. Le righe 0 e 2 non sono billette: sono i due
/// marcatori che aprono e chiudono il lotto, e portano gli stessi istanti della testata.
/// <para>
/// Conseguenza pratica: ogni conteggio o somma sulle billette deve filtrare
/// <see cref="Real"/>, altrimenti i marcatori inquinano i totali. Anche il vecchio applicativo
/// elencava solo le billette vere.
/// </para>
/// </summary>
public static class BatchBilletType
{
    /// <summary>Marcatore di apertura del lotto.</summary>
    public const byte BatchStart = 0;

    /// <summary>Billetta effettivamente estrusa.</summary>
    public const byte Real = 1;

    /// <summary>Marcatore di chiusura del lotto, che porta la causale.</summary>
    public const byte BatchStop = 2;
}
