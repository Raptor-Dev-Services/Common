using Common.Workflow.Rules;
using static Common.Workflow.Tests.Rules.RuleFixture;

namespace Common.Workflow.Tests.Rules;

/// <summary>
/// REQ-CMN-C03-002 y 004: tabla de verdad de los operadores y valores multivaluados.
/// </summary>
public class RuleOperatorTests
{
    private static bool Eval(string field, RuleOperator op, string pattern, RuleValue? value,
        string zone = Zone, IReadOnlyList<CriterionSpec>? catalog = null)
    {
        var rule = Rule("R", 10, RuleMatch.All, [C(field, op, pattern)], A("assign:group_id", "ok"));
        var result = RuleCollection.Compile(new RuleCollectionOptions("t", TimeZoneId: zone), catalog ?? Catalog, Actions, [rule], Trees);
        var collection = Assert.IsAssignableFrom<Common.Results.ISuccess<RuleCollection>>(result).Data;
        var facts = value is { } v ? Facts((field, v)) : RuleFacts.Empty;
        return !collection.Process(facts, RuleTrigger.OnUpdate, Now).NoRuleMatched;
    }

    public static TheoryData<RuleOperator, string, string?, bool> TextCases => new()
    {
        // Is / IsNot: sin mayusculas, patron recortado.
        { RuleOperator.Is, "  Impresora ", "IMPRESORA", true },
        { RuleOperator.Is, "impresora", "impresoras", false },
        { RuleOperator.Is, "impresora", null, false },
        { RuleOperator.IsNot, "impresora", "IMPRESORA", false },
        { RuleOperator.IsNot, "impresora", null, true },
        // Contains / NotContains: patron vacio falso en ambos; campo vacio Contains=false, NotContains=true.
        { RuleOperator.Contains, "PRES", "impresora", true },
        { RuleOperator.Contains, "", "impresora", false },
        { RuleOperator.Contains, "x", null, false },
        { RuleOperator.Contains, "x", "", false },
        { RuleOperator.NotContains, "x", "impresora", true },
        { RuleOperator.NotContains, "pres", "impresora", false },
        { RuleOperator.NotContains, "", "impresora", false },
        { RuleOperator.NotContains, "", null, false },
        { RuleOperator.NotContains, "x", null, true },
        { RuleOperator.NotContains, "x", "", true },
        // BeginsWith / EndsWith: vacio en cualquier lado es falso.
        { RuleOperator.BeginsWith, "IMP", "impresora", true },
        { RuleOperator.BeginsWith, "pres", "impresora", false },
        { RuleOperator.BeginsWith, "", "impresora", false },
        { RuleOperator.BeginsWith, "a", null, false },
        { RuleOperator.EndsWith, "SORA", "impresora", true },
        { RuleOperator.EndsWith, "impre", "impresora", false },
        { RuleOperator.EndsWith, "", "impresora", false },
        { RuleOperator.EndsWith, "a", null, false },
        // Regex: IgnoreCase + Singleline, sin delimitadores.
        { RuleOperator.RegexMatch, "^impresora.rota$", "IMPRESORA\nROTA", true },
        { RuleOperator.RegexMatch, "^x", "impresora", false },
        { RuleOperator.RegexNotMatch, "^x", "impresora", true },
        { RuleOperator.RegexNotMatch, "^imp", "impresora", false },
        // Exists / NotExists.
        { RuleOperator.Exists, "", "algo", true },
        { RuleOperator.Exists, "", "   ", false },
        { RuleOperator.Exists, "", null, false },
        { RuleOperator.NotExists, "", null, true },
        { RuleOperator.NotExists, "", "", true },
        { RuleOperator.NotExists, "", "algo", false },
    };

    [Theory]
    [MemberData(nameof(TextCases))]
    public void Operadores_de_texto(RuleOperator op, string pattern, string? value, bool expected)
    {
        Assert.Equal(expected, Eval("title", op, pattern, value is null ? null : T(value)));
    }

    [Theory]
    [InlineData(RuleOperator.Is, "4", 4, true)]
    [InlineData(RuleOperator.Is, "4.0", 4, true)]
    [InlineData(RuleOperator.IsNot, "4", 3, true)]
    [InlineData(RuleOperator.GreaterThan, "3", 4, true)]
    [InlineData(RuleOperator.GreaterThan, "4", 4, false)]
    [InlineData(RuleOperator.GreaterOrEqual, "4", 4, true)]
    [InlineData(RuleOperator.GreaterOrEqual, "4.5", 4, false)]
    [InlineData(RuleOperator.LessThan, "4.5", 4, true)]
    [InlineData(RuleOperator.LessThan, "4", 4, false)]
    [InlineData(RuleOperator.LessOrEqual, "4", 4, true)]
    [InlineData(RuleOperator.LessOrEqual, "-1", 4, false)]
    public void Operadores_numericos(RuleOperator op, string pattern, int value, bool expected)
    {
        Assert.Equal(expected, Eval("urgency", op, pattern, RuleValue.Number(value)));
    }

