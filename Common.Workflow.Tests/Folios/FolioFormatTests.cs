using System.Text.Json;
using Common.Workflow.Folios;

namespace Common.Workflow.Tests.Folios;

/// <summary>
/// C05: como se escribe un folio y que se reconoce al buscar. Incluye las pruebas de Zeit
/// <c>FolioDeTicketTests</c> (ADR-0094) portadas a <see cref="FolioFormat"/>.
/// </summary>
public class FolioFormatTests
{
    private static readonly FolioFormat Tickets = new();
    private static readonly FolioFormat Ordenes = new("OT-", 5, "-");

    // V1 + Zeit
    [Theory]
    [InlineData(1, "T-000001")]
    [InlineData(123, "T-000123")]
    [InlineData(1234567, "T-1234567")]   // crece en vez de cortarse
    public void V1_se_escribe_con_seis_cifras_como_minimo(long numero, string folio)
    {
        Assert.Equal(folio, Tickets.Format(numero));
    }

    [Fact]
    public void Por_omision_el_prefijo_es_T_y_seis_cifras_sin_periodo()
    {
        Assert.Equal("T-", Tickets.Prefix);
        Assert.Equal(6, Tickets.MinDigits);
        Assert.Null(Tickets.PeriodSeparator);
    }

    // V2 + Zeit
    [Theory]
    [InlineData("  t-123 ", 123)]
    [InlineData("T123", 123)]
    [InlineData("# 123", 123)]
    [InlineData("000123", 123)]
    [InlineData("T-000123", 123)]
    [InlineData("t-123", 123)]
    [InlineData("#123", 123)]
    [InlineData("  123  ", 123)]
    [InlineData("000040", 40)]
    [InlineData("123456789012345678", 123456789012345678)]   // 18 cifras: el maximo
    public void V2_reconoce_el_numero_escrito_de_las_formas_que_se_dictan(string texto, long numero)
    {
        Assert.Equal(numero, Tickets.TryParse(texto));
    }

