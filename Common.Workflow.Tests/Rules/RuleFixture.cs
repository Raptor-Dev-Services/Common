using Common.Results;
using Common.Workflow.Rules;

namespace Common.Workflow.Tests.Rules;

/// <summary>
/// Catalogo, acciones y arbol de los vectores de §12 del SRS CMN-C03.
/// </summary>
internal static class RuleFixture
{
    public const string Zone = "America/Mexico_City";
    public static readonly DateTime Now = new(2026, 11, 17, 17, 0, 0, DateTimeKind.Utc);

    public static readonly IReadOnlySet<RuleOperator> AllOperators = Enum.GetValues<RuleOperator>().ToHashSet();

    public static readonly IReadOnlyList<CriterionSpec> Catalog =
    [
        new("title", RuleValueType.Text, AllOperators),
        new("content", RuleValueType.Text, AllOperators),
        new("requester_groups", RuleValueType.Id, AllOperators, IsMultiValued: true),
        new("location_id", RuleValueType.Id, AllOperators, TreeName: "locations"),
        new("urgency", RuleValueType.Number, AllOperators),
        new("category_id", RuleValueType.Id, AllOperators),
        new("opened_at", RuleValueType.DateTime, AllOperators),
        new("tags", RuleValueType.Text, AllOperators, IsMultiValued: true),
    ];

    public static readonly RuleActionRegistry Actions = RuleActionRegistry.WithBuiltIns()
        .Assign("category_id", RuleValueType.Id)
        .Assign("group_id", RuleValueType.Id)
        .Assign("priority", RuleValueType.Number)
        .RegexResult("branch_code");

    public static readonly ITreeResolver Trees = new LocationTree();

    public static RuleCollectionOptions Options(bool stopOnFirstMatch = false, bool chain = false) =>
        new("tickets", stopOnFirstMatch, chain, Zone);

    public static RuleDefinition Rule(string id, int ranking, RuleMatch match,
        CriterionDefinition[] criteria, params ActionDefinition[] actions) =>
        new(id, "Regla " + id, ranking, true, match, RuleTrigger.Any, criteria, actions);

    public static CriterionDefinition C(string field, RuleOperator op, string pattern = "") => new(field, op, pattern);

    public static ActionDefinition A(string key, string value = "") => new(key, value);

    public static RuleCollection Compile(RuleCollectionOptions options, params RuleDefinition[] rules)
    {
        var result = RuleCollection.Compile(options, Catalog, Actions, rules, Trees);
        if (result is DefinitionFailure<RuleCollection> f) Assert.Fail(f.Message);
        return Assert.IsAssignableFrom<ISuccess<RuleCollection>>(result).Data;
    }

    public static RuleCollection Compile(params RuleDefinition[] rules) => Compile(Options(), rules);

    public static IReadOnlyList<DefinitionError> CompileErrors(params RuleDefinition[] rules)
    {
        var result = RuleCollection.Compile(Options(), Catalog, Actions, rules, Trees);
        return Assert.IsType<DefinitionFailure<RuleCollection>>(result).Errors;
    }

    public static RuleFacts Facts(params (string Field, RuleValue Value)[] values) =>
        RuleFacts.From(values.Select(v => KeyValuePair.Create(v.Field, v.Value)));

    public static RuleValue T(string text) => RuleValue.Text(text);

    public static RuleValue Ids(params string[] ids) => RuleValue.List(ids.Select(RuleValue.Id));

    public static RuleValue Texts(params string[] texts) => RuleValue.List(texts.Select(RuleValue.Text));

    public static RuleOutput Process(RuleCollection collection, RuleFacts facts) =>
        collection.Process(facts, RuleTrigger.OnAdd, Now);

    private sealed class LocationTree : ITreeResolver
    {
        private static readonly Dictionary<string, string> Parent = new(StringComparer.Ordinal)
        {
            ["norte-planta1"] = "norte",
            ["norte-planta1-sala"] = "norte-planta1",
            ["sur"] = "raiz",
            ["norte"] = "raiz",
        };

        public bool IsUnder(string treeName, string id, string ancestorId)
        {
            if (treeName != "locations") return false;
            for (var current = id; Parent.TryGetValue(current, out var parent); current = parent)
                if (parent == ancestorId) return true;
            return false;
        }
    }
}
