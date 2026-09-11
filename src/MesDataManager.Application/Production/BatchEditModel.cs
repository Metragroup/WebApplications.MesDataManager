namespace MesDataManager.Application.Production;

/// <summary>
/// Le modifiche in sospeso su un lotto preso in modifica.
/// <para>
/// Non e' un'entita' e non tocca il database: vive nel circuito di chi sta modificando, e arriva
/// al servizio tutta insieme al salvataggio, che la scrive in una sola transazione. E' il
/// modello del vecchio applicativo — contesto EF unico di sessione e <c>SaveChanges</c> al
/// "Salva" — senza il contesto EF di sessione, che in una applicazione web sarebbe condiviso
/// male e terrebbe aperta una connessione per tutta la modifica.
/// </para>
/// <para>
/// Tiene la fotografia di partenza (<see cref="Original"/>) accanto ai valori correnti, cosi'
/// <see cref="HasChanges"/> risponde confrontando e non fidandosi di un flag che qualcuno deve
/// ricordarsi di alzare.
/// </para>
/// </summary>
public sealed class BatchEditModel(BatchDetail original)
{
    private readonly List<BatchBilletEdit> _billets =
        [.. original.Billets.Select((row, index) => new BatchBilletEdit(row, index))];

    private readonly List<int> _deletedBilletIds = [];

    private readonly List<BatchAdjustmentEdit> _adjustments =
        [.. original.Adjustments.Select((row, index) => new BatchAdjustmentEdit(row, index))];

    private readonly List<int> _deletedAdjustmentIds = [];

    private int _nextAdjustmentLocalId = original.Adjustments.Count;

    /// <summary>
    /// Contatore delle identita' interne. Parte dal numero di billette esistenti e cresce: ogni
    /// billetta aggiunta ne prende una nuova, e non la restituisce nemmeno se viene rimossa —
    /// riusare un'identita' significherebbe confondere due righe diverse nella stessa sessione.
    /// </summary>
    private int _nextLocalId = original.Billets.Count;

    /// <summary>Il lotto come era all'ingresso in modifica.</summary>
    public BatchDetail Original { get; } = original;

    public string BatchId => Original.BatchId;

    /// <summary>Codice completo della matrice, <c>codice</c> oppure <c>codice/numero</c>.</summary>
    public string? DieId { get; private set; } = original.DieId?.Trim();

    public string? DieCode { get; private set; } = original.DieCode?.Trim();

    public short? DieNumber { get; private set; } = original.DieNumber;

    public short? ClosingReasonId { get; private set; } = original.PressBatchClosingReasonId;

    public bool DieChanged =>
        !string.Equals(DieId, Original.DieId?.Trim(), StringComparison.OrdinalIgnoreCase);

    public bool ClosingReasonChanged => ClosingReasonId != Original.PressBatchClosingReasonId;

    /// <summary>Le billette del lotto, in ordine di numero. Le rimosse non compaiono.</summary>
    public IReadOnlyList<BatchBilletEdit> Billets => _billets;

    /// <summary>Chiavi delle billette esistenti che il salvataggio deve cancellare.</summary>
    public IReadOnlyList<int> DeletedBilletIds => _deletedBilletIds;

    public bool BilletsChanged =>
        _deletedBilletIds.Count > 0 || _billets.Any(b => b.IsNew || b.IsModified);

    /// <summary>Le rettifiche delle barre, dalla piu' vecchia. Le rimosse non compaiono.</summary>
    public IReadOnlyList<BatchAdjustmentEdit> Adjustments => _adjustments;

    /// <summary>Chiavi delle rettifiche che il salvataggio deve cancellare.</summary>
    public IReadOnlyList<int> DeletedAdjustmentIds => _deletedAdjustmentIds;

    public bool AdjustmentsChanged =>
        _deletedAdjustmentIds.Count > 0 || _adjustments.Any(a => a.IsNew || a.IsModified);

    /// <summary>Se c'e' qualcosa da salvare. Il pulsante Salva segue questo, non l'aver aperto un form.</summary>
    public bool HasChanges =>
        DieChanged || ClosingReasonChanged || BilletsChanged || AdjustmentsChanged;

    /// <summary>
    /// Assegna una matrice gia' controllata. Prende <see cref="DieValidation"/> e non tre
    /// stringhe di proposito: una matrice che non e' passata dal controllo non deve poter entrare
    /// nel modello.
    /// </summary>
    public void ChangeDie(DieValidation die)
    {
        ArgumentNullException.ThrowIfNull(die);

        if (!die.IsUsable)
        {
            throw ProductionException.InvalidValue(nameof(DieId));
        }

        DieId = die.DieId;
        DieCode = die.DieCode;
        DieNumber = die.DieNumber;
    }

