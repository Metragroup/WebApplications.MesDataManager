namespace MesDataManager.Application.Production;

/// <summary>
/// Regole del blocco di modifica di un lotto.
/// <para>
/// Il blocco e' pessimistico e persistito su riga (<c>Batch.IsLock</c>, <c>Lock_Usr</c>,
/// <c>Lock_Ts</c>), come nel vecchio applicativo. La differenza e' la <b>scadenza</b>: nel
/// WinForms un blocco orfano restava tale a tempo indeterminato, e sul database di test si vedono
/// ancora lotti bloccati da nessuno. Nel web la sessione muore in silenzio — finestra chiusa,
/// circuito caduto, portatile chiuso a meta' turno — quindi il blocco orfano sarebbe la norma e
/// non l'eccezione.
/// </para>
/// </summary>
public static class BatchLockPolicy
{
    /// <summary>Dopo quanto un blocco puo' essere preso da un altro utente.</summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromMinutes(60);

    public const int ExpiryMinutes = 60;

    /// <summary>
    /// Vero se il blocco e' abbandonato, cioe' se e' piu' vecchio della scadenza.
    /// <para>
    /// La scadenza conta solo per <b>gli altri</b>: chi possiede il blocco continua a lavorare e a
    /// salvare anche oltre l'ora, purche' nessuno gliel'abbia portato via nel frattempo. Altrimenti
    /// una modifica lunga — venti billette da rinumerare, con una telefonata in mezzo — si
    /// perderebbe proprio nel momento del salvataggio.
    /// </para>
    /// <para>
    /// Un blocco senza istante e' considerato scaduto: e' il caso dei blocchi lasciati dal vecchio
    /// applicativo, che nessuno rilascerebbe mai.
    /// </para>
    /// </summary>
    public static bool IsExpired(DateTime? lockTs, DateTime now) =>
        lockTs is not { } ts || now - ts >= Expiry;
}
