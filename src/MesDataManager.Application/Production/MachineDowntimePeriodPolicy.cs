namespace MesDataManager.Application.Production;

/// <summary>
/// Regola del periodo massimo interrogabile, per tipo di fermo. Non esisteva nel vecchio
/// applicativo: e' nuova in questa riscrittura, per evitare interrogazioni senza limiti su una
/// tabella da 4,59 milioni di righe.
/// <para>
/// L'abbinamento e' sulla descrizione del tipo ("Macrofermo"/"Microfermo"), non sul suo id: l'id
/// delle due righe di <c>MasterData.PressDowntimeType</c> dipende dai dati del database e non e'
/// una costante deducibile dal codice.
/// </para>
/// </summary>
public static class MachineDowntimePeriodPolicy
{
    public const string Macrofermo = "Macrofermo";
    public const string Microfermo = "Microfermo";

    public const int MacrofermoMaxDays = 7;
    public const int MicrofermoMaxDays = 1;

    /// <summary>
    /// Giorni di calendario ammessi per il tipo indicato, conteggio inclusivo: <c>dal == al</c>
    /// vale un giorno. Un tipo diverso dai due noti e' un errore di configurazione del database
    /// (la tabella ha solo quelle due righe) e non deve passare silenziosamente: si preferisce
    /// fallire in modo esplicito piuttosto che applicare un limite a caso.
    /// </summary>
    public static int MaxDaysFor(string downtimeTypeDescription) => downtimeTypeDescription switch
    {
        Macrofermo => MacrofermoMaxDays,
        Microfermo => MicrofermoMaxDays,
        _ => throw new InvalidOperationException(
            $"Tipo di fermo sconosciuto: '{downtimeTypeDescription}'. Attesi solo " +
            $"'{Macrofermo}' o '{Microfermo}' da MasterData.PressDowntimeType."),
    };

    /// <summary>
    /// Convalida il periodo scelto. Lancia <see cref="ProductionException"/> se la fine precede
    /// l'inizio o se il numero di giorni di calendario coperti supera il massimo del tipo.
    /// </summary>
    public static void Validate(DateTime from, DateTime to, string downtimeTypeDescription)
    {
        if (to.Date < from.Date)
        {
            throw ProductionException.PeriodInvalid();
        }

        var days = (to.Date - from.Date).Days + 1;
        var maxDays = MaxDaysFor(downtimeTypeDescription);

        if (days > maxDays)
        {
            throw ProductionException.PeriodTooWide(downtimeTypeDescription, maxDays);
        }
    }
}
