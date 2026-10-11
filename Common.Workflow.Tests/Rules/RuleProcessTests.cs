using System.Diagnostics;
using Common.Results;
using Common.Workflow.Rules;
using static Common.Workflow.Tests.Rules.RuleFixture;

namespace Common.Workflow.Tests.Rules;

/// <summary>
/// REQ-CMN-C03-003 y 005 a 012: recorrido, capturas, acciones, explicacion y reordenamiento.
/// </summary>
public class RuleProcessTests
{
    private static readonly RuleFacts Impresora = Facts(("title", T("La IMPRESORA no imprime")));

    [Fact]
    public void Any_corta_en_el_primer_verdadero_y_no_calcula_capturas_posteriores()
    {
        var rule = Rule("R1", 10, RuleMatch.Any,
            [C("title", RuleOperator.Contains, "impresora"), C("title", RuleOperator.RegexMatch, "(impresora)")],
            A("regex_result:branch_code", "[#0]"));

        var output = Process(Compile(rule), Impresora);

        Assert.Empty(Assert.Single(output.MatchedRules).Captures);
        Assert.Equal(T("[]"), output.Changes["branch_code"]);
    }

    [Fact]
    public void All_evalua_en_orden_y_corta_en_el_primer_falso()
    {
        // El segundo criterio venceria por tiempo; como el primero es falso, no se evalua y no hay aviso.
        var rule = Rule("R1", 10, RuleMatch.All,
            [C("title", RuleOperator.Contains, "nada"), C("title", RuleOperator.RegexMatch, "(a+)+$")]);

        var output = Process(Compile(rule), Facts(("title", T("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!"))));

        Assert.Empty(output.Warnings);
    }

    [Fact]
    public void Capturas_por_grupo_y_luego_por_coincidencia_concatenadas_por_criterio()
    {
        var rule = Rule("R1", 10, RuleMatch.All,
            [C("title", RuleOperator.RegexMatch, @"(\w)(\d)"), C("content", RuleOperator.RegexMatch, @"#(\d+)")],
            A("regex_result:branch_code", "#0#1#2#3#4|#5|#10"));

        var output = Process(Compile(rule), Facts(("title", T("a1 b2")), ("content", T("ticket #77"))));

        Assert.Equal(["a", "b", "1", "2", "77"], Assert.Single(output.MatchedRules).Captures);
        // #5 no existe -> vacio; #10 es #1 seguido de "0".
        Assert.Equal(T("ab1277||b0"), output.Changes["branch_code"]);
    }

    [Fact]
    public void Multivaluado_aporta_capturas_solo_del_primer_valor_que_coincide()
    {
        var rule = Rule("R1", 10, RuleMatch.All, [C("tags", RuleOperator.RegexMatch, @"^suc-(\d+)$")],
            A("regex_result:branch_code", "#0"));

        var output = Process(Compile(rule), Facts(("tags", Texts("vip", "suc-12", "suc-40"))));

        Assert.Equal(["12"], Assert.Single(output.MatchedRules).Captures);
        Assert.Equal(T("12"), output.Changes["branch_code"]);
    }

    [Fact]
    public void Ranking_ascendente_con_empate_por_id_ordinal()
    {
        var rules = new[]
        {
            Rule("b", 20, RuleMatch.All, [C("title", RuleOperator.Exists)]),
            Rule("a", 20, RuleMatch.All, [C("title", RuleOperator.Exists)]),
            Rule("z", 5, RuleMatch.All, [C("title", RuleOperator.Exists)]),
        };

        var collection = Compile(rules);

        Assert.Equal(["z", "a", "b"], collection.RuleIds);
        Assert.Equal(["z", "a", "b"], Process(collection, Impresora).MatchedRules.Select(m => m.Id));
    }

    [Theory]
    [InlineData(RuleTrigger.OnAdd, RuleTrigger.OnAdd, true)]
    [InlineData(RuleTrigger.OnAdd, RuleTrigger.OnUpdate, false)]
    [InlineData(RuleTrigger.OnUpdate, RuleTrigger.OnUpdate, true)]
    [InlineData(RuleTrigger.Any, RuleTrigger.OnAdd, true)]
    [InlineData(RuleTrigger.Any, RuleTrigger.OnUpdate, true)]
    [InlineData(RuleTrigger.None, RuleTrigger.Any, false)]
    [InlineData(RuleTrigger.OnAdd, RuleTrigger.Any, true)]
    public void Disparo_filtra_reglas(RuleTrigger ruleTriggers, RuleTrigger asked, bool evaluated)
    {
        var rule = Rule("R1", 10, RuleMatch.All, [C("title", RuleOperator.Exists)]) with { Triggers = ruleTriggers };

        var output = Compile(rule).Process(Impresora, asked, Now);

        Assert.Equal(evaluated, !output.NoRuleMatched);
    }

