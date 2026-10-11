using System.Diagnostics;
using Common.Workflow.Rules;
using static Common.Workflow.Tests.Rules.RuleFixture;

namespace Common.Workflow.Tests.Rules;

/// <summary>
/// Vectores V1..V10 de §12 del SRS CMN-C03, con los mismos datos.
/// </summary>
public class RuleVectorTests
{
    private static readonly RuleDefinition R10 = Rule("R10", 10, RuleMatch.All,
        [C("title", RuleOperator.Contains, "impresora")], A("assign:category_id", "print"));

    private static readonly RuleFacts V1Facts = Facts(("title", T("La IMPRESORA no imprime")));

    [Fact]
    public void V1_contains_sin_mayusculas_asigna_categoria()
    {
        var output = Process(Compile(R10), V1Facts);

        Assert.Equal(RuleValue.Id("print"), Assert.Single(output.Changes, kv => kv.Key == "category_id").Value);
        Assert.Single(output.Changes);
        Assert.Equal(["R10"], output.MatchedRules.Select(m => m.Id));
        Assert.False(output.NoRuleMatched);
        Assert.Null(output.StoppedBy);
    }

    [Fact]
    public void V2_all_con_segundo_criterio_falso_no_coincide()
    {
        var r20 = Rule("R20", 20, RuleMatch.All,
            [C("title", RuleOperator.Contains, "red"), C("urgency", RuleOperator.GreaterOrEqual, "4")],
            A("assign:group_id", "redes"));

        var explanation = Compile(r20).Explain(Facts(("title", T("Sin red")), ("urgency", RuleValue.Number(3))),
            RuleTrigger.OnAdd, Now);

        Assert.Empty(explanation.Output.Changes);
        Assert.True(explanation.Output.NoRuleMatched);
        Assert.Equal([true, false], explanation.Rules[0].Criteria.Select(c => c.Result));
    }

    [Fact]
    public void V3_any_coincide_por_ubicacion_bajo_el_arbol()
    {
        var r30 = Rule("R30", 30, RuleMatch.Any,
            [C("requester_groups", RuleOperator.Is, "recepcion"), C("location_id", RuleOperator.Under, "norte")],
            A("assign:group_id", "soporte-norte"));

        var output = Process(Compile(r30),
            Facts(("requester_groups", Ids()), ("location_id", RuleValue.Id("norte-planta1"))));

        Assert.Equal(new Dictionary<string, RuleValue> { ["group_id"] = RuleValue.Id("soporte-norte") }, output.Changes);
    }

    [Fact]
    public void V4_negativo_exige_todos_los_valores_y_positivo_alguno()
    {
        var r40 = Rule("R40", 40, RuleMatch.All, [C("tags", RuleOperator.IsNot, "vip")], A("assign:priority", "2"));
        var r41 = Rule("R41", 41, RuleMatch.All, [C("tags", RuleOperator.Is, "urgente")], A("assign:priority", "5"));

        var output = Process(Compile(r40, r41), Facts(("tags", Texts("vip", "urgente"))));

        Assert.Equal(RuleValue.Number(5), output.Changes["priority"]);
        Assert.Equal(["R41"], output.MatchedRules.Select(m => m.Id));
    }

    [Fact]
    public void V5_capturas_de_regex_en_regex_result()
    {
        var r50 = Rule("R50", 50, RuleMatch.All, [C("title", RuleOperator.RegexMatch, @"^\[SUC-(\d+)\]\s*(\w+)")],
            A("regex_result:branch_code", "B#0-#1"));

        var output = Process(Compile(r50), Facts(("title", T("[SUC-12] Caminadora rota"))));

        Assert.Equal(["12", "Caminadora"], Assert.Single(output.MatchedRules).Captures);
        Assert.Equal(T("B12-Caminadora"), output.Changes["branch_code"]);
    }

    [Fact]
    public void V6_stop_on_first_match_no_evalua_la_siguiente()
    {
        var r60 = Rule("R60", 60, RuleMatch.All, [C("title", RuleOperator.Contains, "impresora")], A("assign:group_id", "ti"));

        var output = Process(Compile(Options(stopOnFirstMatch: true), R10, r60), V1Facts);

        Assert.Equal(new Dictionary<string, RuleValue> { ["category_id"] = RuleValue.Id("print") }, output.Changes);
        Assert.Equal(["R10"], output.MatchedRules.Select(m => m.Id));
        Assert.Equal(StopReason.FirstMatch, output.StoppedBy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void V7_encadenamiento_salida_a_entrada(bool chain)
    {
        var r70 = Rule("R70", 70, RuleMatch.All, [C("category_id", RuleOperator.Is, "print")], A("assign:group_id", "ti"));

        var output = Process(Compile(Options(chain: chain), R10, r70), V1Facts);

        if (chain)
        {
            Assert.Equal(new Dictionary<string, RuleValue>
            {
                ["category_id"] = RuleValue.Id("print"),
                ["group_id"] = RuleValue.Id("ti"),
            }, output.Changes);
            Assert.Equal(["R10", "R70"], output.MatchedRules.Select(m => m.Id));
        }
        else
        {
            Assert.Equal(new Dictionary<string, RuleValue> { ["category_id"] = RuleValue.Id("print") }, output.Changes);
            Assert.Equal(["R10"], output.MatchedRules.Select(m => m.Id));
        }
    }

    [Fact]
    public void V8_gana_la_ultima_aplicada_y_ambas_aparecen()
    {
        var r80 = Rule("R80", 80, RuleMatch.All, [C("title", RuleOperator.Contains, "caminadora")], A("assign:group_id", "mtto"));
        var r90 = Rule("R90", 90, RuleMatch.All, [C("content", RuleOperator.Contains, "urgente")], A("assign:group_id", "mtto-urgente"));

        var output = Process(Compile(r90, r80), Facts(("title", T("Caminadora 3")), ("content", T("es urgente"))));

        Assert.Equal(RuleValue.Id("mtto-urgente"), output.Changes["group_id"]);
        Assert.Equal(["R80", "R90"], output.MatchedRules.Select(m => m.Id));
    }

    [Fact]
    public void V9_date_before_relativo_compara_fechas_locales()
    {
        var r95 = Rule("R95", 95, RuleMatch.All, [C("opened_at", RuleOperator.DateBefore, "-2DAY")], A("assign:priority", "4"));

        var output = Process(Compile(r95),
            Facts(("opened_at", RuleValue.Instant(new DateTime(2026, 11, 14, 18, 0, 0, DateTimeKind.Utc)))));

        Assert.Equal(["R95"], output.MatchedRules.Select(m => m.Id));
        Assert.Equal(RuleValue.Number(4), output.Changes["priority"]);
    }

    [Fact]
    public void V10_regex_catastrofica_vence_y_avisa()
    {
        var r99 = Rule("R99", 99, RuleMatch.All, [C("title", RuleOperator.RegexMatch, "(a+)+$")]);
        var collection = Compile(r99);

        var watch = Stopwatch.StartNew();
        var output = Process(collection, Facts(("title", T("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!"))));
        watch.Stop();

        Assert.True(output.NoRuleMatched);
        Assert.Equal(["rules.regex_timeout:R99"], output.Warnings);
        Assert.True(watch.ElapsedMilliseconds < 150, $"tardo {watch.ElapsedMilliseconds} ms");
    }
}
