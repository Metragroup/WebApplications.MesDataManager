namespace MesDataManager.Domain.Abstractions;

/// <summary>
/// Entita' con disattivazione logica. Le anagrafiche non cancellano mai fisicamente
/// le voci gia' usate dalla produzione: le disattivano, cosi' i dati storici restano leggibili.
/// </summary>
public interface IActivatable
{
    bool IsActive { get; set; }
}
