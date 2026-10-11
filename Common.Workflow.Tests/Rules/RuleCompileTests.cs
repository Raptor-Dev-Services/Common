using System.Text.Json;
using Common.Results;
using Common.Workflow.Rules;
using static Common.Workflow.Tests.Rules.RuleFixture;

namespace Common.Workflow.Tests.Rules;

/// <summary>
/// REQ-CMN-C03-001: compilacion contra el catalogo, con todos los errores a la vez.
/// </summary>
public class RuleCompileTests
{
    [Fact]
    public void Campo_desconocido()
    {
        var error = Assert.Single(CompileErrors(Rule("R1", 10, RuleMatch.All, [C("nope", RuleOperator.Is, "x")])));
        Assert.Equal("rules.unknown_field", error.Code);
        Assert.Equal("Rules[0].Criteria[0].Field", error.Path);
    }

    [Theory]
    [InlineData("title", RuleOperator.Under, "norte")]          // Under sobre texto
    [InlineData("title", RuleOperator.GreaterThan, "3")]        // numerico sobre texto
    [InlineData("urgency", RuleOperator.Contains, "3")]         // texto sobre numero
    [InlineData("title", RuleOperator.DateBefore, "NOW")]       // fecha sobre texto
    [InlineData("category_id", RuleOperator.Under, "print")]    // id sin arbol
    public void Operador_incompatible_con_el_tipo(string field, RuleOperator op, string pattern)
    {
        var error = Assert.Single(CompileErrors(Rule("R1", 10, RuleMatch.All, [C(field, op, pattern)])));
        Assert.Equal("rules.operator_not_allowed", error.Code);
    }

    [Fact]
    public void Operador_que_el_catalogo_no_permite()
    {
        IReadOnlyList<CriterionSpec> catalog = [new("title", RuleValueType.Text, new HashSet<RuleOperator> { RuleOperator.Is })];
        var result = RuleCollection.Compile(Options(), catalog, Actions,
            [Rule("R1", 10, RuleMatch.All, [C("title", RuleOperator.Contains, "x")])]);

        Assert.Equal("rules.operator_not_allowed", Assert.Single(Assert.IsType<DefinitionFailure<RuleCollection>>(result).Errors).Code);
    }

    [Fact]
    public void Under_sin_resolutor_de_arbol()
    {
        var result = RuleCollection.Compile(Options(), Catalog, Actions,
            [Rule("R1", 10, RuleMatch.All, [C("location_id", RuleOperator.Under, "norte")])]);

        Assert.Equal("rules.operator_not_allowed", Assert.Single(Assert.IsType<DefinitionFailure<RuleCollection>>(result).Errors).Code);
    }

    [Theory]
    [InlineData("urgency", RuleOperator.GreaterThan, "alto")]
    [InlineData("urgency", RuleOperator.Is, "3,5")]
    [InlineData("opened_at", RuleOperator.DateBefore, "ayer")]
    [InlineData("opened_at", RuleOperator.DateAfter, "2026-11-16T10:00:00")]   // instante sin zona
    [InlineData("opened_at", RuleOperator.Is, "16/11/2026")]
    [InlineData("title", RuleOperator.RegexMatch, "([a-z")]
    [InlineData("title", RuleOperator.RegexNotMatch, "(")]
    public void Patron_invalido(string field, RuleOperator op, string pattern)
    {
        var error = Assert.Single(CompileErrors(Rule("R1", 10, RuleMatch.All, [C(field, op, pattern)])));
        Assert.Equal("rules.invalid_pattern", error.Code);
        Assert.Equal("Rules[0].Criteria[0].Pattern", error.Path);
    }

    [Fact]
    public void Regex_de_mas_de_500_caracteres_se_rechaza_y_de_500_se_acepta()
    {
        var largo = new string('a', 501);
        Assert.Equal("rules.invalid_pattern",
            Assert.Single(CompileErrors(Rule("R1", 10, RuleMatch.All, [C("title", RuleOperator.RegexMatch, largo)]))).Code);

        Compile(Rule("R1", 10, RuleMatch.All, [C("title", RuleOperator.RegexMatch, largo[..500])]));
    }

    [Fact]
    public void Patron_booleano_invalido()
    {
        IReadOnlyList<CriterionSpec> catalog = [new("vip", RuleValueType.Bool, AllOperators)];
        var result = RuleCollection.Compile(Options(), catalog, Actions,
            [Rule("R1", 10, RuleMatch.All, [C("vip", RuleOperator.Is, "quiza")])]);

        Assert.Equal("rules.invalid_pattern", Assert.Single(Assert.IsType<DefinitionFailure<RuleCollection>>(result).Errors).Code);
    }

    [Fact]
    public void Accion_desconocida()
    {
        var error = Assert.Single(CompileErrors(Rule("R1", 10, RuleMatch.All, [C("title", RuleOperator.Exists)], A("assign:nope", "x"))));
        Assert.Equal("rules.unknown_action", error.Code);
        Assert.Equal("Rules[0].Actions[0].ActionKey", error.Path);
    }

