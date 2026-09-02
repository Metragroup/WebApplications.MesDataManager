namespace MesDataManager.Application.Archives;

/// <summary>Nodo dell'albero di navigazione in cui compare l'anagrafica.</summary>
public enum ArchiveGroup
{
    /// <summary>"Archivi generali": tabelle di causali, in gran parte allineate dall'ERP.</summary>
    MasterData,

    /// <summary>"Archivi": tabelle gestite interamente dallo stabilimento.</summary>
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

    /// <summary>Chiave di risorsa del nome mostrato nel menu e nel titolo di pagina.</summary>
    public required string NameKey { get; init; }

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

    public IEnumerable<ArchiveField> KeyFields => Fields.Where(f => f.IsKey);

    public IEnumerable<ArchiveField> GridFields => Fields.Where(f => f.ShowInGrid);

    public bool AllowsInsert => EditPolicy is ArchiveEditPolicy.Full;

    public bool AllowsDelete => EditPolicy is ArchiveEditPolicy.Full;

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
