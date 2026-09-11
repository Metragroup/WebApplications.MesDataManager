using System.Text.Json;

using MesDataManager.Domain.Entities;

namespace MesDataManager.Infrastructure.Production;

/// <summary>
/// Legge dalla risposta della diagnostica i record delle billette mancanti, cioe' l'oggetto
/// <c>record</c> di ogni elemento di <c>missingBillets</c>.
/// <para>
/// Sta nell'infrastruttura e non nell'applicazione perche' quei campi <b>sono</b> le colonne di
/// <c>Press.BatchBillet</c>: il servizio le nomina come il database, non come il dominio, ed e'
/// giusto che la traduzione stia accanto alla mappatura.
/// </para>
/// </summary>
internal static class MissingBilletRecordReader
{
    public static IReadOnlyList<MissingBilletRecord> Read(string content)
    {
        if (string.IsNullOrWhiteSpace(content) || !content.TrimStart().StartsWith('{'))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(content);

            if (!document.RootElement.TryGetProperty("missingBillets", out var array) ||
                array.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return [.. array.EnumerateArray()
                .Where(item => item.TryGetProperty("record", out var record) &&
                               record.ValueKind == JsonValueKind.Object)
                .Select(item => MissingBilletRecord.From(item.GetProperty("record")))
                .Where(record => record.BilletNo > 0)];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>
/// Una billetta mancante come la descrive il servizio, con i nomi delle colonne di
/// <c>Press.BatchBillet</c>.
/// </summary>
internal sealed record MissingBilletRecord(
    short BilletNo,
    DateTime? StartTs,
    DateTime? StopTs,
    decimal? MmBarSet,
    int? MmBilletAct,
    decimal? KgSheared,
    decimal? KgExtruded,
    string? Billet1AlloyId,
    string? Billet1CastingId,
    string? ProdId,
    int SecCycle,
    int? SecExtrusion,
    int BatchBilletRawId)
{
    public static MissingBilletRecord From(JsonElement record) =>
        new(
            BilletNo: (short)(Int(record, "BilletNo") ?? 0),
            StartTs: Date(record, "StartTs"),
            StopTs: Date(record, "StopTs"),
            MmBarSet: Number(record, "MmBarSet"),
            MmBilletAct: MilliMetres(record, "MmBilletAct"),
            KgSheared: Number(record, "KgSheared"),
            KgExtruded: Number(record, "KgExtruded"),
            Billet1AlloyId: Text(record, "Billet1_AlloyID"),
            Billet1CastingId: Text(record, "Billet1_CastingID"),
            ProdId: Text(record, "ProdID"),
            SecCycle: Int(record, "SecCycle") ?? 0,
            SecExtrusion: Int(record, "SecExtrusion"),

            // -1 e' il valore con cui il MES distingue una billetta che non viene dalla raccolta
            // dati. Il servizio lo dichiara; se non lo facesse, valdrebbe comunque -1.
            BatchBilletRawId: Int(record, "BatchBilletRawID") ?? -1);

    /// <summary>
    /// Riporta i valori sulla billetta, marcandola <c>A</c>: e' quello stato che dice "ce l'ha
    /// messa la diagnostica", e che impedisce alla diagnostica successiva di sovrascrivere una
    /// correzione fatta a mano.
    /// </summary>
    public void ApplyTo(BatchBillet billet)
    {
        billet.BilletNo = BilletNo;
        billet.StartTs = StartTs;
        billet.StopTs = StopTs;
        billet.MmBarSet = MmBarSet;
        billet.MmBilletAct = MmBilletAct;
        billet.KgSheared = KgSheared;
        billet.KgExtruded = KgExtruded;
        billet.Billet1AlloyId = Billet1AlloyId;
        billet.Billet1CastingId = Billet1CastingId;
        billet.ProdId = ProdId;
        billet.SecCycle = SecCycle;
        billet.SecExtrusion = SecExtrusion;
        billet.BatchBilletRawId = BatchBilletRawId;
        billet.EditStatusId = "A";
    }

    private static string? Text(JsonElement record, string property) =>
        record.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.TrimEnd()
            : null;

    private static int? Int(JsonElement record, string property) =>
        record.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDecimal(out var number)
            ? (int)Math.Round(number)
            : null;

    private static decimal? Number(JsonElement record, string property) =>
        record.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDecimal(out var number)
            ? number
            : null;

    /// <summary>
    /// La lunghezza billetta e' un intero a database ma il servizio la manda con i decimali
    /// (<c>680.0</c>): si arrotonda, come faceva <c>MapMissingBilletRecord</c>.
    /// </summary>
    private static int? MilliMetres(JsonElement record, string property) =>
        Number(record, property) is { } value ? (int)Math.Round(value) : null;

    private static DateTime? Date(JsonElement record, string property) =>
        record.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        DateTime.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
}