    // V3 + Zeit
    [Theory]
    [InlineData("T-0")]
    [InlineData("-5")]
    [InlineData("impresora")]
    [InlineData("OT-12")]                   // prefijo de otra secuencia
    [InlineData("1234567890123456789")]     // 19 cifras: no cabe y nadie lo dicta
    [InlineData("١٢٣")]                     // cifras que no son ASCII
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("caja")]
    [InlineData("T-")]
    [InlineData("#")]
    [InlineData("0")]
    [InlineData("12a")]
    [InlineData("1 2")]
    public void V3_lo_que_no_es_un_folio_no_se_lee_como_uno(string? texto)
    {
        Assert.Null(Tickets.TryParse(texto));
    }

    [Fact]
    public void Lo_que_se_escribe_se_vuelve_a_leer()
    {
        Assert.Equal(98765, Tickets.TryParse(Tickets.Format(98765)));
        Assert.Equal(("2026", 42L), Ordenes.TryParseWithPeriod(Ordenes.Format(42, "2026")));
    }

    // V9
    [Fact]
    public void V9_con_periodo_se_escribe_y_se_lee_el_ano()
    {
        Assert.Equal("OT-2026-00042", Ordenes.Format(42, "2026"));
        Assert.Equal(("2026", 42L), Ordenes.TryParseWithPeriod("ot-2026-42"));
        Assert.Equal(("2026", 42L), Ordenes.TryParseWithPeriod("OT-2026-42"));
        Assert.Equal(42, Ordenes.TryParse("OT-2026-42"));
    }

    [Fact]
    public void Con_periodo_un_numero_sin_ano_se_lee_sin_periodo()
    {
        Assert.Equal((null, 42L), Ordenes.TryParseWithPeriod("OT-42"));
        Assert.Equal((null, 42L), Ordenes.TryParseWithPeriod("#42"));
    }

    [Theory]
    [InlineData("OT-20@6-42")]   // periodo con simbolos
    [InlineData("OT-2026-")]     // sin numero
    [InlineData("OT-2026-0")]
    [InlineData("T-2026-42")]    // otro prefijo
    public void Con_periodo_rechaza_lo_que_no_es_folio(string texto)
    {
        Assert.Null(Ordenes.TryParseWithPeriod(texto));
    }

    [Fact]
    public void Sin_periodo_un_texto_con_ano_no_es_folio()
    {
        Assert.Null(Tickets.TryParseWithPeriod("T-2026-42"));
        Assert.Equal((null, 123L), Tickets.TryParseWithPeriod("T-123"));
    }

    [Fact]
    public void Prefijo_vacio_solo_acepta_el_numero_o_almohadilla()
    {
        var soloNumero = new FolioFormat("", 4);
        Assert.Equal("0007", soloNumero.Format(7));
        Assert.Equal(7, soloNumero.TryParse("#7"));
        Assert.Null(soloNumero.TryParse("T-7"));
    }

    [Theory]
    [InlineData("t-")]           // minusculas
    [InlineData("ABCDEFGHI")]    // 9 caracteres
    [InlineData("T_")]
    [InlineData("1-")]           // empieza con cifra: se confundiria con el numero
    [InlineData("-T")]
    public void Prefijo_invalido_es_error_del_programador(string prefijo)
    {
        Assert.Throws<ArgumentException>(() => new FolioFormat(prefijo));
        Assert.Throws<ArgumentException>(() => Tickets with { Prefix = prefijo });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void MinDigits_fuera_de_1_a_12_es_error_del_programador(int cifras)
    {
        Assert.Throws<ArgumentException>(() => new FolioFormat("T-", cifras));
        Assert.Throws<ArgumentException>(() => Tickets with { MinDigits = cifras });
    }

    [Fact]
    public void Limites_validos_de_prefijo_y_cifras()
    {
        Assert.Equal("ABCDEFGH000000000001", new FolioFormat("ABCDEFGH", 12).Format(1));
        Assert.Equal("A1", new FolioFormat("A", 1).Format(1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("----")]
    [InlineData("x")]
    [InlineData("1")]
    public void Separador_invalido_es_error_del_programador(string separador)
    {
        Assert.Throws<ArgumentException>(() => new FolioFormat("OT-", 5, separador));
        Assert.Throws<ArgumentException>(() => Ordenes with { PeriodSeparator = separador });
    }

    [Fact]
    public void Periodo_en_formato_sin_periodo_o_invalido_es_error_del_programador()
    {
        Assert.Throws<ArgumentException>(() => Tickets.Format(1, "2026"));
        Assert.Throws<ArgumentException>(() => Ordenes.Format(1, "20-26"));
        Assert.Throws<ArgumentException>(() => Ordenes.Format(1, ""));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Numero_no_positivo_es_error_del_programador(long numero)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Tickets.Format(numero));
    }

    [Fact]
    public void Formatea_igual_en_cualquier_cultura()
    {
        var antes = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            Assert.Equal("T-000123", Tickets.Format(123));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = antes;
        }
    }

    [Fact]
    public void La_definicion_se_serializa_con_enums_como_texto()
    {
        var definicion = new FolioDefinition("work_order", FolioScope.Branch, Ordenes, FolioMode.Gapless, FolioReset.Yearly);

        var json = JsonSerializer.Serialize(definicion);
        var leida = JsonSerializer.Deserialize<FolioDefinition>(json);

        Assert.Contains("\"Branch\"", json);
        Assert.Contains("\"Gapless\"", json);
        Assert.Contains("\"Yearly\"", json);
        Assert.Equal(definicion, leida);
    }

    [Fact]
    public void Un_formato_invalido_no_se_deserializa()
    {
        Assert.Throws<ArgumentException>(() =>
            JsonSerializer.Deserialize<FolioFormat>("""{"Prefix":"t-","MinDigits":6}"""));
    }
}
