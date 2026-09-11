using MesDataManager.Domain.Entities;
using MesDataManager.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace MesDataManager.Tests;

/// <summary>
/// Verifica la mappatura cosi' come la vede <b>SQL Server</b>, non SQLite.
/// <para>
/// Il modello si costruisce senza connettersi: basta configurare il provider. E' l'unico modo di
/// verificare qui i tipi di colonna reali, che e' la classe di errore piu' costosa di questo
/// progetto — i test su SQLite passano e in esercizio la scrittura fallisce.
/// </para>
/// </summary>
public sealed class MesDbContextMappingTests
{
    /// <summary>
    /// Le due colonne JSON conservano la risposta intera del servizio — una reale ne misura
    /// 4.253 caratteri — e sono <c>nvarchar(max)</c> a database dall'11 settembre 2026.
    /// <para>
    /// Non si dichiarano con <c>HasColumnType("nvarchar(max)")</c>: quello arriverebbe alla
    /// lettera anche a SQLite, dove "max" non e' sintassi valida, e farebbe cadere l'intera
    /// suite. Basta non dichiarare una lunghezza massima.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(nameof(Batch.SvcDiagJson))]
    [InlineData(nameof(Batch.UsrDiagJson))]
    public void Le_risposte_di_diagnostica_sono_nvarchar_max_su_sql_server(string propertyName)
    {
        Assert.Equal("nvarchar(max)", ColumnType(propertyName));
    }

    /// <summary>
    /// I due referti leggibili stanno in <c>nvarchar(2000)</c>: chi li scrive deve troncare, e il
    /// modello deve dichiarare il limite perche' quel troncamento abbia un riferimento.
    /// </summary>
    [Theory]
    [InlineData(nameof(Batch.SvcDiagMsg))]
    [InlineData(nameof(Batch.UsrDiagMsg))]
    public void I_referti_di_diagnostica_stanno_in_duemila_caratteri(string propertyName)
    {
        Assert.Equal("nvarchar(2000)", ColumnType(propertyName));
    }

    /// <summary>
    /// Gli istanti della diagnostica sono <c>datetime</c> e non <c>datetime2</c> come il resto
    /// della tabella: senza dichiararlo EF invia parametri con una precisione che la colonna non
    /// ha, e SQL Server converte a ogni scrittura.
    /// </summary>
    [Theory]
    [InlineData(nameof(Batch.SvcDiagTs))]
    [InlineData(nameof(Batch.UsrDiagTs))]
    public void Gli_istanti_della_diagnostica_sono_datetime(string propertyName)
    {
        Assert.Equal("datetime", ColumnType(propertyName));
    }

    /// <summary>
    /// L'esito corrente e' una lettura dei due gruppi di campi, non una colonna: il modello non
    /// deve provare a mapparla, altrimenti la prima query cercherebbe una colonna inesistente.
    /// </summary>
    [Fact]
    public void L_esito_corrente_non_e_una_colonna()
    {
        using var context = SqlServerContext();

        var property = context.Model
            .FindEntityType(typeof(Batch))!
            .FindProperty(nameof(Batch.CurrentDiagStatus));

        Assert.Null(property);
    }

    private static string? ColumnType(string propertyName)
    {
        using var context = SqlServerContext();

        return context.Model
            .FindEntityType(typeof(Batch))!
            .FindProperty(propertyName)!
            .GetColumnType();
    }

    /// <summary>
    /// Le viste restano mappate come viste. Non e' un dettaglio di stile: EF rifiuta il
    /// salvataggio di un'entita' mappata a una vista, e questo e' l'unico presidio che impedisce
    /// a una scrittura distratta di finire su dati che appartengono alla raccolta dati del MES.
    /// </summary>
    [Theory]
    [InlineData(typeof(ModuleTrans))]
    [InlineData(typeof(ModuleTransRoute))]
    [InlineData(typeof(ModuleTransScrap))]
    [InlineData(typeof(ProductionTag))]
    [InlineData(typeof(Die))]
    [InlineData(typeof(DieSetup))]
    [InlineData(typeof(Casting))]
    public void Le_viste_non_sono_mappate_come_tabelle(Type entityType)
    {
        using var context = SqlServerContext();

        var entity = context.Model.FindEntityType(entityType)!;

        Assert.NotNull(entity.GetViewName());
        Assert.Null(entity.GetTableName());
    }

    /// <summary>
    /// Le tabelle di lotto stanno nello schema <c>Press</c>. Nel database esistono omonimi negli
    /// schemi <c>History</c> e <c>ML</c>: uno schema sbagliato non darebbe errore, leggerebbe lo
    /// storico al posto del dato corrente.
    /// </summary>
    [Theory]
    [InlineData(typeof(Batch), "Batch")]
    [InlineData(typeof(BatchBillet), "BatchBillet")]
    [InlineData(typeof(BatchBarQty), "BatchBarQty")]
    [InlineData(typeof(BatchWorker), "BatchWorker")]
    [InlineData(typeof(BatchProdOrder), "BatchProdOrders")]
    [InlineData(typeof(BatchBilletProdOrder), "BatchBilletProdOrders")]
    public void Le_tabelle_di_lotto_stanno_nello_schema_Press(Type entityType, string tableName)
    {
        using var context = SqlServerContext();

        var entity = context.Model.FindEntityType(entityType)!;

        Assert.Equal(tableName, entity.GetTableName());
        Assert.Equal("Press", entity.GetSchema());
    }

    /// <summary>
    /// Contesto configurato per SQL Server e mai connesso: serve il modello, non il database.
    /// </summary>
    private static MesDbContext SqlServerContext() =>
        new(new DbContextOptionsBuilder<MesDbContext>()
            .UseSqlServer("Server=nessuno;Database=nessuno;Trusted_Connection=True")
            .Options);
}
