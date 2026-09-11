namespace MesDataManager.Application.Lookups;

/// <summary>Voce selezionabile in un campo di tipo lookup.</summary>
/// <param name="Value">Valore memorizzato sulla colonna (tipicamente la chiave esterna).</param>
/// <param name="Text">Testo mostrato all'operatore.</param>
public sealed record LookupItem(string Value, string Text);

/// <summary>
/// Fornisce gli elenchi per i campi che puntano a un'altra tabella. Le chiavi disponibili
/// sono raccolte in <see cref="LookupKeys"/>.
/// </summary>
public interface ILookupProvider
{
    Task<IReadOnlyList<LookupItem>> GetAsync(
        string lookupKey,
        CancellationToken cancellationToken = default);
}

/// <summary>Chiavi dei lookup riconosciuti, per evitare stringhe sparse nei descrittori.</summary>
public static class LookupKeys
{
    public const string Companies = "companies";
    public const string Ovens = "ovens";
    public const string Presses = "presses";
    public const string Modules = "modules";
    public const string DowntimeTypes = "downtimeTypes";
    public const string DowntimeReasons = "downtimeReasons";

    /// <summary>Causali di chiusura lotto attive, in ordine di posizione.</summary>
    public const string BatchClosingReasons = "batchClosingReasons";
}
