using System.Globalization;
using System.Reflection;
using System.Resources;

using MesDataManager.Application.Archives;
using MesDataManager.Domain.Abstractions;
using MesDataManager.Infrastructure.Archives;

namespace MesDataManager.Tests;

/// <summary>
/// Coerenza del catalogo. Il CRUD generico legge i campi per riflessione: un nome sbagliato non
/// e' un errore di compilazione, si manifesta come colonna vuota o etichetta non tradotta al
/// primo utilizzo. Questi test riportano quegli errori al momento della build.
/// </summary>
public sealed class ArchiveCatalogTests
{
    private static readonly IArchiveCatalog Catalog = new ArchiveCatalog();

    private static readonly ResourceManager Resources = new(
        "MesDataManager.Web.Resources.Strings",
        typeof(MesDataManager.Web.Strings).Assembly);

    public static TheoryData<string> Archives()
    {
        var data = new TheoryData<string>();
        foreach (var descriptor in Catalog.All)
        {
            data.Add(descriptor.Key);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Archives))]
    public void Ogni_campo_dichiarato_esiste_sull_entita(string key)
    {
        var descriptor = Resolve(key);

        var mancanti = descriptor.Fields
            .Where(f => descriptor.EntityType.GetProperty(f.Name) is null)
            .Select(f => f.Name)
            .ToList();

        Assert.Empty(mancanti);
    }

    [Theory]
    [MemberData(nameof(Archives))]
    public void Ordinamento_e_ricerca_puntano_a_campi_esistenti(string key)
    {
        var descriptor = Resolve(key);

        Assert.Empty(descriptor.DefaultSort
            .Where(n => descriptor.EntityType.GetProperty(n) is null)
            .ToList());

        Assert.Empty(descriptor.SearchableFields
            .Where(n => descriptor.EntityType.GetProperty(n) is null)
            .ToList());
    }

    [Theory]
    [MemberData(nameof(Archives))]
    public void La_ricerca_agisce_solo_su_campi_testuali(string key)
    {
        // ArchiveQueryBuilder salta in silenzio i campi non testuali: dichiararne uno
        // significa credere di poterlo cercare e non poterlo fare.
        var descriptor = Resolve(key);

        Assert.Empty(descriptor.SearchableFields
            .Where(n => descriptor.EntityType.GetProperty(n)?.PropertyType != typeof(string))
            .ToList());
    }

    [Theory]
    [MemberData(nameof(Archives))]
    public void Il_filtro_sulle_voci_attive_e_dichiarato_solo_dove_esiste_la_colonna(string key)
    {
        var descriptor = Resolve(key);

        if (descriptor.SupportsActiveFilter)
        {
            Assert.True(typeof(IActivatable).IsAssignableFrom(descriptor.EntityType));
        }
    }

    [Theory]
    [MemberData(nameof(Archives))]
    public void Il_flag_del_master_e_dichiarato_solo_dove_l_entita_lo_prevede(string key)
    {
        var descriptor = Resolve(key);

        if (descriptor.HasMasterFlag)
        {
            Assert.True(typeof(IMasterControlled).IsAssignableFrom(descriptor.EntityType));
        }
    }

    [Theory]
    [MemberData(nameof(Archives))]
    public void Ogni_anagrafica_ha_una_chiave(string key)
    {
        Assert.NotEmpty(Resolve(key).KeyFields);
    }

    [Theory]
    [MemberData(nameof(Archives))]
    public void Ogni_campo_di_tipo_lookup_dichiara_l_elenco_da_interrogare(string key)
    {
        var descriptor = Resolve(key);

        Assert.Empty(descriptor.Fields
            .Where(f => f.Kind is ArchiveFieldKind.Lookup && string.IsNullOrEmpty(f.LookupKey))
            .Select(f => f.Name));
    }

    [Theory]
    [MemberData(nameof(Archives))]
    public void Le_tabelle_allineate_dall_ERP_hanno_posizione_e_attivazione(string key)
    {
        var descriptor = Resolve(key);

        if (descriptor.EditPolicy is not ArchiveEditPolicy.MasterControlled)
        {
            return;
        }

        // Senza questi due campi la policy non lascerebbe modificare nulla.
        Assert.Contains(descriptor.Fields, f => f.Name == nameof(IPositionable.Position));
        Assert.Contains(descriptor.Fields, f => f.Name == nameof(IActivatable.IsActive));
    }

    [Theory]
    [MemberData(nameof(Archives))]
    public void Il_divieto_di_eliminazione_si_dichiara_solo_dove_avrebbe_effetto(string key)
    {
        var descriptor = Resolve(key);

        // Su una policy che non prevede l'eliminazione il flag non cambia niente: dichiararlo
        // farebbe credere che stia proteggendo qualcosa.
        if (descriptor.PreventDelete)
        {
            Assert.Equal(ArchiveEditPolicy.Full, descriptor.EditPolicy);
        }
    }

    [Fact]
    public void Le_chiavi_delle_anagrafiche_sono_distinte()
    {
        var duplicate = Catalog.All
            .GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        Assert.Empty(duplicate);
    }

    [Theory]
    [InlineData("it")]
    [InlineData("en")]
    [InlineData("fr")]
    public void Ogni_etichetta_del_catalogo_e_tradotta(string culture)
    {
        // Regressione: i resx portavano le chiavi con i nomi fisici delle colonne (OprID,
        // CompanyID) mentre il catalogo le risolve dai nomi delle proprieta' C# (OprId,
        // CompanyId). Il localizzatore distingue maiuscole e minuscole, quindi in griglia
        // usciva la chiave grezza al posto dell'etichetta.
        var presenti = Chiavi(culture);

        var mancanti = Catalog.All
            .Select(d => d.NameKey)
            .Concat(Catalog.All.SelectMany(d => d.Fields).Select(f => f.ResolvedLabelKey))
            .Distinct(StringComparer.Ordinal)
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
    public void Ogni_messaggio_di_ArchiveException_e_tradotto(string culture)
    {
        // Regressione: "Error.RecordNotFound" e "Error.ArchiveNotFound" erano citate dal codice
        // ma non esistevano in nessun resx, e all'operatore usciva la chiave grezza. Il test
        // precedente non le vedeva perche' guarda solo le chiavi del catalogo.
        var presenti = Chiavi(culture);

        var mancanti = MessaggiDiErrore()
            .Where(k => !presenti.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            mancanti.Count == 0,
            $"Chiavi assenti da Strings.{culture}.resx: {string.Join(", ", mancanti)}");
    }

    [Fact]
    public void Le_tre_lingue_hanno_le_stesse_chiavi()
    {
        // Una traduzione aggiunta in una lingua sola non e' un errore di compilazione: la voce
        // ricade sulla lingua neutra e passa inosservata fino a quando non la nota un utente.
        var neutra = Chiavi("it");

        foreach (var culture in new[] { "en", "fr" })
        {
            var tradotte = Chiavi(culture);

            Assert.True(
                neutra.SetEquals(tradotte),
                $"Strings.{culture}.resx: mancano [{string.Join(", ", neutra.Except(tradotte).Order(StringComparer.Ordinal))}], "
                + $"in piu' [{string.Join(", ", tradotte.Except(neutra).Order(StringComparer.Ordinal))}]");
        }
    }

    [Fact]
    public void Ogni_chiave_di_risorsa_appartiene_a_un_gruppo_noto()
    {
        // I resx tengono insieme testi di natura diversa: il prefisso dice a cosa serve una
        // voce senza doverla cercare nel codice. Una chiave senza prefisso e' una voce che
        // nessuno ritrovera' piu'.
        string[] gruppi = ["Archive.", "Field.", "Nav.", "Action.", "Msg.", "Error.", "App."];

        var fuori = Chiavi("it")
            .Where(k => !gruppi.Any(g => k.StartsWith(g, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(fuori.Count == 0, $"Chiavi senza gruppo: {string.Join(", ", fuori)}");
    }

    /// <summary>
    /// Le chiavi dei messaggi d'errore, prese dalle factory di <see cref="ArchiveException"/>:
    /// sono l'unico modo previsto per costruire un errore, quindi percorrerle copre tutti i
    /// messaggi che l'applicazione puo' mostrare. Gli argomenti passati sono indifferenti,
    /// del risultato interessa solo <see cref="ArchiveException.MessageKey"/>.
    /// </summary>
    private static IEnumerable<string> MessaggiDiErrore()
    {
        var factory = typeof(ArchiveException)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(ArchiveException))
            .ToList();

        Assert.NotEmpty(factory);

        return factory
            .Select(m => m.Invoke(null, [.. m.GetParameters().Select(p => Segnaposto(p.ParameterType))]))
            .Cast<ArchiveException>()
            .Select(e => e.MessageKey)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static object? Segnaposto(Type type) => type switch
    {
        _ when type == typeof(string) => "Position",
        _ when type == typeof(int) => 0,
        _ when type == typeof(object) => 0,
        _ => null,
    };

    /// <summary>
    /// Chiavi presenti nel resx di una lingua. L'italiano e' la lingua neutra
    /// (<c>Strings.resx</c> senza suffisso), quindi non ha un assembly satellite: si legge
    /// dalla cultura invariante.
    /// </summary>
    private static HashSet<string> Chiavi(string culture)
    {
        var target = culture == "it" ? CultureInfo.InvariantCulture : new CultureInfo(culture);

        var set = Resources.GetResourceSet(target, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);

        return set.Cast<System.Collections.DictionaryEntry>()
            .Select(e => (string)e.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static ArchiveDescriptor Resolve(string key) =>
        Catalog.Find(key) ?? throw new InvalidOperationException($"Anagrafica {key} assente dal catalogo.");
}
