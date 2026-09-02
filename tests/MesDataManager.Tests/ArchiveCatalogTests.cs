using System.Globalization;
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
        // L'italiano e' la lingua neutra (Strings.resx senza suffisso), quindi non ha un
        // assembly satellite: si legge dalla cultura invariante.
        var target = culture == "it" ? CultureInfo.InvariantCulture : new CultureInfo(culture);

        var set = Resources.GetResourceSet(target, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);

        var presenti = set.Cast<System.Collections.DictionaryEntry>()
            .Select(e => (string)e.Key)
            .ToHashSet(StringComparer.Ordinal);

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

    private static ArchiveDescriptor Resolve(string key) =>
        Catalog.Find(key) ?? throw new InvalidOperationException($"Anagrafica {key} assente dal catalogo.");
}
