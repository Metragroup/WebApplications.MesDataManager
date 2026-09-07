using Microsoft.Data.SqlClient;

namespace MesDataManager.Web;

/// <summary>
/// Server e database su cui l'applicazione sta lavorando, mostrati in fondo al menu.
/// <para>
/// Serve perche' l'ambiente di prova e quello di esercizio si assomigliano: sapere a colpo
/// d'occhio dove si sta scrivendo evita di correggere i dati sbagliati. Della stringa di
/// connessione si prendono solo questi due valori e mai altro — nessuna credenziale finisce a
/// schermo, anche se un domani la connessione passasse da utente e password invece che
/// dall'autenticazione integrata.
/// </para>
/// </summary>
public sealed record DatabaseIdentity(string Server, string Database)
{
    /// <summary>Cio' che si mostra quando la stringa di connessione manca o non si legge.</summary>
    public static readonly DatabaseIdentity Unknown = new(Placeholder, Placeholder);

    private const string Placeholder = "—";

    public static DatabaseIdentity FromConfiguration(IConfiguration configuration) =>
        From(configuration.GetConnectionString("MesDatabase"));

    /// <summary>
    /// Legge server e database dalla stringa di connessione. Una stringa illeggibile non e' un
    /// motivo per far cadere la pagina: e' un'informazione di cortesia, e in quel caso resta il
    /// segnaposto. Che la connessione funzioni si scopre alla prima query, con un errore vero.
    /// </summary>
    public static DatabaseIdentity From(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Unknown;
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);

            return new DatabaseIdentity(Text(builder.DataSource), Text(builder.InitialCatalog));
        }
        catch (ArgumentException)
        {
            return Unknown;
        }
    }

    private static string Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Placeholder : value.Trim();
}
