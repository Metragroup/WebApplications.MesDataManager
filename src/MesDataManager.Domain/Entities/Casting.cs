namespace MesDataManager.Domain.Entities;

/// <summary>
/// Colata di alluminio (vista <c>MetraPQ.Casting</c>).
/// <para>
/// Il codice che l'operatore digita sulla billetta e' <see cref="Description"/>, non
/// <see cref="CastingId"/>: e' su quello che il vecchio applicativo verificava l'esistenza, e da
/// esso ricavava la lega — che quindi <b>non</b> si imposta a mano.
/// </para>
/// </summary>
public sealed class Casting
{
    public int CastingId { get; set; }

    /// <summary>Codice della colata, quello digitato sulla billetta.</summary>
    public string Description { get; set; } = null!;

    public string? AlloyId { get; set; }

    /// <summary>Data della colata: serve a ordinare quando lo stesso codice compare su piu' righe.</summary>
    public DateTime? CastingDate { get; set; }
}
