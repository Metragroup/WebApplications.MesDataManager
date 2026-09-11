namespace MesDataManager.Application.Production;

/// <summary>
/// Dati per l'inserimento di un blocco di billette.
/// <para>
/// <see cref="KgSheared"/> e <see cref="KgExtruded"/> sono i <b>totali</b> del blocco e vengono
/// divisi per il numero di billette, come nel vecchio applicativo
/// (<c>BatchesPresenter.AddBillet</c>): chi inserisce sa quanto pesa l'insieme, non la singola.
/// </para>
/// <para>
/// La stessa richiesta serve in due posti — l'aggiunta a un lotto esistente e la creazione di un
/// lotto nuovo — quindi convalida e distribuzione stanno qui, e non in uno dei due chiamanti.
/// </para>
/// </summary>
public sealed record NewBilletsRequest(
    int Count,
    short FromNo,
    DateTime From,
    DateTime To,
    decimal BarLengthMm,
    int BilletLengthMm,
    decimal KgSheared,
    decimal KgExtruded,
    string? ProdId,
    string? CastingId,
    string? AlloyId)
{
    /// <summary>
    /// Convalida la richiesta e distribuisce tempo e chilogrammi fra le billette.
    /// </summary>
    /// <param name="batchStart">
    /// Inizio del lotto, quando esiste: una billetta non puo' cominciare prima. Il vecchio
    /// applicativo controllava solo questo estremo e non la fine — allungare il lotto in coda e'
    /// legittimo, e il salvataggio riallinea il marcatore di chiusura.
    /// </param>
    public IReadOnlyList<PlannedBillet> Plan(DateTime? batchStart = null)
    {
        Validate(batchStart);

        var perBillet = (To - From) / Count;
        var kgShearedEach = decimal.Round(KgSheared / Count, 5);
        var kgExtrudedEach = decimal.Round(KgExtruded / Count, 5);

        var planned = new List<PlannedBillet>(Count);
        var start = From;

        for (var i = 0; i < Count; i++)
        {
            var stop = start + perBillet;

            planned.Add(new PlannedBillet(
                BilletNo: (short)(FromNo + i),
                StartTs: start,
                StopTs: stop,
                MmBarSet: BarLengthMm,
                MmBilletAct: BilletLengthMm,
                KgSheared: kgShearedEach,
                KgExtruded: kgExtrudedEach,
                ProdId: Normalize(ProdId),
                CastingId: Normalize(CastingId),
                AlloyId: Normalize(AlloyId),
                SecCycle: (int)perBillet.TotalSeconds));

            start = stop;
        }

        return planned;
    }

    /// <summary>
    /// Le convalide che <c>FrmNewBillet.CheckDataInput</c> faceva sul form, portate dove valgono
    /// per tutti: interfaccia, test e un domani un'altra interfaccia.
    /// </summary>
    private void Validate(DateTime? batchStart)
    {
        if (Count <= 0)
        {
            throw ProductionException.InvalidValue(nameof(Count));
        }

        if (To < From)
        {
            throw ProductionException.PeriodInvalid();
        }

        if (batchStart is { } start && From < start)
        {
            throw ProductionException.InvalidValue(nameof(From));
        }

        if (BarLengthMm <= 0)
        {
            throw ProductionException.InvalidValue(nameof(BarLengthMm));
        }

        if (BilletLengthMm <= 0)
        {
            throw ProductionException.InvalidValue(nameof(BilletLengthMm));
        }

        if (BarLengthMm < BilletLengthMm)
        {
            throw ProductionException.InvalidValue(nameof(BarLengthMm));
        }

        if (KgSheared <= 0)
        {
            throw ProductionException.InvalidValue(nameof(KgSheared));
        }

        if (KgExtruded <= 0)
        {
            throw ProductionException.InvalidValue(nameof(KgExtruded));
        }

        // Non si puo' estrudere piu' di quanto si e' cesoiato: e' la prima incoerenza che la
        // diagnostica segnalerebbe, quindi si impedisce di crearla.
        if (KgExtruded > KgSheared)
        {
            throw ProductionException.InvalidValue(nameof(KgExtruded));
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}

/// <summary>
/// Una billetta come esce dalla distribuzione: valori pronti da scrivere, sia su un modello di
/// modifica sia direttamente su una riga nuova di lotto.
/// </summary>
public sealed record PlannedBillet(
    short BilletNo,
    DateTime StartTs,
    DateTime StopTs,
    decimal MmBarSet,
    int MmBilletAct,
    decimal KgSheared,
    decimal KgExtruded,
    string? ProdId,
    string? CastingId,
    string? AlloyId,
    int SecCycle);
