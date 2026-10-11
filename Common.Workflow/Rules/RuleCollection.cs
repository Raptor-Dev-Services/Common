using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Common.Results;

namespace Common.Workflow.Rules
{
    /// <summary>
    /// Coleccion de reglas compilada: inmutable y segura entre hilos (se registra como singleton).
    /// Se obtiene con <see cref="Compile"/> y se evalua con <see cref="Process"/> o <see cref="Explain"/>.
    /// </summary>
    public sealed class RuleCollection
    {
        private static readonly Regex CaptureToken = new("#([0-9])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        private readonly CompiledRule[] _rules;
        private readonly TimeZoneInfo _zone;
        private readonly ITreeResolver? _trees;

        private RuleCollection(RuleCollectionOptions options, CompiledRule[] rules, TimeZoneInfo zone,
            ITreeResolver? trees, IReadOnlyList<DefinitionError> warnings)
        {
            Options = options;
            _rules = rules;
            _zone = zone;
            _trees = trees;
            Warnings = warnings;
        }

        /// <summary>
        /// Opciones con que se compilo.
        /// </summary>
        public RuleCollectionOptions Options { get; }

        /// <summary>
        /// Advertencias de compilacion que no impiden usar la coleccion (por ejemplo <c>rules.no_criteria</c>).
        /// </summary>
        public IReadOnlyList<DefinitionError> Warnings { get; }

        /// <summary>
        /// Ids de las reglas en orden de evaluacion (ranking ascendente; empate por id ordinal).
        /// </summary>
        public IReadOnlyList<string> RuleIds => _rules.Select(r => r.Definition.Id).ToArray();

        /// <summary>
        /// Valida las reglas contra el catalogo de criterios y el registro de acciones y las compila.
        /// </summary>
        /// <param name="options">Opciones de la coleccion.</param>
        /// <param name="catalog">Criterios que el producto declara.</param>
        /// <param name="actions">Acciones que el producto registra.</param>
        /// <param name="rules">Reglas a compilar (activas e inactivas).</param>
        /// <param name="trees">Resolutor de arboles para <c>Under</c>; obligatorio si alguna regla lo usa.</param>
        /// <returns>La coleccion, o un <see cref="DefinitionFailure{T}"/> con todos los errores.</returns>
        /// <exception cref="ArgumentException">
        /// Argumentos nulos, zona horaria desconocida, catalogo con campos repetidos o regla nula.
        /// </exception>
        public static Result<RuleCollection> Compile(RuleCollectionOptions options,
            IReadOnlyList<CriterionSpec> catalog, RuleActionRegistry actions,
            IReadOnlyList<RuleDefinition> rules, ITreeResolver? trees = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(catalog);
            ArgumentNullException.ThrowIfNull(actions);
            ArgumentNullException.ThrowIfNull(rules);

            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId ?? "UTC"); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw new ArgumentException($"Zona horaria desconocida: '{options.TimeZoneId}'.", nameof(options), ex);
            }

            var specs = new Dictionary<string, CriterionSpec>(StringComparer.Ordinal);
            foreach (var spec in catalog)
            {
                if (spec is null || string.IsNullOrWhiteSpace(spec.Field))
                    throw new ArgumentException("El catalogo tiene un criterio nulo o sin campo.", nameof(catalog));
                if (!specs.TryAdd(spec.Field, spec))
                    throw new ArgumentException($"El catalogo declara dos veces '{spec.Field}'.", nameof(catalog));
            }

            var errors = new List<DefinitionError>();
            var warnings = new List<DefinitionError>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var compiled = new List<CompiledRule>(rules.Count);

            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i] ?? throw new ArgumentException($"La regla {i} es nula.", nameof(rules));
                var path = $"Rules[{i}]";
                if (string.IsNullOrWhiteSpace(rule.Id))
                    errors.Add(new DefinitionError("rules.missing_id", path + ".Id", "La regla no tiene id."));
                else if (!ids.Add(rule.Id))
                    errors.Add(new DefinitionError("rules.duplicate_id", path + ".Id", $"El id '{rule.Id}' esta repetido."));

