namespace MesDataManager.Application.Production;

/// <summary>
/// I tre esiti della diagnostica di lotto, come stanno in <c>Batch.DiagnosticsStatus</c>
/// (<c>char(3)</c>).
/// <para>
/// Sono valori del dato, non nomi scelti qui: 240 mila lotti a database li portano gia', e
/// l'elenco dei lotti li colora. Restano stringhe e non un enumerato perche' e' cosi' che il
/// servizio di diagnostica li restituisce e che il database li conserva.
/// </para>
/// </summary>
public static class DiagnosticsOutcome
{
    /// <summary>Nessun rilievo.</summary>
    public const string Ok = "OK";

    /// <summary>Avvisi, ma niente che impedisca la riconciliazione.</summary>
    public const string Warning = "ATT";

    /// <summary>Errori: il lotto non si segna da riconciliare.</summary>
    public const string Error = "ERR";
}
