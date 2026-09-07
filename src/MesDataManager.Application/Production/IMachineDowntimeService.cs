namespace MesDataManager.Application.Production;

/// <summary>
/// Unico punto di accesso ai fermi macchina. Gli elenchi dei filtri (Pressa, Tipo, Causale) non
/// passano da qui: arrivano da <see cref="MesDataManager.Application.Lookups.ILookupProvider"/>,
/// lo stesso meccanismo gia' usato dagli editor delle anagrafiche.
/// </summary>
public interface IMachineDowntimeService
{
    Task<MachineDowntimePage> GetPageAsync(
        MachineDowntimeQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Macrofermi che si sovrappongono alla finestra di un lotto, per la scheda del lotto.
    /// <para>
    /// Solo macrofermi, come nel vecchio applicativo: i microfermi di un turno sono centinaia e
    /// la scheda diventerebbe illeggibile. Non si applica il periodo massimo di
    /// <see cref="MachineDowntimePeriodPolicy"/>: qui la finestra la impone il lotto, non
    /// l'operatore.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<MachineDowntimeRow>> GetForBatchAsync(
        string pressId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);

    /// <summary>Inserisce se <see cref="MachineDowntimeEditModel.Id"/> e' nullo, altrimenti modifica.</summary>
    Task SaveAsync(
        MachineDowntimeEditModel model,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
