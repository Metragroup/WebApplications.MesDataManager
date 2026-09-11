using MesDataManager.Application.Production;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MesDataManager.Infrastructure.Production;

/// <summary>
/// Configurazione del servizio di diagnostica. L'indirizzo <b>non e' un segreto</b> e sta in
/// <c>appsettings</c>: e' un servizio di stabilimento raggiungibile solo dalla rete interna.
/// </summary>
public sealed class DiagnosticsOptions
{
    public const string SectionName = "Diagnostics";

    /// <summary>
    /// Indirizzo base, con la barra finale: <c>http://192.168.3.8:8000/api/v1/batches/</c>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Secondi concessi al servizio. Trenta, come nel vecchio applicativo.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Client HTTP del servizio di diagnostica.
/// <para>
/// Restituisce il testo della risposta <b>come arriva</b>: non lo deserializza e non lo
/// riserializza, perche' quel testo e' cio' che verra' conservato su <c>Batch.DiagnosticsMsg</c>
/// e riletto fra mesi. Chi lo interpreta e' <see cref="DiagnosticsReportReader"/>, a parte.
/// </para>
/// </summary>
public sealed class DiagnosticsClient(
    HttpClient httpClient,
    IOptions<DiagnosticsOptions> options,
    ILogger<DiagnosticsClient> logger) : IDiagnosticsClient
{
    public async Task<string> AnalyzeAsync(
        string batchId,
        bool ignoreManualAddedBillets,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            logger.LogError("Diagnostica: l'indirizzo del servizio non e' configurato.");
            throw ProductionException.DiagnosticsUnavailable();
        }

        var baseUrl = settings.BaseUrl.EndsWith('/') ? settings.BaseUrl : settings.BaseUrl + "/";
        var url = $"{baseUrl}{Uri.EscapeDataString(batchId)}/analyze" +
                  $"?ignoreManualAddedBillets={(ignoreManualAddedBillets ? "true" : "false")}";

        try
        {
            logger.LogInformation("Diagnostica: interrogazione di {Url}.", url);

            using var response = await httpClient
                .GetAsync(url, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var content = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(content))
            {
                throw ProductionException.DiagnosticsUnavailable();
            }

            return content;
        }
        catch (ProductionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Servizio spento, rete assente, timeout, risposta illeggibile: per chi ha premuto
            // il pulsante sono lo stesso fatto — la diagnostica non si e' potuta fare.
            logger.LogError(ex, "Diagnostica: interrogazione di {Url} non riuscita.", url);
            throw ProductionException.DiagnosticsUnavailable(ex);
        }
    }
}