                var criteriaDefs = rule.Criteria ?? [];
                var criteria = new List<CompiledCriterion>(criteriaDefs.Count);
                for (var j = 0; j < criteriaDefs.Count; j++)
                {
                    var c = CompiledCriterion.Compile(j, criteriaDefs[j], specs, trees, $"{path}.Criteria[{j}]", errors);
                    if (c is not null) criteria.Add(c);
                }
                if (criteriaDefs.Count == 0)
                    warnings.Add(new DefinitionError("rules.no_criteria", path + ".Criteria",
                        $"La regla '{rule.Id}' no tiene criterios: nunca coincide."));

                var hasRegex = criteriaDefs.Any(c => c?.Operator == RuleOperator.RegexMatch);
                var actionDefs = rule.Actions ?? [];
                var compiledActions = new List<CompiledAction>(actionDefs.Count);
                for (var j = 0; j < actionDefs.Count; j++)
                {
                    var a = CompileAction(actionDefs[j], actions, hasRegex, $"{path}.Actions[{j}]", errors);
                    if (a is not null) compiledActions.Add(a);
                }

                compiled.Add(new CompiledRule(rule, criteria.ToArray(), compiledActions.ToArray()));
            }

            if (errors.Count > 0) return new DefinitionFailure<RuleCollection>(errors);

            var ordered = compiled
                .OrderBy(r => r.Definition.Ranking)
                .ThenBy(r => r.Definition.Id, StringComparer.Ordinal)
                .ToArray();
            return Result.OK(new RuleCollection(options, ordered, zone, trees, warnings.ToArray()));
        }

        private static CompiledAction? CompileAction(ActionDefinition? def, RuleActionRegistry registry, bool hasRegex,
            string path, List<DefinitionError> errors)
        {
            if (def is null || !registry.TryGet(def.ActionKey, out var spec))
            {
                errors.Add(new DefinitionError("rules.unknown_action", path + ".ActionKey",
                    $"La accion '{def?.ActionKey}' no esta registrada."));
                return null;
            }

            var raw = def.Value ?? string.Empty;
            if (spec.UsesCaptures)
            {
                if (!hasRegex)
                {
                    errors.Add(new DefinitionError("rules.capture_without_regex", path,
                        $"La accion '{spec.Key}' usa capturas pero la regla no tiene criterio RegexMatch."));
                    return null;
                }
                return new CompiledAction(spec, raw, RuleValue.Empty);
            }

            if (spec.Key == RuleActionRegistry.StopKey) return new CompiledAction(spec, raw, RuleValue.Empty);

            if (!TryParseValue(raw, spec.ValueType, out var value))
            {
                errors.Add(new DefinitionError("rules.invalid_action_value", path + ".Value",
                    $"'{raw}' no es un valor {spec.ValueType} valido para '{spec.Key}'."));
                return null;
            }
            return new CompiledAction(spec, raw, value);
        }

        /// <summary>
        /// Convierte el texto de una accion al tipo declarado.
        /// </summary>
        internal static bool TryParseValue(string raw, RuleValueType type, out RuleValue value)
        {
            var text = raw.Trim();
            value = RuleValue.Empty;
            switch (type)
            {
                case RuleValueType.Text:
                    value = RuleValue.Text(raw);
                    return true;
                case RuleValueType.Id:
                    if (text.Length == 0) return false;
                    value = RuleValue.Id(text);
                    return true;
                case RuleValueType.Number:
                    if (!CompiledCriterion.TryParseNumber(text, out var n)) return false;
                    value = RuleValue.Number(n);
                    return true;
                case RuleValueType.Bool:
                    if (!CompiledCriterion.TryParseBool(text, out var b)) return false;
                    value = RuleValue.Bool(b);
                    return true;
                case RuleValueType.Date:
                    if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                        return false;
                    value = RuleValue.Date(d);
                    return true;
                default:
                    if (!DatePattern.TryParseInstant(text, out var instant)) return false;
                    value = RuleValue.Instant(instant);
                    return true;
            }
        }

        /// <summary>
        /// Recorre las reglas activas cuyo disparo incluya <paramref name="trigger"/>, por ranking, y aplica
        /// las acciones de las que coincidan.
        /// </summary>
        /// <param name="facts">Hechos del elemento.</param>
        /// <param name="trigger">Disparo (<c>OnAdd</c>, <c>OnUpdate</c> o ambos).</param>
        /// <param name="nowUtc">El "ahora" para fechas relativas; <c>Kind = Utc</c>.</param>
        /// <returns>Cambios, reglas coincidentes, motivo de parada y advertencias.</returns>
        /// <exception cref="ArgumentException">Hechos nulos, disparo <c>None</c> o <paramref name="nowUtc"/> no UTC.</exception>
        public RuleOutput Process(RuleFacts facts, RuleTrigger trigger, DateTime nowUtc) =>
            Run(facts, trigger, nowUtc, null);

        /// <summary>
        /// Modo explicacion ("probar regla"): evalua todos los criterios de todas las reglas, sin corte,
        /// marca las que <see cref="Process"/> saltaria y devuelve tambien la salida real.
        /// </summary>
        /// <param name="facts">Hechos del elemento.</param>
        /// <param name="trigger">Disparo.</param>
        /// <param name="nowUtc">El "ahora"; <c>Kind = Utc</c>.</param>
        /// <returns>La explicacion.</returns>
        /// <exception cref="ArgumentException">Hechos nulos, disparo <c>None</c> o <paramref name="nowUtc"/> no UTC.</exception>
        public RuleExplanation Explain(RuleFacts facts, RuleTrigger trigger, DateTime nowUtc)
        {
            var entries = new List<RuleExplanationEntry>(_rules.Length);
            var output = Run(facts, trigger, nowUtc, entries);
            return new RuleExplanation(entries, output);
        }

        private RuleOutput Run(RuleFacts facts, RuleTrigger trigger, DateTime nowUtc, List<RuleExplanationEntry>? explain)
        {
            ArgumentNullException.ThrowIfNull(facts);
            if ((trigger & RuleTrigger.Any) == RuleTrigger.None)
                throw new ArgumentException("El disparo debe incluir OnAdd u OnUpdate.", nameof(trigger));
            if (nowUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("nowUtc debe tener Kind = Utc.", nameof(nowUtc));

            var warnings = new List<string>();
            var ctx = new EvalContext(nowUtc, _zone, _trees, warnings);
            var explainCtx = explain is null ? null : new EvalContext(nowUtc, _zone, _trees, []);
            var output = new RuleOutputBuilder();
            var matched = new List<MatchedRule>();
            StopReason? stopped = null;

            var input = facts;
            var inputVersion = 0;

            foreach (var rule in _rules)
            {
                var def = rule.Definition;
                RuleSkipReason? skip = !def.IsActive ? RuleSkipReason.Inactive
                    : (def.Triggers & trigger) == RuleTrigger.None ? RuleSkipReason.TriggerNotIncluded
                    : stopped is not null ? RuleSkipReason.AfterStop
                    : null;
                if (skip == RuleSkipReason.AfterStop && explain is null) break;

                if (Options.ChainOutputToInput && output.Version != inputVersion)
                {
                    input = facts.With(output.Snapshot());
                    inputVersion = output.Version;
                }

                var applied = false;
                List<string>? captures = null;
                if (skip is null)
                {
                    ctx.RuleId = def.Id;
                    captures = [];
                    if (Matches(rule, input, ctx, captures))
                    {
                        foreach (var action in rule.Actions) ApplyAction(action, input, output, captures, ctx);
                        matched.Add(new MatchedRule(def.Id, def.Ranking, captures.ToArray()));
                        applied = true;
                        if (output.StopRequested) stopped = StopReason.StopAction;
                        else if (Options.StopOnFirstMatch) stopped = StopReason.FirstMatch;
                    }
                }

                if (explain is not null) explain.Add(ExplainRule(rule, input, explainCtx!, skip, applied));
            }

            return new RuleOutput(new ReadOnlyDictionary<string, RuleValue>(output.Snapshot()), matched, stopped, warnings);
        }

        private static bool Matches(CompiledRule rule, RuleFacts input, EvalContext ctx, List<string> captures)
        {
            if (rule.Criteria.Length == 0) return false;
            if (rule.Definition.Match == RuleMatch.All)
            {
                foreach (var c in rule.Criteria)
                    if (!c.Evaluate(input.Get(c.Spec.Field), ctx, captures)) return false;
                return true;
            }
            foreach (var c in rule.Criteria)
                if (c.Evaluate(input.Get(c.Spec.Field), ctx, captures)) return true;
            return false;
        }

        private static RuleExplanationEntry ExplainRule(CompiledRule rule, RuleFacts input, EvalContext ctx,
            RuleSkipReason? skip, bool applied)
        {
            var def = rule.Definition;
            ctx.RuleId = def.Id;
            var captures = new List<string>();
            var criteria = new List<CriterionExplanation>(rule.Criteria.Length);
            foreach (var c in rule.Criteria)
            {
                var value = input.Get(c.Spec.Field);
                var result = c.Evaluate(value, ctx, captures);
                criteria.Add(new CriterionExplanation(c.Index, c.Spec.Field, c.Operator, c.Definition.Pattern, value, result));
            }
            var matched = criteria.Count > 0 && (def.Match == RuleMatch.All
                ? criteria.TrueForAll(c => c.Result)
                : criteria.Exists(c => c.Result));
            return new RuleExplanationEntry(def.Id, def.Name, def.Ranking, skip, matched, applied, criteria, captures);
        }

        private static void ApplyAction(CompiledAction action, RuleFacts input, RuleOutputBuilder output,
            List<string> captures, EvalContext ctx)
        {
            var value = action.Value;
            if (action.Spec.UsesCaptures)
            {
                var text = CaptureToken.Replace(action.Raw, m =>
                {
                    var n = m.Groups[1].Value[0] - '0';
                    return n < captures.Count ? captures[n] : string.Empty;
                });
                if (!TryParseValue(text, action.Spec.ValueType, out value))
                {
                    ctx.Warn("rules.invalid_action_value:" + ctx.RuleId);
                    return;
                }
            }
            action.Spec.Handler.Apply(new RuleActionContext(input, output, value, captures.ToArray()));
        }

        private sealed record CompiledRule(RuleDefinition Definition, CompiledCriterion[] Criteria, CompiledAction[] Actions);

        private sealed record CompiledAction(ActionSpec Spec, string Raw, RuleValue Value);
    }

    /// <summary>
    /// Reordenamiento de reglas: renumera de 10 en 10 para dejar hueco a inserciones.
    /// </summary>
    public static class RuleRanking
    {
        /// <summary>
        /// Donde queda la regla movida respecto a la destino.
        /// </summary>
        [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<Position>))]
        public enum Position
        {
            /// <summary>Antes de la regla destino.</summary>
            Before,
            /// <summary>Despues de la regla destino.</summary>
            After,
        }

        /// <summary>
        /// Mueve <paramref name="ruleId"/> antes o despues de <paramref name="targetId"/> y devuelve los
        /// rankings nuevos de toda la coleccion (10, 20, 30...).
        /// </summary>
        /// <param name="rules">Reglas de la coleccion.</param>
        /// <param name="ruleId">Regla a mover.</param>
        /// <param name="targetId">Regla de referencia.</param>
        /// <param name="position">Antes o despues de la referencia.</param>
        /// <returns>Id -> ranking nuevo.</returns>
        /// <exception cref="ArgumentException">Si algun id no existe en la coleccion.</exception>
        public static IReadOnlyDictionary<string, int> Move(IReadOnlyList<RuleDefinition> rules,
            string ruleId, string targetId, Position position)
        {
            ArgumentNullException.ThrowIfNull(rules);
            ArgumentNullException.ThrowIfNull(ruleId);
            ArgumentNullException.ThrowIfNull(targetId);

            var ordered = rules.OrderBy(r => r.Ranking).ThenBy(r => r.Id, StringComparer.Ordinal).Select(r => r.Id).ToList();
            if (!ordered.Contains(ruleId))
                throw new ArgumentException($"La regla '{ruleId}' no esta en la coleccion.", nameof(ruleId));
            if (!ordered.Contains(targetId))
                throw new ArgumentException($"La regla '{targetId}' no esta en la coleccion.", nameof(targetId));

            if (!string.Equals(ruleId, targetId, StringComparison.Ordinal))
            {
                ordered.Remove(ruleId);
                var target = ordered.IndexOf(targetId);
                ordered.Insert(position == Position.Before ? target : target + 1, ruleId);
            }

            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < ordered.Count; i++) result[ordered[i]] = (i + 1) * 10;
            return result;
        }
    }
}
