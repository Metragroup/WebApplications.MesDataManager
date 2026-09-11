namespace MesDataManager.Application.Production;

/// <summary>
/// Esito del controllo di una matrice, prima di assegnarla a un lotto.
/// <para>
/// Nel vecchio applicativo il cambio matrice verificava solo l'esistenza
/// (<c>FrmChangeMatrix</c>), e lo stato d'uso lo guardava la diagnostica <b>dopo</b>: il lotto
/// poteva quindi finire su una matrice in magazzino o eliminata, e lo si scopriva al controllo
/// successivo. Deciso l'8 settembre 2026 di controllare entrambi qui.
/// </para>
/// </summary>
public enum DieCheck
{
    /// <summary>Esiste ed e' disponibile.</summary>
    Available,

    /// <summary>Esiste ed e' di prova: si assegna, con avviso.</summary>
    Test,

    /// <summary>Non esiste fra i centri di lavoro dell'ERP.</summary>
    NotFound,

    /// <summary>In magazzino: non si assegna.</summary>
    Stored,

    /// <summary>Eliminata: non si assegna.</summary>
    Deleted,

    /// <summary>Trasferita a un altro stabilimento: non si assegna.</summary>
    Transferred,

    /// <summary>
    /// Esiste come centro di lavoro ma non ha una riga di stato d'uso.
    /// <para>
    /// Si assegna <b>senza avviso</b>, come faceva la diagnosi locale del vecchio applicativo, che
    /// sullo stato mancante non diceva nulla. Non e' un caso raro da segnalare: sui sei mesi
    /// precedenti al 9 settembre 2026, su `MES40_RDP_TEST`, 94 lotti su 347 girano su matrici
    /// senza riga di stato. Un avviso su un quarto delle assegnazioni sarebbe rumore, e il rumore
    /// si impara a ignorare — anche quando dice qualcosa.
    /// </para>
    /// </summary>
    Unknown,
}

/// <summary>
/// Matrice controllata: il codice completo come andra' scritto sul lotto e l'esito del
/// controllo.
/// </summary>
/// <param name="DieId">
/// Codice completo, nella forma <c>codice</c> oppure <c>codice/numero</c>: e' il valore che
/// finisce in <c>Batch.DieID</c> e su tutte le billette.
/// </param>
public sealed record DieValidation(string DieId, string DieCode, short? DieNumber, DieCheck Outcome)
{
    /// <summary>Se la matrice si puo' assegnare al lotto.</summary>
    public bool IsUsable => Outcome is DieCheck.Available or DieCheck.Test or DieCheck.Unknown;

    /// <summary>
    /// Se l'assegnazione va accompagnata da un avviso. Solo la matrice di prova: lo stato
    /// mancante e' troppo comune per meritarne uno (vedi <see cref="DieCheck.Unknown"/>).
    /// </summary>
    public bool IsWarning => Outcome is DieCheck.Test;

    /// <summary>
    /// Compone il codice completo dalle due parti, come faceva <c>RepositoryService.ChangeDie</c>:
    /// senza numero resta il solo codice, e non "codice/".
    /// </summary>
    public static string ComposeDieId(string dieCode, short? dieNumber) =>
        dieNumber is { } number ? $"{dieCode.Trim()}/{number}" : dieCode.Trim();
}

/// <summary>
/// Colata trovata dal suo codice, con la lega che le appartiene.
/// <para>
/// Il codice colata <b>non e' unico</b>: su <c>MES40_RDP_TEST</c> 94.122 righe portano 73.151
/// codici distinti. La ricerca restituisce quindi la colata piu' recente, e non "una qualsiasi"
/// come faceva il <c>FirstOrDefault</c> senza ordinamento del vecchio applicativo — che a parita'
/// di codice poteva dare una lega diversa a ogni chiamata.
/// </para>
/// </summary>
public sealed record CastingLookup(string Code, string? AlloyId);
