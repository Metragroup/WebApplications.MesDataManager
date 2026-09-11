namespace MesDataManager.Domain.Entities;

/// <summary>
/// Matrice di estrusione, dal centro di lavoro dell'ERP (vista <c>EF.WRKCTRTABLE</c>).
/// <para>
/// La vista espone <b>una sola colonna</b>: serve a rispondere "questa matrice esiste?". Lo stato
/// d'uso sta altrove, in <see cref="DieSetup"/>.
/// </para>
/// </summary>
public sealed class Die
{
    /// <summary>Codice della matrice, nella forma <c>codice</c> oppure <c>codice/numero</c>.</summary>
    public string DieId { get; set; } = null!;
}
