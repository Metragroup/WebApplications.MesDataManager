namespace MesDataManager.Domain.Abstractions;

/// <summary>Entita' il cui ordine di presentazione all'operatore e' deciso a mano.</summary>
public interface IPositionable
{
    short Position { get; set; }
}
