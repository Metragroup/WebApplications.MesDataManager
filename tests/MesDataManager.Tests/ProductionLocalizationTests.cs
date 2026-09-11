using System.Globalization;
using System.Reflection;
using System.Resources;

using MesDataManager.Application.Production;

namespace MesDataManager.Tests;

/// <summary>
/// Traduzione dei messaggi dell'area produzione, con lo stesso meccanismo usato per
/// <c>ArchiveException</c>: le factory statiche sono l'unico modo previsto per costruire un
/// errore, quindi percorrerle copre tutti i messaggi che la pagina Fermi puo' mostrare. Una
/// chiave assente da un resx non e' un errore di compilazione: all'operatore uscirebbe la chiave
/// grezza.
/// </summary>
public sealed class ProductionLocalizationTests
{
    private static readonly ResourceManager Resources = new(
        "MesDataManager.Web.Resources.Strings",
        typeof(MesDataManager.Web.Strings).Assembly);

    [Theory]
    [InlineData("it")]
    [InlineData("en")]
    [InlineData("fr")]
    public void Ogni_messaggio_di_ProductionException_e_tradotto(string culture)
    {
        var presenti = Chiavi(culture);

        var mancanti = MessaggiDiErrore()
            .Where(k => !presenti.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            mancanti.Count == 0,
            $"Chiavi assenti da Strings.{culture}.resx: {string.Join(", ", mancanti)}");
    }

    [Theory]
    [InlineData("it")]
    [InlineData("en")]
    [InlineData("fr")]
    public void Nessuna_traduzione_e_codificata_due_volte(string culture)
    {
        // Regressione: le chiavi aggiunte con uno script che leggeva l'ingresso come byte sono
        // finite nei resx codificate due volte, e all'operatore usciva "SÃ¬" al posto di "Si'".
        // La firma e' una A maiuscola con tilde davanti a un altro carattere: non compare in
        // nessuna parola italiana, inglese o francese.
        var rotte = Valori(culture)
            .Where(v => v.Contains('\u00C3'))
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            rotte.Count == 0,
            $"Valori codificati due volte in Strings.{culture}.resx: {string.Join(" | ", rotte)}");
    }

    private static IEnumerable<string> Valori(string culture)
    {
        var target = culture == "it" ? CultureInfo.InvariantCulture : new CultureInfo(culture);

        var set = Resources.GetResourceSet(target, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);

        return set.Cast<System.Collections.DictionaryEntry>()
            .Select(e => e.Value as string)
            .Where(v => v is not null)
            .ToList()!;
    }

    private static IEnumerable<string> MessaggiDiErrore()
    {
        var factory = typeof(ProductionException)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(ProductionException))
            .ToList();

        Assert.NotEmpty(factory);

        return factory
            .Select(m => m.Invoke(null, [.. m.GetParameters().Select(p => Segnaposto(p.ParameterType))]))
            .Cast<ProductionException>()
            .Select(e => e.MessageKey)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static object? Segnaposto(Type type) => type switch
    {
        _ when type == typeof(string) => "PressId",
        _ when type == typeof(int) => 0,
        _ when type == typeof(DateTime) => new DateTime(2026, 9, 1),
        _ when type == typeof(DateTime?) => new DateTime(2026, 9, 1),
        _ when type == typeof(IReadOnlyCollection<int>) => new[] { 1 },
        _ when type == typeof(IReadOnlyCollection<string>) => new[] { "MP1260901080000" },
        _ => null,
    };

    private static HashSet<string> Chiavi(string culture)
    {
        var target = culture == "it" ? CultureInfo.InvariantCulture : new CultureInfo(culture);

        var set = Resources.GetResourceSet(target, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);

        return set.Cast<System.Collections.DictionaryEntry>()
            .Select(e => (string)e.Key)
            .ToHashSet(StringComparer.Ordinal);
    }
}
