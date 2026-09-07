namespace MesDataManager.Domain.Entities;

/// <summary>
/// Fermo macchina (Press.BatchDowntime). Dato transazionale, non un'anagrafica: nessuna delle
/// interfacce <c>IActivatable</c>/<c>IPositionable</c>/<c>IMasterControlled</c> si applica.
/// </summary>
public sealed class BatchDowntime
{
    public int BatchDowntimeId { get; set; }
    public int? BatchDowntimeRawId { get; set; }
    public string PressId { get; set; } = null!;
    public short DowntimeReasonId { get; set; }
    public string DowntimeCode { get; set; } = null!;
    public DateTime StartTs { get; set; }
    public DateTime StopTs { get; set; }

    /// <summary>
    /// Riferisce <see cref="PressDowntimeType.PressDowntimeTypeId"/> senza FK dichiarata: il
    /// tipo di colonna non coincide (tinyint contro smallint), stesso disallineamento gia'
    /// documentato per <see cref="FailureType"/>.
    /// </summary>
    public byte DowntimeType { get; set; }

    /// <summary>
    /// Riferisce PressFailureType.PressFailureTypeID senza FK dichiarata (tinyint contro
    /// smallint, docs/decisioni-aperte.md). Non e' esposta ne' in griglia ne' nel form: le
    /// inserzioni manuali del vecchio applicativo la lasciavano a zero.
    /// </summary>
    public byte FailureType { get; set; }

    public string? EditStatusId { get; set; }
}
