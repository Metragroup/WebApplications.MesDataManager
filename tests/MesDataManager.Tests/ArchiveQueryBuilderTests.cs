using MesDataManager.Domain.Entities;
using MesDataManager.Infrastructure.Archives;
using MesDataManager.Tests.Support;

using Microsoft.EntityFrameworkCore;

namespace MesDataManager.Tests;

/// <summary>
/// Composizione di filtri e ordinamenti a partire dai nomi dei campi. E' il pezzo che rende
/// possibile un solo servizio per sedici tabelle: le espressioni si costruiscono a runtime,
/// quindi un errore qui non lo segnala il compilatore.
/// </summary>
public sealed class ArchiveQueryBuilderTests : IDisposable
{
    private readonly ArchiveHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Il_filtro_sulle_voci_attive_esclude_le_disattivate()
    {
        var source = new List<Worker>
        {
            new() { WorkerId = 1, Description = "attivo", IsActive = true },
            new() { WorkerId = 2, Description = "disattivo", IsActive = false },
        }.AsQueryable();

        var filtered = ArchiveQueryBuilder.WhereActive(source).ToList();

        Assert.Equal("attivo", Assert.Single(filtered).Description);
    }

    [Fact]
    public void L_ordinamento_si_applica_nell_ordine_dei_campi_dichiarati()
    {
        var source = new List<ModuleScrapReason>
        {
            new() { ModuleScrapReasonId = 1, Position = 2, Description = "b", Code = "", OprId = "" },
            new() { ModuleScrapReasonId = 2, Position = 1, Description = "z", Code = "", OprId = "" },
            new() { ModuleScrapReasonId = 3, Position = 1, Description = "a", Code = "", OprId = "" },
        }.AsQueryable();

        var ordered = ArchiveQueryBuilder
            .OrderByFields(source, [nameof(ModuleScrapReason.Position), nameof(ModuleScrapReason.Description)])
            .Select(r => r.Description)
            .ToList();

        Assert.Equal(["a", "z", "b"], ordered);
    }

    [Fact]
    public void Un_campo_di_ordinamento_inesistente_viene_ignorato_senza_errori()
    {
        var source = new List<Module> { new() { ModuleId = "A", ModuleGroupId = "G" } }.AsQueryable();

        var ordered = ArchiveQueryBuilder.OrderByFields(source, ["CampoCheNonEsiste"]).ToList();

        Assert.Single(ordered);
    }

    [Fact]
    public void La_ricerca_cerca_in_or_su_tutti_i_campi_dichiarati()
    {
        _harness.Seed(
            new ModuleScrapReason
            {
                Position = 1,
                Description = "descrizione",
                Code = "AAA",
                OprId = "X",
                IsActiveMaster = true,
            },
            new ModuleScrapReason
            {
                Position = 2,
                Description = "altro",
                Code = "BBB",
                OprId = "AAA",
                IsActiveMaster = true,
            },
            new ModuleScrapReason
            {
                Position = 3,
                Description = "estraneo",
                Code = "CCC",
                OprId = "Y",
                IsActiveMaster = true,
            });

        using var context = _harness.CreateContext();

        var found = ArchiveQueryBuilder.WhereMatches(
            context.ModuleScrapReasons.AsNoTracking(),
            [nameof(ModuleScrapReason.Description), nameof(ModuleScrapReason.Code), nameof(ModuleScrapReason.OprId)],
            "AAA");

        Assert.Equal(2, found.Count());
    }

    [Fact]
    public void I_campi_non_testuali_vengono_saltati_e_la_ricerca_non_esplode()
    {
        using var context = _harness.CreateContext();

        var found = ArchiveQueryBuilder.WhereMatches(
            context.ModuleScrapReasons.AsNoTracking(),
            [nameof(ModuleScrapReason.Position)],
            "1");

        // Nessun campo utilizzabile: la query resta quella di partenza invece di filtrare
        // su qualcosa di arbitrario.
        Assert.Empty(found);
    }

    [Fact]
    public void I_caratteri_jolly_digitati_dall_utente_vengono_neutralizzati()
    {
        // Si verifica sul comando generato e non sul risultato: la sequenza di escape "[%]"
        // e' quella di SQL Server, mentre il LIKE di SQLite non conosce le classi di
        // caratteri. L'effetto sui dati resta da confermare sul database reale.
        using var context = _harness.CreateContext();

        var sql = ArchiveQueryBuilder.WhereMatches(
                context.DieCorrectionIssues.AsNoTracking(),
                [nameof(DieCorrectionIssue.Name)],
                "sconto 50%")
            .ToQueryString();

        Assert.Contains("%sconto 50[%]%", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Una_ricerca_vuota_non_filtra()
    {
        var source = new List<Module> { new() { ModuleId = "A", ModuleGroupId = "G" } }.AsQueryable();

        Assert.Single(ArchiveQueryBuilder.WhereMatches(source, [nameof(Module.ModuleId)], "   "));
        Assert.Single(ArchiveQueryBuilder.WhereMatches(source, [], "A"));
    }
}
