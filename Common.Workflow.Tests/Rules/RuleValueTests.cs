using Common.Workflow.Rules;

namespace Common.Workflow.Tests.Rules;

public class RuleValueTests
{
    [Fact]
    public void Default_y_texto_nulo_son_vacio()
    {
        Assert.Equal(RuleValue.Empty, default);
        Assert.Equal(RuleValue.Empty, RuleValue.Text(null));
        Assert.True(RuleValue.Empty.IsEmpty);
        Assert.Null(RuleValue.Empty.AsText);
        Assert.True(RuleValue.List([]).IsEmpty);
        Assert.False(RuleValue.Bool(false).IsEmpty);
    }

    [Fact]
    public void Las_listas_se_comparan_por_contenido()
    {
        var a = RuleValue.List([RuleValue.Id("x"), RuleValue.Number(1)]);
        var b = RuleValue.List([RuleValue.Id("x"), RuleValue.Number(1)]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, RuleValue.List([RuleValue.Id("x")]));
        Assert.NotEqual(RuleValue.Id("x"), RuleValue.Text("x"));
        Assert.Equal("x,1", a.AsText);
        Assert.Equal("[x, 1]", a.ToString());
    }

    [Fact]
    public void Representacion_invariante()
    {
        Assert.Equal("3.5", RuleValue.Number(3.5m).AsText);
        Assert.Equal("true", RuleValue.Bool(true).AsText);
        Assert.Equal("2026-11-16", RuleValue.Date(new DateOnly(2026, 11, 16)).AsText);
        Assert.Equal("2026-11-17T17:00:00Z", RuleValue.Instant(new DateTime(2026, 11, 17, 17, 0, 0, DateTimeKind.Utc)).AsText);
        Assert.Equal(new DateOnly(2026, 11, 16), RuleValue.Date(new DateOnly(2026, 11, 16)).AsDate);
        Assert.Null(RuleValue.Text("x").AsInstant);
        Assert.Null(RuleValue.Text("x").AsBool);
        Assert.Equal("(vacio)", RuleValue.Empty.ToString());
    }

    [Fact]
    public void Errores_del_programador()
    {
        Assert.Throws<ArgumentException>(() => RuleValue.Instant(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local)));
        Assert.Throws<ArgumentException>(() => RuleValue.Instant(new DateTime(2026, 1, 1)));
        Assert.Throws<ArgumentNullException>(() => RuleValue.Id(null!));
        Assert.Throws<ArgumentNullException>(() => RuleValue.List(null!));
        Assert.Throws<ArgumentNullException>(() => RuleFacts.From(null!));
    }

    [Fact]
    public void With_sobrescribe_sin_tocar_los_originales()
    {
        var facts = RuleFacts.From([KeyValuePair.Create("a", RuleValue.Text("1")), KeyValuePair.Create("b", RuleValue.Text("2"))]);

        var merged = facts.With(new Dictionary<string, RuleValue> { ["b"] = RuleValue.Text("3"), ["c"] = RuleValue.Text("4") });

        Assert.Equal(RuleValue.Text("2"), facts["b"]);
        Assert.False(facts.ContainsKey("c"));
        Assert.Equal(RuleValue.Text("3"), merged["b"]);
        Assert.Equal(3, merged.Count);
        Assert.Equal(RuleValue.Empty, merged.Get("zzz"));
        Assert.Same(facts, facts.With(new Dictionary<string, RuleValue>()));
    }
}
