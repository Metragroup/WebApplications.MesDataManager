namespace MesDataManager.Application.Production;

/// <summary>
/// Una billetta in modifica: i valori correnti accanto a quelli di partenza.
/// <para>
/// Non e' l'entita' <c>BatchBillet</c> e non e' tracciata da EF. Le modifiche vivono qui finche'
/// l'operatore non salva, e <see cref="IsModified"/> si ottiene <b>confrontando</b> — non da un
/// flag che qualcuno deve ricordarsi di alzare a ogni cella toccata, che nel vecchio applicativo
/// era il compito del gestore <c>CellDoubleClick</c> della griglia.
/// </para>
/// </summary>
public sealed class BatchBilletEdit
{
    private readonly BatchBilletRow? _original;

    /// <summary>Billetta esistente, con i suoi valori di partenza.</summary>
    internal BatchBilletEdit(BatchBilletRow original, int localId)
    {
        _original = original;
        LocalId = localId;

        BilletNo = original.BilletNo;
        StartTs = original.StartTs;
        StopTs = original.StopTs;
        ShiftId = original.ShiftId;
        MmBarSet = original.MmBarSet;
        MmBilletAct = original.MmBilletAct;
        KgExtruded = original.KgExtruded;
        Billet1CastingId = original.Billet1CastingId;
        Billet1AlloyId = original.Billet1AlloyId;
        Billet1Kg = original.Billet1Kg;
        Billet2CastingId = original.Billet2CastingId;
        Billet2AlloyId = original.Billet2AlloyId;
        Billet2Kg = original.Billet2Kg;
        ProdId = original.ProdId;
    }

    /// <summary>Billetta nuova, che a database ancora non esiste.</summary>
    internal BatchBilletEdit(int localId) => LocalId = localId;

    /// <summary>
    /// Identita' interna, stabile anche per le billette nuove. La griglia e i comandi ragionano
    /// su questa e non sulla chiave: una billetta appena aggiunta non ha ancora un
    /// <c>BatchBilletID</c>, e usare il numero di billetta come identita' si romperebbe alla
    /// prima rinumerazione.
    /// </summary>
    public int LocalId { get; }

    /// <summary>Chiave a database, nulla per una billetta nuova.</summary>
    public int? Id => _original?.Id;

    public bool IsNew => _original is null;

    public short BilletNo { get; set; }

    public DateTime? StartTs { get; set; }

    public DateTime? StopTs { get; set; }

    /// <summary>Turno: lo assegna la raccolta dati, non si modifica.</summary>
    public string? ShiftId { get; }

    public decimal? MmBarSet { get; set; }

    public int? MmBilletAct { get; set; }

    public decimal? KgExtruded { get; set; }

    public string? Billet1CastingId { get; private set; }

    /// <summary>Lega del primo troncone: deriva dalla colata, non si imposta a mano.</summary>
    public string? Billet1AlloyId { get; private set; }

    public decimal? Billet1Kg { get; set; }

    public string? Billet2CastingId { get; private set; }

    /// <summary>Lega del secondo troncone: deriva dalla colata, non si imposta a mano.</summary>
    public string? Billet2AlloyId { get; private set; }

    public decimal? Billet2Kg { get; set; }

    public string? ProdId { get; set; }

    /// <summary>
    /// Kg cesoiati: non e' un campo da compilare. Li calcola il salvataggio come somma dei kg dei
    /// due tronconi, come faceva <c>BatchesPresenter.CalcBilletsKgSheared</c>.
    /// </summary>
    public decimal? KgSheared => (Billet1Kg ?? 0) + (Billet2Kg ?? 0);

    /// <summary>
    /// Stato di modifica che finira' a database: <c>N</c> per una billetta nuova, <c>M</c> per una
    /// esistente toccata a mano, altrimenti quello che aveva — che puo' essere <c>A</c> se ce
    /// l'ha messa la diagnostica.
    /// </summary>
    public string? EditStatusId => IsNew
        ? "N"
        : IsModified ? "M" : _original?.EditStatusId;

    /// <summary>Vero se un valore e' diverso da come era all'ingresso in modifica.</summary>
    public bool IsModified =>
        _original is { } o &&
        (BilletNo != o.BilletNo ||
         StartTs != o.StartTs ||
         StopTs != o.StopTs ||
         MmBarSet != o.MmBarSet ||
         MmBilletAct != o.MmBilletAct ||
         KgExtruded != o.KgExtruded ||
         !Same(Billet1CastingId, o.Billet1CastingId) ||
         Billet1Kg != o.Billet1Kg ||
         !Same(Billet2CastingId, o.Billet2CastingId) ||
         Billet2Kg != o.Billet2Kg ||
         !Same(ProdId, o.ProdId));

    /// <summary>
    /// Assegna la colata del primo troncone con la sua lega. Le due vanno insieme di proposito:
    /// la lega deriva dalla colata, e un metodo per ciascuna renderebbe possibile scriverne una
    /// senza l'altra.
    /// </summary>
    public void SetCasting1(string? castingId, string? alloyId)
    {
        Billet1CastingId = Normalize(castingId);
        Billet1AlloyId = Normalize(alloyId);
    }

    public void SetCasting2(string? castingId, string? alloyId)
    {
        Billet2CastingId = Normalize(castingId);
        Billet2AlloyId = Normalize(alloyId);
    }

    /// <summary>Ordine di produzione, normalizzato come lo scriveva il vecchio applicativo.</summary>
    public void SetProdId(string? prodId) => ProdId = Normalize(prodId);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    /// <summary>
    /// Confronto dei codici: a database sono colonne <c>char</c>, quindi arrivano con spazi di
    /// riempimento, e un valore vuoto e uno assente vanno considerati la stessa cosa.
    /// </summary>
    private static bool Same(string? left, string? right) =>
        string.Equals(
            string.IsNullOrWhiteSpace(left) ? null : left.Trim(),
            string.IsNullOrWhiteSpace(right) ? null : right.Trim(),
            StringComparison.OrdinalIgnoreCase);
}
