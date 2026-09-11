using MesDataManager.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace MesDataManager.Infrastructure.Production;

/// <summary>
/// <see cref="BatchService"/>, l'unita' transazionale condivisa da salvataggio, creazione ed
/// eliminazione.
/// </summary>
public sealed partial class BatchService
{
    /// <summary>
    /// Esegue <paramref name="work"/> dentro una transazione sola, come unita' ritentabile.
    /// <para>
    /// La connessione e' configurata con <c>EnableRetryOnFailure</c>, e
    /// <c>SqlServerRetryingExecutionStrategy</c> rifiuta le transazioni aperte a mano: senza
    /// questo involucro qualunque <c>BeginTransaction</c> fallisce subito, com'e' successo al
    /// salvataggio del lotto l'11 settembre 2026. La transazione va quindi affidata alla
    /// strategia, che sa ripetere l'unita' intera quando l'errore e' passeggero.
    /// </para>
    /// <para>
    /// Il contesto nasce <b>dentro</b> l'unita' e non fuori: un secondo tentativo deve ripartire
    /// da dati riletti e da un tracciamento pulito, altrimenti riscriverebbe le modifiche
    /// calcolate sullo stato del tentativo fallito. Per lo stesso motivo <paramref name="work"/>
    /// deve contenere tutto cio' che la transazione comprende — le letture di controllo comprese
    /// — e lavorare solo sul contesto che riceve.
    /// </para>
    /// <para>
    /// Se il contesto arriva con una transazione gia' aperta — e' il caso delle prove sul
    /// database vero, che girano dentro una transazione poi annullata — ci si aggancia invece di
    /// aprirne un'altra, che SQL Server rifiuterebbe, e il commit spetta a chi l'ha aperta.
    /// <b>Quelle prove vanno configurate senza <c>EnableRetryOnFailure</c></b>: con la strategia
    /// di ripetizione accesa, un contesto che porta gia' la transazione di qualcun altro rifiuta
    /// qualunque operazione, e la sonda fallirebbe per una ragione che in esercizio non esiste —
    /// l'applicazione la sua transazione se la apre da se'.
    /// </para>
    /// </summary>
    private async Task<TResult> InTransactionAsync<TResult>(
        Func<MesDbContext, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken)
    {
        // Un contesto al solo scopo di avere la strategia: crearlo non apre connessioni, e quello
        // su cui l'unita' lavora deve nascere a ogni tentativo.
        await using var probe = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await probe.Database
            .CreateExecutionStrategy()
            .ExecuteAsync(
                async ct =>
                {
                    await using var context = await contextFactory
                        .CreateDbContextAsync(ct)
                        .ConfigureAwait(false);

                    await using var transaction = context.Database.CurrentTransaction is null
                        ? await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
                        : null;

                    var result = await work(context, ct).ConfigureAwait(false);

                    if (transaction is not null)
                    {
                        await transaction.CommitAsync(ct).ConfigureAwait(false);
                    }

                    return result;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Come sopra, per un'unita' che non restituisce niente.</summary>
    private Task InTransactionAsync(
        Func<MesDbContext, CancellationToken, Task> work,
        CancellationToken cancellationToken) =>
        InTransactionAsync<object?>(
            async (context, ct) =>
            {
                await work(context, ct).ConfigureAwait(false);
                return null;
            },
            cancellationToken);
}