    [Theory]
    [InlineData(RuleOperator.GreaterThan, "-1")]
    [InlineData(RuleOperator.LessThan, "100")]
    [InlineData(RuleOperator.Is, "0")]
    public void Numero_vacio_nunca_cumple_una_comparacion(RuleOperator op, string pattern)
    {
        Assert.False(Eval("urgency", op, pattern, null));
    }

    [Fact]
    public void Numero_como_texto_invariante_se_compara()
    {
        Assert.True(Eval("urgency", RuleOperator.GreaterThan, "3", T("3.5")));
        Assert.False(Eval("urgency", RuleOperator.GreaterThan, "3", T("3,5x")));
    }

    [Theory]
    [InlineData(RuleOperator.Under, "norte", "norte", true)]
    [InlineData(RuleOperator.Under, "norte", "norte-planta1", true)]
    [InlineData(RuleOperator.Under, "norte", "norte-planta1-sala", true)]
    [InlineData(RuleOperator.Under, "norte", "sur", false)]
    [InlineData(RuleOperator.Under, "norte", null, false)]
    [InlineData(RuleOperator.NotUnder, "norte", "sur", true)]
    [InlineData(RuleOperator.NotUnder, "norte", "norte-planta1", false)]
    [InlineData(RuleOperator.NotUnder, "norte", null, true)]
    [InlineData(RuleOperator.Is, "norte", "Norte", false)]   // los ids distinguen mayusculas
    public void Operadores_de_arbol_e_id(RuleOperator op, string pattern, string? value, bool expected)
    {
        Assert.Equal(expected, Eval("location_id", op, pattern, value is null ? null : RuleValue.Id(value)));
    }

    // nowUtc = 2026-11-17T17:00Z = 2026-11-17 11:00 en Mexico.
    [Theory]
    [InlineData(RuleOperator.DateBefore, "2026-11-16", "2026-11-16T05:59:00Z", true)]   // 15-nov 23:59 local
    [InlineData(RuleOperator.DateBefore, "2026-11-16", "2026-11-16T06:00:00Z", false)]  // 16-nov 00:00 local
    [InlineData(RuleOperator.DateEquals, "2026-11-16", "2026-11-17T05:00:00Z", true)]   // 16-nov 23:00 local
    [InlineData(RuleOperator.DateNotEquals, "2026-11-16", "2026-11-17T06:00:00Z", true)]
    [InlineData(RuleOperator.DateAfter, "-1DAY", "2026-11-17T06:00:00Z", true)]
    [InlineData(RuleOperator.DateAfter, "-1DAY", "2026-11-16T23:00:00Z", false)]
    [InlineData(RuleOperator.DateEquals, "+0DAY", "2026-11-17T06:00:00Z", true)]
    [InlineData(RuleOperator.DateBefore, "-1WEEK", "2026-11-09T12:00:00Z", true)]
    [InlineData(RuleOperator.DateBefore, "-1MONTH", "2026-10-16T12:00:00Z", true)]
    [InlineData(RuleOperator.DateBefore, "-1MONTH", "2026-10-17T12:00:00Z", false)]
    [InlineData(RuleOperator.DateAfter, "-1YEAR", "2025-11-18T12:00:00Z", true)]
    [InlineData(RuleOperator.DateBefore, "NOW", "2026-11-17T16:59:59Z", true)]
    [InlineData(RuleOperator.DateAfter, "NOW", "2026-11-17T16:59:59Z", false)]
    [InlineData(RuleOperator.DateEquals, "NOW", "2026-11-17T17:00:00Z", true)]
    [InlineData(RuleOperator.DateAfter, "-3HOUR", "2026-11-17T14:30:00Z", true)]
    [InlineData(RuleOperator.DateBefore, "+30MINUTE", "2026-11-17T17:20:00Z", true)]
    [InlineData(RuleOperator.DateBefore, "2026-11-17T12:00:00-06:00", "2026-11-17T17:59:00Z", true)]
    [InlineData(RuleOperator.Is, "2026-11-17", "2026-11-17T23:00:00Z", true)]
    [InlineData(RuleOperator.IsNot, "2026-11-17", "2026-11-18T07:00:00Z", true)]
    public void Operadores_de_fecha(RuleOperator op, string pattern, string value, bool expected)
    {
        var instant = DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime;
        Assert.Equal(expected, Eval("opened_at", op, pattern, RuleValue.Instant(instant)));
    }

    [Theory]
    [InlineData(RuleOperator.DateBefore, false)]
    [InlineData(RuleOperator.DateEquals, false)]
    [InlineData(RuleOperator.DateNotEquals, true)]
    public void Fecha_vacia(RuleOperator op, bool expected)
    {
        Assert.Equal(expected, Eval("opened_at", op, "NOW", null));
    }

