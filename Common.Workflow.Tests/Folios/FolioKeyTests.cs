using Common.Workflow.Folios;

namespace Common.Workflow.Tests.Folios;

/// <summary>C05: la clave de un contador (ambito, periodo) y el reinicio anual por fecha local.</summary>
public class FolioKeyTests
{
    [Fact]
    public void Las_claves_de_ambito_tienen_la_forma_del_contrato()
    {
        Assert.Equal("global", FolioKey.Global("support_ticket").ToScopeKey());
        Assert.Equal("t:42", FolioKey.ForTenant("maintenance_order", "42").ToScopeKey());
        Assert.Equal("t:42/b:7", FolioKey.ForBranch("work_order", "42", "7").ToScopeKey());
        Assert.Equal("global/p:2026", FolioKey.Global("contract").WithPeriod(2026).ToScopeKey());
    }

    // V4
    [Fact]
    public void V4_sucursal_con_periodo()
    {
        const string tenant = "7f1c2a4e-0b6d-4c8e-9a1f-3d5e7b9c1a2f";
        const string branch = "a2b9d4f6-1c3e-4a5b-8d7f-9e0a1b2c3d4e";

        var key = FolioKey.ForBranch("work_order", tenant, branch).WithPeriod(2026);

        Assert.Equal($"t:{tenant}/b:{branch}/p:2026", key.ToScopeKey());
        Assert.Equal("2026", key.Period);
    }

    [Fact]
    public void Una_clave_de_mas_de_160_caracteres_es_error_del_programador()
    {
        var largo = new string('a', 80);
        var limite = new string('a', 155);   // "t:" + 155 + ... = 157

        Assert.Equal(157, FolioKey.ForTenant("s", limite).ToScopeKey().Length);
        Assert.Throws<ArgumentException>(() => FolioKey.ForBranch("s", largo, largo));
        Assert.Throws<ArgumentException>(() => FolioKey.ForTenant("s", limite).WithPeriod(2026));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Support_Ticket")]
    [InlineData("support-ticket")]
    [InlineData("support ticket")]
    public void Secuencia_invalida_es_error_del_programador(string secuencia)
    {
        Assert.Throws<ArgumentException>(() => FolioKey.Global(secuencia));
    }

    [Fact]
    public void Secuencia_de_64_caracteres_es_el_maximo()
    {
        Assert.Equal("global", FolioKey.Global(new string('a', 64)).ToScopeKey());
        Assert.Throws<ArgumentException>(() => FolioKey.Global(new string('a', 65)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a/b")]    // la diagonal separa partes de la clave
    [InlineData("a b")]
    public void Id_de_tenant_o_sucursal_invalido_es_error_del_programador(string? id)
    {
        Assert.Throws<ArgumentException>(() => FolioKey.ForTenant("contract", id!));
        Assert.Throws<ArgumentException>(() => FolioKey.ForBranch("contract", "1", id!));
    }

    [Fact]
    public void Una_clave_incoherente_hecha_a_mano_se_rechaza_al_usarla()
    {
        Assert.Throws<ArgumentException>(() => new FolioKey("s", FolioScope.Global, "1", null, null).ToScopeKey());
        Assert.Throws<ArgumentException>(() => new FolioKey("s", FolioScope.Global, null, "1", null).ToScopeKey());
        Assert.Throws<ArgumentException>(() => new FolioKey("s", FolioScope.Tenant, "1", "2", null).ToScopeKey());
        Assert.Throws<ArgumentException>(() => new FolioKey("s", FolioScope.Branch, "1", null, null).ToScopeKey());
        Assert.Throws<ArgumentException>(() => new FolioKey("s", (FolioScope)9, null, null, null).ToScopeKey());
        Assert.Throws<ArgumentException>(() => new FolioKey("s", FolioScope.Global, null, null, "20/26").ToScopeKey());
        Assert.Throws<ArgumentException>(() => new FolioKey("s", FolioScope.Global, null, null, "").ToScopeKey());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    public void Ano_fuera_de_rango_es_error_del_programador(int ano)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FolioKey.Global("contract").WithPeriod(ano));
    }

    [Fact]
    public void Ano_de_una_cifra_se_escribe_con_cuatro()
    {
        Assert.Equal("global/p:0001", FolioKey.Global("contract").WithPeriod(1).ToScopeKey());
    }

    // REQ-CMN-C05-005: el periodo es el ano LOCAL, no el UTC.
    [Fact]
    public void El_reinicio_anual_usa_el_ano_local_de_la_sucursal()
    {
        var definicion = new FolioDefinition("work_order", FolioScope.Branch, new FolioFormat("OT-", 5, "-"),
            Reset: FolioReset.Yearly);
        var asignadoUtc = new DateTime(2027, 1, 1, 0, 30, 0, DateTimeKind.Utc);
        var mexico = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
        var fechaLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(asignadoUtc, mexico));

        var key = definicion.KeyFor(fechaLocal, "42", "7");

        Assert.Equal(new DateOnly(2026, 12, 31), fechaLocal);
        Assert.Equal("2026", key.Period);
        Assert.Equal("t:42/b:7/p:2026", key.ToScopeKey());
        Assert.Equal("OT-2026-00001", definicion.FormatFor(1, key));
    }

    [Fact]
    public void Sin_reinicio_no_hay_periodo()
    {
        var definicion = new FolioDefinition("support_ticket", FolioScope.Global, new FolioFormat());

        var key = definicion.KeyFor(new DateOnly(2026, 12, 31), "ignorado", "ignorado");

        Assert.Equal(FolioMode.AllowGaps, definicion.Mode);
        Assert.Equal(FolioReset.Never, definicion.Reset);
        Assert.Null(key.Period);
        Assert.Equal("global", key.ToScopeKey());
        Assert.Equal("T-000009", definicion.FormatFor(9, key));
    }

    [Fact]
    public void La_definicion_por_tenant_exige_el_tenant()
    {
        var definicion = new FolioDefinition("contract", FolioScope.Tenant, new FolioFormat("CT-"));

        Assert.Equal("t:9", definicion.KeyFor(new DateOnly(2026, 1, 1), "9").ToScopeKey());
        Assert.Throws<ArgumentException>(() => definicion.KeyFor(new DateOnly(2026, 1, 1)));
        Assert.Throws<ArgumentException>(() =>
            (definicion with { Scope = (FolioScope)9 }).KeyFor(new DateOnly(2026, 1, 1), "9"));
        Assert.Throws<ArgumentNullException>(() => definicion.FormatFor(1, null!));
    }
}
