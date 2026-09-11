using MesDataManager.Application.Production;
using MesDataManager.Application.Security;
using MesDataManager.Domain.Entities;
using MesDataManager.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MesDataManager.Infrastructure.Production;

/// <summary>
/// Diagnostica di lotto: interroga il servizio, conserva la risposta e inserisce le billette che
/// il servizio ritiene mancanti.
/// <para>
/// La chiamata HTTP sta dietro <see cref="IDiagnosticsClient"/>, quindi tutto quello che questa
/// classe fa <b>con</b> la risposta si verifica senza rete.
/// </para>
/// </summary>
public sealed class BatchDiagnosticsService(
    IDbContextFactory<MesDbContext> contextFactory,
    IUserContext user,
    IDiagnosticsClient client,
    ILogger<BatchDiagnosticsService> logger) : IBatchDiagnosticsService
{
    public async Task<BatchDiagnosticsOutcome> RunAsync(
        string batchId,
        bool ignoreManualAddedBillets = false,
        CancellationToken cancellationToken = default)
    {
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);

        // La diagnostica scrive su Batch: non la esegue chi consulta soltanto. Nel vecchio
        // applicativo non c'era alcun controllo, ma non esisteva nemmeno un ruolo di sola lettura.
        if (!permissions.CanEditProduction)
        {
            throw ProductionException.Forbidden();
        }

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var batch = await context.Batches
            .SingleOrDefaultAsync(b => b.BatchId == batchId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw ProductionException.NotFound();

        // Su un lotto in modifica la diagnostica la esegue solo chi lo sta modificando: altrimenti
        // due persone scriverebbero lo stesso campo, e chi modifica vedrebbe cambiare l'esito
        // sotto le mani.
        if (batch.IsLock && batch.LockUsr != permissions.UserName)
        {
            throw ProductionException.BatchLocked(batch.LockUsr, batch.LockTs);
        }

        var content = await client
            .AnalyzeAsync(batchId, ignoreManualAddedBillets, cancellationToken)
            .ConfigureAwait(false);

        var report = DiagnosticsReportReader.TryRead(content)
            ?? throw ProductionException.DiagnosticsUnavailable();

        var status = Status(report);
        var now = DateTime.Now;

        // Si scrive **solo** il gruppo UsrDiag*, cioe' la diagnostica chiesta da un utente.
        //
        // I campi SvcDiag* restano intatti anche quando sono vuoti, ed e' il punto: un lotto
        // senza esito del servizio e' un buco nel calcolo automatico, e riempirlo con l'esito di
        // una esecuzione chiesta a mano cancellerebbe proprio l'informazione che serve a
        // trovarlo. Fino all'11 settembre 2026 questa applicazione li compilava "se vuoti", come
        // rete di sicurezza: quella rete nascondeva il problema invece di segnalarlo.
        batch.UsrDiagStatus = status;
        batch.UsrDiagTs = now;

        // Messaggio e risposta si conservano separati: il referto leggibile arriva dal servizio
        // (diagnostics.message) e non si compone piu' qui, la risposta si conserva intera e
        // verbatim — riserializzarla significherebbe rileggere fra mesi una cosa diversa da
        // quella che il servizio ha detto.
        batch.UsrDiagMsg = Truncate(report.Message);
        batch.UsrDiagJson = content;

        var inserted = await UpsertMissingBilletsAsync(context, batch, content, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Diagnostica: salvataggio dell'esito del lotto {Batch} non riuscito.", batchId);
            throw ProductionException.SaveFailed(ex);
        }

        logger.LogInformation(
            "Diagnostica: lotto {Batch} esito {Status} da parte di {User}, {Inserted} billette inserite.",
            batchId,
            status,
            permissions.UserName,
            inserted);

        return new BatchDiagnosticsOutcome(status, report, inserted);
    }

    /// <summary>
    /// Traduce l'esito del servizio nei tre valori del campo di lotto, come
    /// <c>BatchesPresenter.ProcessDiagnosticResult</c>: errore se il servizio dice errore, avviso
    /// se ci sono avvisi, altrimenti tutto bene.
    /// </summary>
    private static string Status(DiagnosticsReport report) => report switch
    {
        { Status: DiagnosticsOutcome.Error } => DiagnosticsOutcome.Error,
        { Failed: > 0 } => DiagnosticsOutcome.Error,
        { Warnings: > 0 } => DiagnosticsOutcome.Warning,
        _ => DiagnosticsOutcome.Ok,
    };

    /// <summary>
    /// Tronca il referto alla lunghezza della colonna. Un lotto con molti errori ha un messaggio
    /// lungo quanto l'elenco degli errori, e sarebbe il salvataggio a fallire — proprio sul lotto
    /// messo peggio. Il testo intero resta comunque nel JSON accanto.
    /// </summary>
    private static string? Truncate(string? message) =>
        message is { Length: > MesDbContext.DiagnosticsMessageMaxLength }
            ? message[..MesDbContext.DiagnosticsMessageMaxLength]
            : message;

    /// <summary>
    /// Inserisce o aggiorna le billette che il servizio ritiene mancanti
    /// (<c>RepositoryService.UpsertMissingBillets</c>).
    /// <para>
    /// La regola che conta e' la seconda: una billetta che esiste con uno stato di modifica
    /// diverso da <c>A</c> <b>non si tocca</b>. Significa che qualcuno l'ha inserita o corretta a
    /// mano, e il giudizio di una persona batte quello del servizio.
    /// </para>
    /// <para>
    /// I valori arrivano dall'oggetto <c>record</c> della risposta, che porta i nomi delle colonne
    /// di <c>Press.BatchBillet</c>: si leggono da la' e non dal riassunto strutturato, cosi' un
    /// campo in piu' non richiede di toccare il lettore.
    /// </para>
    /// </summary>
    private static async Task<int> UpsertMissingBilletsAsync(
        MesDbContext context,
        Batch batch,
        string content,
        CancellationToken cancellationToken)
    {
        var records = MissingBilletRecordReader.Read(content);
        if (records.Count == 0)
        {
            return 0;
        }

        var existing = await context.BatchBillets
            .Where(b => b.BatchId == batch.BatchId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var affected = 0;

        foreach (var record in records)
        {
            var billet = existing.FirstOrDefault(b => b.BilletNo == record.BilletNo);

            if (billet is not null)
            {
                if (billet.EditStatusId?.Trim() != "A")
                {
                    continue;
                }
            }
            else
            {
                billet = new BatchBillet
                {
                    BatchId = batch.BatchId,
                    PressId = batch.PressId,
                    DieId = batch.DieId ?? string.Empty,
                    TypeId = BatchBilletType.Real,
                };

                context.BatchBillets.Add(billet);
                existing.Add(billet);
            }

            record.ApplyTo(billet);
            affected++;
        }

        return affected;
    }
}