    [Fact]
    public void La_zona_cambia_el_dia_local()
    {
        var instant = RuleValue.Instant(new DateTime(2026, 11, 17, 3, 0, 0, DateTimeKind.Utc));
        Assert.True(Eval("opened_at", RuleOperator.DateEquals, "2026-11-16", instant));          // Mexico: 16-nov 21:00
        Assert.False(Eval("opened_at", RuleOperator.DateEquals, "2026-11-16", instant, "UTC"));
    }

    [Fact]
    public void Campos_fecha_local_bool_y_texto_tipado()
    {
        IReadOnlyList<CriterionSpec> catalog =
        [
            new("due", RuleValueType.Date, AllOperators),
            new("vip", RuleValueType.Bool, AllOperators),
        ];

        Assert.True(Eval("due", RuleOperator.DateBefore, "NOW", RuleValue.Date(new DateOnly(2026, 11, 16)), catalog: catalog));
        Assert.True(Eval("due", RuleOperator.DateEquals, "2026-11-17T12:00:00Z", RuleValue.Date(new DateOnly(2026, 11, 17)), catalog: catalog));
        Assert.True(Eval("due", RuleOperator.Is, "2026-11-16", RuleValue.Date(new DateOnly(2026, 11, 16)), catalog: catalog));
        Assert.False(Eval("due", RuleOperator.DateBefore, "NOW", T("ayer"), catalog: catalog));
        Assert.True(Eval("vip", RuleOperator.Is, "true", RuleValue.Bool(true), catalog: catalog));
        Assert.True(Eval("vip", RuleOperator.Is, "0", T("false"), catalog: catalog));
        Assert.True(Eval("vip", RuleOperator.IsNot, "TRUE", RuleValue.Bool(false), catalog: catalog));
        Assert.False(Eval("vip", RuleOperator.Is, "false", null, catalog: catalog));
    }

    public static TheoryData<RuleOperator, string, string[], bool> MultiCases => new()
    {
        // Positivos: alguno. Negativos: todos.
        { RuleOperator.Is, "urgente", ["vip", "urgente"], true },
        { RuleOperator.IsNot, "vip", ["vip", "urgente"], false },
        { RuleOperator.IsNot, "vip", ["normal", "urgente"], true },
        { RuleOperator.Contains, "gen", ["vip", "urgente"], true },
        { RuleOperator.NotContains, "gen", ["vip", "urgente"], false },
        { RuleOperator.NotContains, "zz", ["vip", "urgente"], true },
        { RuleOperator.RegexMatch, "^u", ["vip", "urgente"], true },
        { RuleOperator.RegexNotMatch, "^u", ["vip", "urgente"], false },
        { RuleOperator.RegexNotMatch, "^z", ["vip", "urgente"], true },
        { RuleOperator.Exists, "", ["", "x"], true },
        { RuleOperator.NotExists, "", ["", "x"], false },
        // Lista vacia = campo vacio.
        { RuleOperator.Is, "vip", [], false },
        { RuleOperator.IsNot, "vip", [], true },
        { RuleOperator.Contains, "vip", [], false },
        { RuleOperator.NotContains, "vip", [], true },
        { RuleOperator.Exists, "", [], false },
        { RuleOperator.NotExists, "", [], true },
    };

    [Theory]
    [MemberData(nameof(MultiCases))]
    public void Multivaluados(RuleOperator op, string pattern, string[] values, bool expected)
    {
        Assert.Equal(expected, Eval("tags", op, pattern, Texts(values)));
    }

    [Theory]
    [InlineData(RuleOperator.NotUnder, "norte", new[] { "sur", "norte-planta1" }, false)]
    [InlineData(RuleOperator.NotUnder, "norte", new[] { "sur", "raiz" }, true)]
    [InlineData(RuleOperator.Under, "norte", new[] { "sur", "norte-planta1" }, true)]
    public void Multivaluados_en_arbol(RuleOperator op, string pattern, string[] values, bool expected)
    {
        Assert.Equal(expected, Eval("location_id", op, pattern, Ids(values)));
    }

    [Fact]
    public void Multivaluado_de_fechas_negativo_exige_todos()
    {
        var dates = RuleValue.List([
            RuleValue.Instant(new DateTime(2026, 11, 16, 18, 0, 0, DateTimeKind.Utc)),
            RuleValue.Instant(new DateTime(2026, 11, 17, 18, 0, 0, DateTimeKind.Utc)),
        ]);
        Assert.False(Eval("opened_at", RuleOperator.DateNotEquals, "2026-11-17", dates));
        Assert.True(Eval("opened_at", RuleOperator.DateEquals, "2026-11-17", dates));
    }
}