    [Fact]
    public void Regla_inactiva_no_se_evalua()
    {
        var rule = Rule("R1", 10, RuleMatch.All, [C("title", RuleOperator.Exists)]) with { IsActive = false };

        Assert.True(Process(Compile(rule), Impresora).NoRuleMatched);
    }

    [Fact]
    public void Accion_stop_detiene_despues_de_aplicar_todas_las_acciones_de_la_regla()
    {
        var r1 = Rule("R1", 10, RuleMatch.All, [C("title", RuleOperator.Exists)],
            A("assign:group_id", "a"), A("stop"), A("assign:priority", "3"));
        var r2 = Rule("R2", 20, RuleMatch.All, [C("title", RuleOperator.Exists)], A("assign:group_id", "b"));

        var output = Process(Compile(r1, r2), Impresora);

        Assert.Equal(StopReason.StopAction, output.StoppedBy);
        Assert.Equal(RuleValue.Id("a"), output.Changes["group_id"]);
        Assert.Equal(RuleValue.Number(3), output.Changes["priority"]);
        Assert.Equal(["R1"], output.MatchedRules.Select(m => m.Id));
    }

    [Fact]
    public void Sin_coincidencias_stop_on_first_match_no_detiene()
    {
        var r1 = Rule("R1", 10, RuleMatch.All, [C("title", RuleOperator.Contains, "nada")]);
        var r2 = Rule("R2", 20, RuleMatch.All, [C("title", RuleOperator.Exists)], A("assign:group_id", "b"));

        var output = Process(Compile(Options(stopOnFirstMatch: true), r1, r2), Impresora);

        Assert.Equal(["R2"], output.MatchedRules.Select(m => m.Id));
        Assert.Equal(StopReason.FirstMatch, output.StoppedBy);
    }

    [Fact]
    public void Append_y_append_regex_result()
    {
        var actions = RuleActionRegistry.WithBuiltIns()
            .Append("watchers", RuleValueType.Id)
            .AppendRegexResult("title")
            .RegexResult("code");
        var r1 = Rule("R1", 10, RuleMatch.All, [C("title", RuleOperator.RegexMatch, @"(\d+)")],
            A("append:watchers", "ana"), A("append:watchers", "luis"), A("append_regex_result:title", " [#0]"));
        var r2 = Rule("R2", 20, RuleMatch.All, [C("title", RuleOperator.RegexMatch, @"(\d+)")],
            A("append_regex_result:title", "!"), A("regex_result:code", "C#0"));

        var result = RuleCollection.Compile(Options(), Catalog, actions, [r1, r2], Trees);
        var output = Assert.IsAssignableFrom<ISuccess<RuleCollection>>(result).Data
            .Process(Facts(("title", T("Caminadora 3"))), RuleTrigger.OnAdd, Now);

        Assert.Equal(Ids("ana", "luis"), output.Changes["watchers"]);
        // Primero concatena al hecho; despues, a la salida que ya existe.
        Assert.Equal(T("Caminadora 3 [3]!"), output.Changes["title"]);
        Assert.Equal(T("C3"), output.Changes["code"]);
    }

    [Fact]
    public void Accion_del_producto_compute_priority_lee_hechos_y_escribe_salida()
    {
        var actions = RuleActionRegistry.WithBuiltIns()
            .Custom(new ActionSpec("compute_priority", RuleValueType.Text, new ComputePriority()));
        IReadOnlyList<CriterionSpec> catalog = [new("urgency", RuleValueType.Number, AllOperators)];
        var rule = Rule("R1", 10, RuleMatch.All, [C("urgency", RuleOperator.Exists)], A("compute_priority"));

        var collection = Assert.IsAssignableFrom<ISuccess<RuleCollection>>(
            RuleCollection.Compile(Options(), catalog, actions, [rule])).Data;
        var output = collection.Process(Facts(("urgency", RuleValue.Number(5)), ("impact", RuleValue.Number(4))),
            RuleTrigger.OnAdd, Now);

        Assert.Equal(RuleValue.Number(5), output.Changes["priority"]);
    }

