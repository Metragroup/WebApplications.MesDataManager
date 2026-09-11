namespace MesDataManager.Domain.Entities;

/// <summary>
/// Stato d'uso di una matrice (vista <c>EF.NPOWRKCTRSETUPTABLE</c>). Vedi
/// <see cref="DieUseStatus"/> per i valori.
/// </summary>
public sealed class DieSetup
{
    public string DieId { get; set; } = null!;
    public int StatusUse { get; set; }
}

/// <summary>
/// Valori di <see cref="DieSetup.StatusUse"/>, come li interpretava la diagnosi locale del
/// vecchio applicativo.
/// <para>
/// Il cambio matrice li usa per decidere: <see cref="Stored"/>, <see cref="Deleted"/> e
/// <see cref="Transferred"/> impediscono l'assegnazione, <see cref="Test"/> la consente con un
/// avviso, <see cref="Available"/> passa. Nel vecchio applicativo questo controllo lo faceva solo
/// la diagnostica, a lotto gia' modificato.
/// </para>
/// </summary>
public static class DieUseStatus
{
    public const int Test = 0;
    public const int Available = 1;
    public const int Stored = 2;
    public const int Deleted = 4;
    public const int Transferred = 5;
}
