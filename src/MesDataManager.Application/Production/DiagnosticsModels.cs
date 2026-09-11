using System.Text.Json;

namespace MesDataManager.Application.Production;

/// <summary>
/// La risposta del servizio di diagnostica, letta in forma strutturata.
/// <para>
/// Non e' il contratto del servizio: e' cio' che serve alla scheda del lotto. Il contratto vero
/// resta il JSON, che si conserva <b>intero e verbatim</b> in <c>Batch.DiagnosticsMsg</c> —
/// questo record e' una lettura di quel testo, e se il servizio aggiunge campi il testo li porta
/// comunque.
/// </para>
/// </summary>
/// <param name="Message">
/// Referto leggibile, dal campo <c>diagnostics.message</c> della risposta. Lo compone il
/// servizio, non questa applicazione: dall'11 settembre 2026 messaggio e risposta si conservano
/// in due colonne separate, e il messaggio non si ricostruisce piu' dagli elenchi di errori e
/// avvisi.
/// </param>
public sealed record DiagnosticsReport(
    string? Status,
    string? Message,
    int Total,
    int Passed,
    int Warnings,
    int Failed,
    IReadOnlyList<DiagnosticsCheck> Checks,
    IReadOnlyList<string> ErrorMessages,
    IReadOnlyList<string> WarningMessages,
    IReadOnlyList<MissingBilletReport> MissingBillets);

/// <summary>Un controllo eseguito dal servizio, col suo esito.</summary>
public sealed record DiagnosticsCheck(string Id, string? Description, string? Outcome, string? Detail);

/// <summary>
/// Una billetta che il servizio ritiene mancante, con il motivo per cui lo ritiene.
/// <para>
/// E' il dettaglio che la scheda mostra cliccando la billetta marcata <c>A</c>: senza il
/// motivo, una billetta comparsa dal nulla e' solo un dato in piu' di cui non fidarsi.
/// </para>
/// </summary>
public sealed record MissingBilletReport(
    short BilletNo,
    string? Decision,
    decimal? ConfidenceScore,
    IReadOnlyList<string> ReasonCodes,
    string? GapDetail);

/// <summary>
/// Legge la risposta del servizio di diagnostica.
/// <para>
/// Restituisce <c>null</c> su un contenuto che JSON non e': sono le 232 mila righe storiche di
/// <c>DiagnosticsMsg</c>, che contengono il referto testuale del vecchio applicativo. La scheda
/// in quel caso mostra il testo cosi' com'e', e non un errore.
/// </para>
/// </summary>
public static class DiagnosticsReportReader
{
    public static DiagnosticsReport? TryRead(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var text = content.TrimStart();
        if (!text.StartsWith('{'))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            if (!root.TryGetProperty("diagnostics", out var diagnostics))
            {
                return null;
            }

            var summary = diagnostics.TryGetProperty("summary", out var s) ? s : default;

            return new DiagnosticsReport(
                Status: String(diagnostics, "status")?.Trim().ToUpperInvariant(),

                // Il referto leggibile arriva dal servizio. Le risposte precedenti all'11
                // settembre 2026 non lo hanno: la' resta nullo, e la scheda mostra gli elenchi
                // di errori e avvisi.
                Message: String(diagnostics, "message"),
                Total: Int(summary, "total"),
                Passed: Int(summary, "passed"),
                Warnings: Int(summary, "warnings"),
                Failed: Int(summary, "failed"),
                Checks: Checks(diagnostics),
                ErrorMessages: Messages(diagnostics, "errors"),
                WarningMessages: Messages(diagnostics, "warnings"),
                MissingBillets: MissingBillets(root));
        }
        catch (JsonException)
        {
            // Un JSON malformato non e' un caso da gestire con un errore: si ricade sul testo,
            // come per i referti storici.
            return null;
        }
    }

    private static IReadOnlyList<DiagnosticsCheck> Checks(JsonElement diagnostics)
    {
        if (!diagnostics.TryGetProperty("checks", out var checks) ||
            checks.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. checks.EnumerateArray().Select(c => new DiagnosticsCheck(
            String(c, "id") ?? string.Empty,
            String(c, "description"),
            String(c, "outcome"),
            String(c, "detail")))];
    }

    private static IReadOnlyList<string> Messages(JsonElement diagnostics, string property)
    {
        if (!diagnostics.TryGetProperty(property, out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. array.EnumerateArray()
            .Select(m => String(m, "message"))
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m!)];
    }

    private static IReadOnlyList<MissingBilletReport> MissingBillets(JsonElement root)
    {
        if (!root.TryGetProperty("missingBillets", out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. array.EnumerateArray().Select(item =>
        {
            var record = item.TryGetProperty("record", out var r) ? r : default;
            var gap = item.TryGetProperty("gap", out var g) ? g : default;

            return new MissingBilletReport(
                BilletNo: (short)Int(record, "BilletNo"),
                Decision: String(item, "decision"),
                ConfidenceScore: Decimal(item, "confidenceScore"),
                ReasonCodes: Strings(item, "reasonCodes"),
                GapDetail: GapDetail(gap));
        })];
    }

    /// <summary>
    /// Il divario che ha fatto sospettare la billetta mancante, in una riga leggibile: quanti
    /// secondi di vuoto, quanti se ne aspettavano, quante billette ci starebbero.
    /// </summary>
    private static string? GapDetail(JsonElement gap)
    {
        if (gap.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var parts = new List<string>();

        if (Decimal(gap, "netGapSec") is { } net)
        {
            parts.Add($"vuoto {net:N0} s");
        }

        if (Decimal(gap, "expectedDurationSec") is { } expected)
        {
            parts.Add($"attesi {expected:N0} s");
        }

        if (gap.TryGetProperty("billetsInGap", out var count) && count.ValueKind == JsonValueKind.Number)
        {
            parts.Add($"{count.GetInt32()} billette nel vuoto");
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string? String(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> Strings(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var array) &&
        array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString()!)]
            : [];

    private static int Int(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number
            ? value.TryGetInt32(out var number) ? number : 0
            : 0;

    private static decimal? Decimal(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDecimal(out var number)
            ? number
            : null;
}
