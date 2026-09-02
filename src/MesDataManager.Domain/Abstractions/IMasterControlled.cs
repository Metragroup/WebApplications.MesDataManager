namespace MesDataManager.Domain.Abstractions;

/// <summary>
/// Entita' allineata da un sistema esterno (ERP): la colonna IsActive_Master dice se la voce
/// e' abilitata a monte. Lo stabilimento puo' solo disattivare localmente cio' che il master
/// ha abilitato, mai il contrario: IsActive puo' passare a true solo se IsActiveMaster e' true.
/// </summary>
public interface IMasterControlled
{
    bool IsActiveMaster { get; set; }
}
