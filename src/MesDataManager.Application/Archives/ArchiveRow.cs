using MesDataManager.Domain.Abstractions;

namespace MesDataManager.Application.Archives;

/// <summary>
/// Riga di anagrafica in forma neutra: valori indicizzati per nome di campo. Consente a griglia
/// e form di essere scritti una volta sola, senza un tipo generico per ogni tabella e senza
/// esporre le entita' EF Core alla UI.
/// </summary>
public sealed class ArchiveRow
{
    private readonly Dictionary<string, object?> _values;

    public ArchiveRow(IDictionary<string, object?> values)
    {
        _values = new Dictionary<string, object?>(values, StringComparer.Ordinal);
    }

    public static ArchiveRow Empty() => new(new Dictionary<string, object?>());

    public IReadOnlyDictionary<string, object?> Values => _values;

    public object? this[string field]
    {
        get => _values.GetValueOrDefault(field);
        set => _values[field] = value;
    }

    public bool Has(string field) => _values.ContainsKey(field);

    public T? Get<T>(string field) => _values.GetValueOrDefault(field) is T typed ? typed : default;

    /// <summary>Stato di attivazione locale, se l'anagrafica lo prevede.</summary>
    public bool IsActive => Get<bool>(nameof(IActivatable.IsActive));

    /// <summary>
    /// Abilitazione a livello ERP. Quando e' false il flag "Attivo" non e' modificabile:
    /// lo stabilimento non puo' riattivare una voce che il master ha disabilitato.
    /// </summary>
    public bool IsActiveMaster => Get<bool>(nameof(IMasterControlled.IsActiveMaster));

    public ArchiveRow Clone() => new(_values);

    /// <summary>Estrae i soli valori di chiave primaria, per identificare la riga a database.</summary>
    public Dictionary<string, object?> ExtractKey(ArchiveDescriptor descriptor) =>
        descriptor.KeyFields.ToDictionary(f => f.Name, f => this[f.Name], StringComparer.Ordinal);
}
