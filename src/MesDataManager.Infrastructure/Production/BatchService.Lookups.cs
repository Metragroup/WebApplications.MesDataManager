using MesDataManager.Application.Production;
using MesDataManager.Application.Security;
using MesDataManager.Domain.Entities;
using MesDataManager.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MesDataManager.Infrastructure.Production;

/// <summary>
/// <see cref="BatchService"/>, i controlli che precedono una modifica: esistenza e stato
/// d'uso di una matrice, colata con la sua lega, esistenza di un ordine di produzione.
/// <para>
/// Non scrivono niente: servono a impedire che un codice inventato entri fra le modifiche in
/// sospeso.
/// </para>
/// </summary>
public sealed partial class BatchService
{
    public async Task<DieValidation> ValidateDieAsync(
        string dieCode,
        short? dieNumber,
        CancellationToken cancellationToken = default)
    {
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!permissions.CanEditProduction)
        {
            throw ProductionException.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(dieCode))
        {
            throw ProductionException.Required(nameof(Batch.DieCode));
        }

        var code = dieCode.Trim().ToUpperInvariant();
        var dieId = DieValidation.ComposeDieId(code, dieNumber);

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var exists = await context.Dies
            .AsNoTracking()
            .AnyAsync(d => d.DieId == dieId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            return new DieValidation(dieId, code, dieNumber, DieCheck.NotFound);
        }

        // Lo stato d'uso sta su una vista diversa, e puo' mancare: un centro di lavoro senza riga
        // di setup esiste ma non dichiara nulla.
        var statusUse = await context.DieSetups
            .AsNoTracking()
            .Where(d => d.DieId == dieId)
            .Select(d => (int?)d.StatusUse)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var outcome = statusUse switch
        {
            null => DieCheck.Unknown,
            DieUseStatus.Test => DieCheck.Test,
            DieUseStatus.Available => DieCheck.Available,
            DieUseStatus.Stored => DieCheck.Stored,
            DieUseStatus.Deleted => DieCheck.Deleted,
            DieUseStatus.Transferred => DieCheck.Transferred,

            // Un valore che l'anagrafica dell'ERP non prevede: si assegna con avviso invece di
            // bloccare, perche' il divieto va motivato e qui non si saprebbe con cosa.
            _ => DieCheck.Unknown,
        };

        return new DieValidation(dieId, code, dieNumber, outcome);
    }

    public async Task<CastingLookup?> FindCastingAsync(
        string castingCode,
        CancellationToken cancellationToken = default)
    {
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!permissions.CanEditProduction)
        {
            throw ProductionException.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(castingCode))
        {
            return null;
        }

        var code = castingCode.Trim().ToUpperInvariant();

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Il codice colata non e' unico: si prende la piu' recente, con l'id come secondo
        // criterio perche' la data puo' mancare. Il vecchio applicativo usava un FirstOrDefault
        // senza ordinamento, che a parita' di codice poteva restituire leghe diverse.
        return await context.Castings
            .AsNoTracking()
            .Where(c => c.Description == code)
            .OrderByDescending(c => c.CastingDate)
            .ThenByDescending(c => c.CastingId)
            .Select(c => new CastingLookup(c.Description.TrimEnd(), c.AlloyId))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> ProdOrderExistsAsync(
        string prodId,
        CancellationToken cancellationToken = default)
    {
        var permissions = await user.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!permissions.CanEditProduction)
        {
            throw ProductionException.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(prodId))
        {
            return false;
        }

        var code = prodId.Trim().ToUpperInvariant();

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await context.ProductionTags
            .AsNoTracking()
            .AnyAsync(t => t.ProdId == code, cancellationToken)
            .ConfigureAwait(false);
    }
}
