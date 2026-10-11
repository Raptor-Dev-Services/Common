using System.Text.Json;
using Common.Workflow.Depreciation;

namespace Common.Workflow.Tests.Depreciation;

/// <summary>
/// SRS-CMN-C07 REQ-008 (garantia) y REQ-009 (TCO); vectores V7 y V8.
/// </summary>
public class WarrantyAndTcoTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso);

    [Fact]
    public void V7_tco_total_meses_completos_y_mensual()
    {
        var tco = Tco.Compute(12_000m, [1_500m, 500m], D("2026-01-15"), D("2026-10-11"));

        Assert.Equal(new TcoResult(14_000m, 1_750m, 8), tco);
    }

    [Fact]
    public void Tco_con_menos_de_un_mes_divide_entre_uno()
    {
        var tco = Tco.Compute(1_000m, [], D("2026-01-15"), D("2026-02-14"));
        Assert.Equal(new TcoResult(1_000m, 1_000m, 0), tco);
    }

    [Fact]
    public void Tco_mensual_se_redondea_al_centavo()
    {
        var tco = Tco.Compute(1_000m, [], D("2026-01-01"), D("2026-04-01"));
        Assert.Equal(333.33m, tco.Monthly);
    }

    [Fact]
    public void Tco_con_corte_antes_de_la_compra_es_error_del_programador()
    {
        Assert.Throws<ArgumentException>(() => Tco.Compute(1m, [], D("2026-01-15"), D("2026-01-14")));
        Assert.Throws<ArgumentNullException>(() => Tco.Compute(1m, null!, D("2026-01-15"), D("2026-01-16")));
    }

    [Fact]
    public void V8_garantia_suma_meses_con_fin_de_mes()
    {
        Assert.Equal(new WarrantyExpiry.On(D("2027-02-28")), Warranty.ExpiresOn(D("2026-03-31"), 11));
    }

    [Fact]
    public void V8_garantia_de_por_vida()
    {
        Assert.IsType<WarrantyExpiry.Lifetime>(Warranty.ExpiresOn(D("2026-03-31"), -1));
    }

    [Fact]
    public void V8_sin_garantia()
    {
        Assert.IsType<WarrantyExpiry.None>(Warranty.ExpiresOn(D("2026-03-31"), 0));
    }

    [Fact]
    public void Garantia_con_duracion_menor_a_menos_uno_es_error_del_programador()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Warranty.ExpiresOn(D("2026-03-31"), -2));
    }

    [Fact]
    public void Vencimiento_de_garantia_se_serializa_con_discriminador()
    {
        WarrantyExpiry expiry = new WarrantyExpiry.On(D("2027-02-28"));
        var json = JsonSerializer.Serialize(expiry);

        Assert.Contains("\"kind\":\"on\"", json);
        Assert.Equal(expiry, JsonSerializer.Deserialize<WarrantyExpiry>(json));
    }
}