    /// <summary>Cambia la causale di chiusura. Nullo significa "nessuna causale".</summary>
    public void ChangeClosingReason(short? closingReasonId) => ClosingReasonId = closingReasonId;

    // ------------------------------------------------------------------ billette

    /// <summary>
    /// Aggiunge billette dividendo tempo e chilogrammi in parti uguali, come
    /// <c>BatchesPresenter.AddBillet</c>: i kg indicati sono il <b>totale</b> del blocco, non di
    /// ogni billetta.
    /// </summary>
    public IReadOnlyList<BatchBilletEdit> AddBillets(NewBilletsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var added = new List<BatchBilletEdit>(request.Count);

        // La divisione di tempo e chilogrammi sta nella richiesta: la stessa serve alla
        // creazione di un lotto nuovo, dove non esiste ancora un modello di modifica.
        foreach (var planned in request.Plan(Original.StartTs))
        {
            var billet = new BatchBilletEdit(_nextLocalId++)
            {
                BilletNo = planned.BilletNo,
                StartTs = planned.StartTs,
                StopTs = planned.StopTs,
                MmBarSet = planned.MmBarSet,
                MmBilletAct = planned.MmBilletAct,
                KgExtruded = planned.KgExtruded,
                Billet1Kg = planned.KgSheared,
            };

            billet.SetCasting1(planned.CastingId, planned.AlloyId);
            billet.SetProdId(planned.ProdId);

            added.Add(billet);
            _billets.Add(billet);
        }

        Sort();
        return added;
    }

    /// <summary>
    /// Duplica una billetta un certo numero di volte, distribuendo l'intervallo indicato.
    /// Ricopia tutto tranne numero e istanti, come <c>RepositoryService.DuplicateBatchBillet</c>:
    /// serve a rimettere le billette che la pressa non ha registrato, che sono uguali a quelle
    /// accanto.
    /// </summary>
    public IReadOnlyList<BatchBilletEdit> DuplicateBillet(
        int sourceLocalId,
        int count,
        short fromNo,
        DateTime from,
        DateTime to)
    {
        var source = Find(sourceLocalId);

        if (count <= 0)
        {
            throw ProductionException.InvalidValue(nameof(count));
        }

        if (to < from)
        {
            throw ProductionException.PeriodInvalid();
        }

        var perBillet = (to - from) / count;
        var added = new List<BatchBilletEdit>(count);
        var start = from;

        for (var i = 0; i < count; i++)
        {
            var billet = new BatchBilletEdit(_nextLocalId++)
            {
                BilletNo = (short)(fromNo + i),
                StartTs = start,
                StopTs = start + perBillet,
                MmBarSet = source.MmBarSet,
                MmBilletAct = source.MmBilletAct,
                KgExtruded = source.KgExtruded,
                Billet1Kg = source.Billet1Kg,
                Billet2Kg = source.Billet2Kg,
            };

            billet.SetCasting1(source.Billet1CastingId, source.Billet1AlloyId);
            billet.SetCasting2(source.Billet2CastingId, source.Billet2AlloyId);
            billet.SetProdId(source.ProdId);

            added.Add(billet);
            _billets.Add(billet);
            start += perBillet;
        }

        Sort();
        return added;
    }

    /// <summary>
    /// Rimuove le billette indicate. Quelle esistenti finiscono nell'elenco da cancellare al
    /// salvataggio; quelle appena aggiunte scompaiono e non lasciano traccia.
    /// <para>
    /// A differenza del vecchio applicativo si possono rimuovere piu' billette in un colpo: la
    /// griglia ne cancellava una per volta.
    /// </para>
    /// </summary>
    public void RemoveBillets(IEnumerable<int> localIds)
    {
        ArgumentNullException.ThrowIfNull(localIds);

        foreach (var localId in localIds.Distinct().ToList())
        {
            var billet = Find(localId);

            if (billet.Id is { } id)
            {
                _deletedBilletIds.Add(id);
            }

            _billets.Remove(billet);
        }
    }