    [Theory]
    [InlineData("assign:priority", "abc")]
    [InlineData("assign:group_id", "  ")]
    public void Valor_de_accion_invalido(string key, string value)
    {
        var error = Assert.Single(CompileErrors(Rule("R1", 10, RuleMatch.All, [C("title", RuleOperator.Exists)], A(key, value))));
        Assert.Equal("rules.invalid_action_value", error.Code);
    }

    [Fact]
    public void Captura_sin_regex()
    {
        var error = Assert.Single(CompileErrors(Rule("R1", 10, RuleMatch.All, [C("title", RuleOperator.Contains, "x")],
            A("regex_result:branch_code", "#0"))));
        Assert.Equal("rules.capture_without_regex", error.Code);
    }

    [Fact]
    public void Id_duplicado_y_todos_los_errores_juntos()
    {
        var errors = CompileErrors(
            Rule("R1", 10, RuleMatch.All, [C("title", RuleOperator.Exists)]),
            Rule("R1", 20, RuleMatch.All, [C("nope", RuleOperator.Is, "x"), C("urgency", RuleOperator.Is, "x")],
                A("nope")));

        Assert.Equal(["rules.duplicate_id", "rules.unknown_field", "rules.invalid_pattern", "rules.unknown_action"],
            errors.Select(e => e.Code));
    }

    [Fact]
    public void Regla_sin_id_es_error()
    {
        Assert.Equal("rules.missing_id", Assert.Single(CompileErrors(Rule(" ", 10, RuleMatch.All, [C("title", RuleOperator.Exists)]))).Code);
    }

    [Fact]
    public void Regla_sin_criterios_es_valida_pero_avisa_y_nunca_coincide()
    {
        var collection = Compile(Rule("R1", 10, RuleMatch.All, [], A("assign:group_id", "x")),
            Rule("R2", 20, RuleMatch.Any, [], A("assign:group_id", "y")));

        Assert.Equal(["rules.no_criteria", "rules.no_criteria"], collection.Warnings.Select(w => w.Code));
        Assert.True(Process(collection, Facts(("title", T("x")))).NoRuleMatched);
    }

    [Fact]
    public void Errores_del_programador_son_excepciones()
    {
        RuleDefinition[] rules = [];
        Assert.Throws<ArgumentNullException>(() => RuleCollection.Compile(null!, Catalog, Actions, rules));
        Assert.Throws<ArgumentNullException>(() => RuleCollection.Compile(Options(), null!, Actions, rules));
        Assert.Throws<ArgumentNullException>(() => RuleCollection.Compile(Options(), Catalog, null!, rules));
        Assert.Throws<ArgumentNullException>(() => RuleCollection.Compile(Options(), Catalog, Actions, null!));
        Assert.Throws<ArgumentException>(() => RuleCollection.Compile(Options() with { TimeZoneId = "Marte/Olympus" }, Catalog, Actions, rules));
        Assert.Throws<ArgumentException>(() => RuleCollection.Compile(Options(), [.. Catalog, Catalog[0]], Actions, rules));
        Assert.Throws<ArgumentException>(() => RuleCollection.Compile(Options(), Catalog, Actions, [null!]));

        var collection = Compile();
        Assert.Throws<ArgumentException>(() => collection.Process(RuleFacts.Empty, RuleTrigger.OnAdd, DateTime.SpecifyKind(Now, DateTimeKind.Local)));
        Assert.Throws<ArgumentException>(() => collection.Process(RuleFacts.Empty, RuleTrigger.None, Now));
        Assert.Throws<ArgumentNullException>(() => collection.Process(null!, RuleTrigger.OnAdd, Now));
    }

    [Fact]
    public void Registro_rechaza_claves_repetidas()
    {
        Assert.Throws<ArgumentException>(() => Actions.Assign("group_id", RuleValueType.Id));
        Assert.Throws<ArgumentException>(() => RuleActionRegistry.WithBuiltIns().Assign(" ", RuleValueType.Id));
    }

    [Fact]
    public void La_definicion_viaja_en_json_con_enums_como_texto()
    {
        var rule = new RuleDefinition("R1", "Impresoras", 10, true, RuleMatch.Any, RuleTrigger.OnAdd | RuleTrigger.OnUpdate,
            [C("title", RuleOperator.RegexMatch, "^x")], [A("assign:group_id", "ti")]);

        var json = JsonSerializer.Serialize(rule);
        var back = JsonSerializer.Deserialize<RuleDefinition>(json)!;

        Assert.Contains("\"Match\":\"Any\"", json);
        Assert.Contains("\"Operator\":\"RegexMatch\"", json);
        Assert.Equal(rule.Triggers, back.Triggers);
        Assert.Equal(rule.Criteria.Single(), back.Criteria.Single());
        Assert.Equal(rule.Actions.Single(), back.Actions.Single());
        Assert.IsAssignableFrom<ISuccess<RuleCollection>>(RuleCollection.Compile(Options(), Catalog, Actions, [back], Trees));
    }
}
