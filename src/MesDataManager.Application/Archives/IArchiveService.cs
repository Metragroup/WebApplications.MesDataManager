namespace MesDataManager.Application.Archives;

/// <summary>Elenco delle anagrafiche gestite dall'applicazione.</summary>
public interface IArchiveCatalog
{
    IReadOnlyList<ArchiveDescriptor> All { get; }

    /// <summary>Restituisce il descrittore, o <c>null</c> se la chiave non esiste.</summary>
    ArchiveDescriptor? Find(string key);
}

/// <summary>Criteri di lettura di un'anagrafica.</summary>
/// <param name="IncludeInactive">Include anche le voci disattivate.</param>
/// <param name="Search">Filtro testuale sui campi ricercabili del descrittore.</param>
public sealed record ArchiveQuery(bool IncludeInactive = false, string? Search = null);

/// <summary>
/// Unico punto di accesso ai dati delle anagrafiche. La UI non conosce ne' EF Core ne' le
/// entita': parla di chiavi di anagrafica e di <see cref="ArchiveRow"/>.
/// </summary>
public interface IArchiveService
{
    Task<IReadOnlyList<ArchiveRow>> GetRowsAsync(
        string archiveKey,
        ArchiveQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Riga precompilata con i default per un nuovo record.</summary>
    Task<ArchiveRow> CreateTemplateAsync(
        string archiveKey,
        CancellationToken cancellationToken = default);

    Task InsertAsync(
        string archiveKey,
        ArchiveRow row,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        string archiveKey,
        ArchiveRow row,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string archiveKey,
        ArchiveRow row,
        CancellationToken cancellationToken = default);
}
