namespace MesDataManager.Application.Archives;

/// <summary>Come il campo va presentato e validato a UI.</summary>
public enum ArchiveFieldKind
{
    Text,
    Integer,
    Decimal,
    Boolean,
    Date,
    /// <summary>Testo il cui valore va scelto da un elenco fornito da <c>ILookupProvider</c>.</summary>
    Lookup,
}

/// <summary>Quando un campo e' scrivibile.</summary>
public enum ArchiveFieldEditability
{
    /// <summary>Sempre in sola lettura (chiavi generate dal database, colonne allineate dall'ERP).</summary>
    ReadOnly,

    /// <summary>Scrivibile solo in inserimento: tipico delle chiavi primarie assegnate a mano.</summary>
    OnInsert,

    /// <summary>Scrivibile sia in inserimento sia in modifica.</summary>
    Always,
}

/// <summary>
/// Descrive una colonna di un'anagrafica. E' l'unica fonte di verita' da cui la UI genera
/// griglia e form: aggiungere una colonna a un'anagrafica significa aggiungere un
/// <see cref="ArchiveField"/>, non scrivere una pagina.
/// </summary>
/// <param name="Name">Nome della proprieta' sull'entita' di dominio.</param>
/// <param name="Kind">Tipo logico, che determina l'editor usato a UI.</param>
public sealed record ArchiveField(string Name, ArchiveFieldKind Kind)
{
    /// <summary>Chiave di risorsa per l'etichetta. Se assente si usa <see cref="Name"/>.</summary>
    public string? LabelKey { get; init; }

    /// <summary>Fa parte della chiave primaria.</summary>
    public bool IsKey { get; init; }

    /// <summary>Obbligatorio: colonna NOT NULL senza default.</summary>
    public bool IsRequired { get; init; }

    /// <summary>Lunghezza massima per i campi testuali, allineata alla colonna SQL.</summary>
    public int? MaxLength { get; init; }

    public ArchiveFieldEditability Editability { get; init; } = ArchiveFieldEditability.Always;

    /// <summary>Visibile nella griglia. I campi tecnici restano nel form ma fuori dalla griglia.</summary>
    public bool ShowInGrid { get; init; } = true;

    /// <summary>La colonna occupa lo spazio residuo della griglia (descrizioni, note, e-mail).</summary>
    public bool IsWide { get; init; }

    /// <summary>Chiave del lookup da interrogare quando <see cref="Kind"/> e' <c>Lookup</c>.</summary>
    public string? LookupKey { get; init; }

    /// <summary>Cifre decimali da mostrare per i campi <c>Decimal</c>.</summary>
    public int DecimalDigits { get; init; } = 2;

    public string ResolvedLabelKey => LabelKey ?? Name;
}