    private sealed class ComputePriority : IRuleActionHandler
    {
        // Matriz urgencia x impacto simplificada de G01: el redondeo hacia arriba del promedio.
        public void Apply(RuleActionContext context)
        {
            var u = context.Facts.Get("urgency").AsNumber ?? 3;
            var i = context.Facts.Get("impact").AsNumber ?? 3;
            context.Output.Set("priority", RuleValue.Number(Math.Ceiling((u + i) / 2)));
        }
    }

    [Fact]
    public void Accion_de_regex_con_tipo_no_texto_que_no_convierte_avisa_y_se_omite()
    {
        var actions = RuleActionRegistry.WithBuiltIns()
            .Custom(new ActionSpec("regex_number", RuleValueType.Number, new SetNumber(), UsesCaptures: true));
        var rule = Rule("R1", 10, RuleMatch.All, [C("title", RuleOperator.RegexMatch, @"(\w+)")], A("regex_number", "#0"));
        var collection = Assert.IsAssignableFrom<ISuccess<RuleCollection>>(
            RuleCollection.Compile(Options(), Catalog, actions, [rule])).Data;

        var bad = collection.Process(Facts(("title", T("abc"))), RuleTrigger.OnAdd, Now);
        var good = collection.Process(Facts(("title", T("42"))), RuleTrigger.OnAdd, Now);

        Assert.Equal(["rules.invalid_action_value:R1"], bad.Warnings);
        Assert.Empty(bad.Changes);
        Assert.Equal(RuleValue.Number(42), good.Changes["n"]);
    }

    private sealed class SetNumber : IRuleActionHandler
    {
        public void Apply(RuleActionContext context) => context.Output.Set("n", context.Value);
    }

    [Fact]
    public void Explain_evalua_todo_sin_corte_y_marca_las_saltadas()
    {
        var r1 = Rule("R1", 10, RuleMatch.Any,
            [C("title", RuleOperator.Contains, "impresora"), C("title", RuleOperator.RegexMatch, "(imp)resora")],
            A("assign:group_id", "ti"), A("stop"));
        var r2 = Rule("R2", 20, RuleMatch.All, [C("title", RuleOperator.Exists)]) with { IsActive = false };
        var r3 = Rule("R3", 30, RuleMatch.All, [C("title", RuleOperator.Exists)]) with { Triggers = RuleTrigger.OnUpdate };
        var r4 = Rule("R4", 40, RuleMatch.All, [C("title", RuleOperator.Exists), C("urgency", RuleOperator.GreaterThan, "1")]);
        var r5 = Rule("R5", 50, RuleMatch.All, []);

        var explanation = Compile(r1, r2, r3, r4, r5).Explain(Impresora, RuleTrigger.OnAdd, Now);

        Assert.Equal(["R1", "R2", "R3", "R4", "R5"], explanation.Rules.Select(r => r.Id));
        var e1 = explanation.Rules[0];
        Assert.Null(e1.Skipped);
        Assert.True(e1.Matched);
        Assert.True(e1.Applied);
        Assert.Equal([true, true], e1.Criteria.Select(c => c.Result));   // sin corte en Any
        Assert.Equal(["IMP"], e1.Captures);
        Assert.Equal(T("La IMPRESORA no imprime"), e1.Criteria[0].Value);
        Assert.Equal(RuleSkipReason.Inactive, explanation.Rules[1].Skipped);
        Assert.True(explanation.Rules[1].Matched);
        Assert.False(explanation.Rules[1].Applied);
        Assert.Equal(RuleSkipReason.TriggerNotIncluded, explanation.Rules[2].Skipped);
        Assert.Equal(RuleSkipReason.AfterStop, explanation.Rules[3].Skipped);
        Assert.Equal([true, false], explanation.Rules[3].Criteria.Select(c => c.Result));   // sin corte en All
        Assert.False(explanation.Rules[4].Matched);
        Assert.Equal(StopReason.StopAction, explanation.Output.StoppedBy);
        Assert.Equal(["R1"], explanation.Output.MatchedRules.Select(m => m.Id));
    }