    /// <summary>
    /// Rinumera le billette selezionate a partire da un numero, in ordine di numero attuale
    /// (<c>frmRenumberBillet</c>). Agisce sulla selezione e non su tutto il lotto: e' il
    /// comportamento del vecchio applicativo, corretto nel caso di una sola riga selezionata, che
    /// lui ignorava.
    /// </summary>
    public void RenumberBillets(IEnumerable<int> localIds, short fromNo)
    {
        ArgumentNullException.ThrowIfNull(localIds);

        var selected = localIds
            .Distinct()
            .Select(Find)
            .OrderBy(b => b.BilletNo)
            .ToList();

        var next = fromNo;
        foreach (var billet in selected)
        {
            billet.BilletNo = next++;
        }

        Sort();
    }

    /// <summary>Assegna la colata (e la sua lega) alle billette selezionate.</summary>
    public void ApplyCasting(IEnumerable<int> localIds, string castingId, string? alloyId)
    {
        foreach (var billet in Selected(localIds))
        {
            billet.SetCasting1(castingId, alloyId);
        }
    }

    /// <summary>Assegna la lunghezza barra alle billette selezionate.</summary>
    public void ApplyBarLength(IEnumerable<int> localIds, decimal millimetres)
    {
        if (millimetres <= 0)
        {
            throw ProductionException.InvalidValue(nameof(BatchBilletEdit.MmBarSet));
        }

        foreach (var billet in Selected(localIds))
        {
            billet.MmBarSet = millimetres;
        }
    }

    /// <summary>Assegna la lunghezza billetta alle billette selezionate.</summary>
    public void ApplyBilletLength(IEnumerable<int> localIds, int millimetres)
    {
        if (millimetres <= 0)
        {
            throw ProductionException.InvalidValue(nameof(BatchBilletEdit.MmBilletAct));
        }

        foreach (var billet in Selected(localIds))
        {
            billet.MmBilletAct = millimetres;
        }
    }

    /// <summary>Assegna l'ordine di produzione alle billette selezionate.</summary>
    public void ApplyProdId(IEnumerable<int> localIds, string? prodId)
    {
        foreach (var billet in Selected(localIds))
        {
            billet.SetProdId(prodId);
        }
    }

    // ------------------------------------------------------------------ rettifiche

    /// <summary>
    /// Aggiunge una rettifica delle barre. La quantita' puo' essere negativa — e' il caso
    /// interessante — ma non nulla: una rettifica di zero barre non rettifica niente.
    /// </summary>
    public BatchAdjustmentEdit AddAdjustment(decimal barLengthMm, string? prodId, int qty)
    {
        if (barLengthMm <= 0)
        {
            throw ProductionException.InvalidValue(nameof(BatchAdjustmentEdit.BarLength));
        }

        if (qty == 0)
        {
            throw ProductionException.InvalidValue(nameof(BatchAdjustmentEdit.Qty));
        }

        var adjustment = new BatchAdjustmentEdit(_nextAdjustmentLocalId++, DateTime.Now)
        {
            BarLength = barLengthMm,
            ProdId = string.IsNullOrWhiteSpace(prodId) ? null : prodId.Trim().ToUpperInvariant(),
            Qty = qty,
        };

        _adjustments.Add(adjustment);
        return adjustment;
    }

    /// <summary>Rimuove le rettifiche indicate: quelle esistenti vanno cancellate al salvataggio.</summary>
    public void RemoveAdjustments(IEnumerable<int> localIds)
    {
        ArgumentNullException.ThrowIfNull(localIds);

        foreach (var localId in localIds.Distinct().ToList())
        {
            var adjustment = _adjustments.SingleOrDefault(a => a.LocalId == localId)
                ?? throw ProductionException.NotFound();

            if (adjustment.Id is { } id)
            {
                _deletedAdjustmentIds.Add(id);
            }

            _adjustments.Remove(adjustment);
        }
    }

    private IEnumerable<BatchBilletEdit> Selected(IEnumerable<int> localIds)
    {
        ArgumentNullException.ThrowIfNull(localIds);

        return localIds.Distinct().Select(Find).ToList();
    }

    private BatchBilletEdit Find(int localId) =>
        _billets.SingleOrDefault(b => b.LocalId == localId)
        ?? throw ProductionException.NotFound();

    /// <summary>
    /// Tiene le billette in ordine di numero. La griglia mostra quest'ordine, e dopo
    /// un'aggiunta o una rinumerazione le righe nuove devono comparire al loro posto e non in
    /// coda.
    /// </summary>
    private void Sort() => _billets.Sort((left, right) => left.BilletNo.CompareTo(right.BilletNo));

}


