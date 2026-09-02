namespace MesDataManager.Application.Security;

/// <summary>
/// Ruoli applicativi. Vanno mappati sui ruoli dell'app registration di Entra ID
/// (claim "roles") e non su gruppi di dominio, per non dipendere dal tenant.
/// </summary>
public static class AppRoles
{
    /// <summary>Consultazione delle anagrafiche.</summary>
    public const string ArchiveReader = "Archive.Reader";

    /// <summary>Inserimento e modifica.</summary>
    public const string ArchiveEditor = "Archive.Editor";

    /// <summary>Eliminazione e operazioni sulle anagrafiche governate dall'ERP.</summary>
    public const string ArchiveAdministrator = "Archive.Administrator";
}

/// <summary>
/// Identita' e permessi dell'utente corrente. Sostituisce l'<c>AuthService</c> WinForms, che
/// restituiva permessi costanti; qui le risposte derivano dai claim del token Entra ID.
/// </summary>
public interface IUserContext
{
    string UserName { get; }

    string? DisplayName { get; }

    bool IsAuthenticated { get; }

    bool CanRead { get; }

    bool CanInsert { get; }

    bool CanUpdate { get; }

    bool CanDelete { get; }
}
