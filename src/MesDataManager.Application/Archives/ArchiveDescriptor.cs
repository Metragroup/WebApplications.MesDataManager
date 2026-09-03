namespace MesDataManager.Application.Archives;

/// <summary>Nodo dell'albero di navigazione in cui compare l'anagrafica.</summary>
public enum ArchiveGroup
{
    /// <summary>"Anagrafiche di gruppo": tabelle valide per tutte le sedi, in gran parte allineate dall'ERP.</summary>
    MasterData,

    /// <summary>"Anagrafiche di impianto": tabelle gestite interamente dallo stabilimento.</summary>
    Plant,
}

/// <summary>Quali operazioni sono ammesse su un'anagrafica.</summary>
public enum ArchiveEditPolicy
{
    /// <summary>Inserimento, modifica di tutti i campi ed eliminazione.</summary>
    Full,

    /// <summary>
    /// L'elenco delle voci arriva dall'ERP: niente inserimenti ne' eliminazioni, e in modifica
    /// si toccano solo posizione e flag di attivazione. Replica il comportamento delle
    /// "master table" dell'applicazione WinForms.
    /// </summary>
    MasterControlled,

    /// <summary>Sola consultazione.</summary>
    ReadOnly,
}

/// <summary>
/// Metadati completi di un'anagrafica: identificano la tabella, il posto che occupa nel menu,
/// cosa si puo' farci e da quali campi e' composta.
/// </summary>
public sealed class ArchiveDescriptor
{
    public required string Key { get; init; }

    public required Type EntityType { get; init; }

    /// <summary>
    /// Chiave di risorsa del nome mostrato nel menu e nel titolo di pagina. Si ricava da
    /// <see cref="Key"/>: il gruppo <c>Archive.</c> tiene i nomi delle anagrafiche separati
    /// dalle etichette dei campi, quindi non serve dichiararla ne' distinguerla a mano.
    /// </summary>
    public string NameKey => ResourceKeys.Archive(Key);

    public required ArchiveGroup Group { get; init; }

    public ArchiveEditPolicy EditPolicy { get; init; } = ArchiveEditPolicy.Full;

    public required IReadOnlyList<ArchiveField> Fields { get; init; }

    /// <summary>Campi su cui agisce la ricerca libera della griglia.</summary>
    public IReadOnlyList<string> SearchableFields { get; init; } = [];

    /// <summary>Ordinamento predefinito, applicato lato database.</summary>
    public IReadOnlyList<string> DefaultSort { get; init; } = [];

    /// <summary>L'entita' ha una colonna IsActive, quindi il filtro "mostra voci non attive" ha senso.</summary>
    public bool SupportsActiveFilter { get; init; }

    /// <summary>L'entita' ha una colonna IsActive_Master governata dall'ERP.</summary>
    public bool HasMasterFlag { get; init; }

    /// <summary>
    /// Vieta l'eliminazione anche dove la policy la prevederebbe.
    /// <para>
    /// Serve per le tabelle referenziate dai dati di produzione **senza** un vincolo di chiave
    /// esterna a database: lì il `DELETE` non verrebbe respinto da nessuno e lascerebbe
    /// riferimenti orfani nello storico. E' un fatto sullo schema, non sul modo in cui
    /// l'anagrafica e' governata, e per questo sta fuori da <see cref="ArchiveEditPolicy"/>:
    /// il giorno in cui il vincolo viene aggiunto si toglie questa riga e nient'altro.
    /// </para>
    /// </summary>
    public bool PreventDelete { get; init; }

    public IEnumerable<ArchiveField> KeyFields => Fields.Where(f => f.IsKey);

    public IEnumerable<ArchiveField> GridFields => Fields.Where(f => f.ShowInGrid);

    public bool AllowsInsert => EditPolicy is ArchiveEditPolicy.Full;

    public bool AllowsDelete => EditPolicy is ArchiveEditPolicy.Full && !PreventDelete;

    public bool AllowsUpdate => EditPolicy is ArchiveEditPolicy.Full or ArchiveEditPolicy.MasterControlled;

    /// <summary>
    /// Dice se un campo e' scrivibile nel contesto dato, combinando la policy dell'anagrafica
    /// con l'editabilita' del singolo campo.
    /// </summary>
    public bool IsWritable(ArchiveField field, bool isNewRecord)
    {
        if (EditPolicy is ArchiveEditPolicy.ReadOnly)
        {
            return false;
        }

        if (EditPolicy is ArchiveEditPolicy.MasterControlled
            && field.Name is not (nameof(Domain.Abstractions.IPositionable.Position)
                or nameof(Domain.Abstractions.IActivatable.IsActive)))
        {
            return false;
        }

        return field.Editability switch
        {
            ArchiveFieldEditability.Always => true,
            ArchiveFieldEditability.OnInsert => isNewRecord,
            _ => false,
        };
    }
}
