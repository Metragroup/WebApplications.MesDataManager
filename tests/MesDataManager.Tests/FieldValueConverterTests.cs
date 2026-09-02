using MesDataManager.Infrastructure.Archives;

namespace MesDataManager.Tests;

/// <summary>
/// Conversione dei valori dalla UI al tipo esatto della colonna. E' il prezzo della griglia
/// generica — i valori viaggiano come <c>object?</c> — e quindi il punto dove si perdono o si
/// falsano i dati se qualcosa non torna.
/// </summary>
public sealed class FieldValueConverterTests
{
    [Fact]
    public void Un_intero_lungo_entra_in_una_colonna_smallint()
    {
        // L'editor numerico produce long?, la colonna e' smallint: senza questo passaggio
        // l'assegnazione per riflessione fallirebbe.
        Assert.Equal((short)12, FieldValueConverter.Coerce(12L, typeof(short)));
    }

    [Fact]
    public void Un_testo_numerico_viene_convertito()
    {
        Assert.Equal(12, FieldValueConverter.Coerce("12", typeof(int)));
        Assert.Equal(1.5m, FieldValueConverter.Coerce("1.5", typeof(decimal)));
    }

    [Fact]
    public void I_decimali_si_leggono_in_formato_invariante()
    {
        // Il punto resta separatore decimale qualunque sia la lingua dell'interfaccia:
        // il valore arriva dal componente numerico, non digitato come testo libero.
        Assert.Equal(1234.56m, FieldValueConverter.Coerce("1234.56", typeof(decimal)));
    }

    [Fact]
    public void Una_casella_svuotata_diventa_assenza_di_valore_dove_la_colonna_lo_ammette()
    {
        Assert.Null(FieldValueConverter.Coerce("", typeof(int?)));
        Assert.Null(FieldValueConverter.Coerce("   ", typeof(decimal?)));
    }

    [Fact]
    public void Su_colonna_non_annullabile_il_vuoto_diventa_il_valore_neutro()
    {
        // La validazione del servizio intercetta prima i campi obbligatori: qui conta solo
        // non far fallire l'assegnazione.
        Assert.Equal(0, FieldValueConverter.Coerce("", typeof(int)));
        Assert.Equal(0, FieldValueConverter.Coerce(null, typeof(int)));
    }

    [Fact]
    public void Un_valore_gia_del_tipo_giusto_passa_intatto()
    {
        Assert.Equal("testo", FieldValueConverter.Coerce("testo", typeof(string)));
        Assert.Equal(true, FieldValueConverter.Coerce(true, typeof(bool)));
        Assert.Equal((short)3, FieldValueConverter.Coerce((short)3, typeof(short?)));
    }

    [Fact]
    public void Il_valore_iniziale_di_un_nuovo_record_e_vuoto_ma_non_nullo_sui_testi()
    {
        // Un null su un campo testuale farebbe apparire il segnaposto al posto della casella.
        Assert.Equal(string.Empty, FieldValueConverter.DefaultFor(typeof(string)));
        Assert.Equal(false, FieldValueConverter.DefaultFor(typeof(bool)));
        Assert.Null(FieldValueConverter.DefaultFor(typeof(decimal?)));
    }

    [Theory]
    [InlineData(typeof(short), -32768, 32767)]
    [InlineData(typeof(short?), -32768, 32767)]
    [InlineData(typeof(byte), 0, 255)]
    public void L_intervallo_del_tipo_intero_e_riconosciuto(Type type, int minimo, int massimo)
    {
        var range = FieldValueConverter.IntegerRange(type);

        Assert.NotNull(range);
        Assert.Equal(minimo, range.Value.Minimum);
        Assert.Equal(massimo, range.Value.Maximum);
    }

    [Theory]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(string))]
    [InlineData(typeof(bool))]
    public void I_tipi_non_interi_non_hanno_intervallo_da_verificare(Type type)
    {
        Assert.Null(FieldValueConverter.IntegerRange(type));
    }
}