    [Fact]
    public void Process_es_puro_y_repetible_entre_hilos()
    {
        var collection = Compile(Options(chain: true),
            Rule("R10", 10, RuleMatch.All, [C("title", RuleOperator.Contains, "impresora")], A("assign:category_id", "print")),
            Rule("R70", 70, RuleMatch.All, [C("category_id", RuleOperator.Is, "print")], A("assign:group_id", "ti")));

        var outputs = Enumerable.Range(0, 200).AsParallel().Select(_ => Process(collection, Impresora)).ToList();

        Assert.All(outputs, o => Assert.Equal(["R10", "R70"], o.MatchedRules.Select(m => m.Id)));
        Assert.False(Impresora.ContainsKey("category_id"));
    }

    [Fact]
    public void Rendimiento_200_reglas_por_5_criterios_sobre_30_hechos()
    {
        var rules = Enumerable.Range(0, 200).Select(i => Rule($"R{i:000}", i, RuleMatch.All,
        [
            C("title", RuleOperator.Contains, "impresora"),
            C("content", RuleOperator.RegexMatch, @"piso (\d+)"),
            C("urgency", RuleOperator.GreaterOrEqual, (i % 5).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            C("tags", RuleOperator.IsNot, "vip"),
            C("location_id", RuleOperator.Under, "norte"),
        ], A("assign:group_id", "g" + i))).ToArray();
        var collection = Compile(Options(chain: true), rules);
        var facts = RuleFacts.From(Enumerable.Range(0, 22).Select(i => KeyValuePair.Create("extra" + i, T("x")))
            .Concat([
                KeyValuePair.Create("title", T("La impresora no imprime")),
                KeyValuePair.Create("content", T("en el piso 3")),
                KeyValuePair.Create("urgency", RuleValue.Number(3)),
                KeyValuePair.Create("tags", Texts("normal", "urgente")),
                KeyValuePair.Create("location_id", RuleValue.Id("norte-planta1")),
                KeyValuePair.Create("category_id", RuleValue.Id("x")),
                KeyValuePair.Create("opened_at", RuleValue.Instant(Now)),
                KeyValuePair.Create("requester_groups", Ids("a")),
            ]));
        Assert.Equal(30, facts.Count);

        for (var i = 0; i < 20; i++) Process(collection, facts);   // calentamiento
        var samples = new List<double>();
        for (var i = 0; i < 100; i++)
        {
            var watch = Stopwatch.StartNew();
            Process(collection, facts);
            samples.Add(watch.Elapsed.TotalMilliseconds);
        }
        samples.Sort();

        // El objetivo del SRS es 1 ms p95; se deja holgura para maquinas de CI compartidas.
        Assert.True(samples[94] < 10, $"p95 = {samples[94]:F3} ms");
    }

    [Theory]
    [InlineData("C", "A", RuleRanking.Position.Before, new[] { "C:10", "A:20", "B:30" })]
    [InlineData("A", "C", RuleRanking.Position.After, new[] { "B:10", "C:20", "A:30" })]
    [InlineData("A", "B", RuleRanking.Position.After, new[] { "B:10", "A:20", "C:30" })]
    [InlineData("B", "B", RuleRanking.Position.Before, new[] { "A:10", "B:20", "C:30" })]
    public void Mover_renumera_de_10_en_10(string ruleId, string targetId, RuleRanking.Position position, string[] expected)
    {
        RuleDefinition[] rules =
        [
            Rule("B", 20, RuleMatch.All, []), Rule("A", 10, RuleMatch.All, []), Rule("C", 30, RuleMatch.All, []),
        ];

        var rankings = RuleRanking.Move(rules, ruleId, targetId, position);

        Assert.Equal(expected, rankings.OrderBy(kv => kv.Value).Select(kv => $"{kv.Key}:{kv.Value}"));
    }

    [Fact]
    public void Mover_con_ids_inexistentes_es_error_del_programador()
    {
        RuleDefinition[] rules = [Rule("A", 10, RuleMatch.All, [])];
        Assert.Throws<ArgumentException>(() => RuleRanking.Move(rules, "X", "A", RuleRanking.Position.Before));
        Assert.Throws<ArgumentException>(() => RuleRanking.Move(rules, "A", "X", RuleRanking.Position.Before));
    }
}
