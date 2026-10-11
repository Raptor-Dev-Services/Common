using System.Collections.Immutable;

namespace Common.Workflow.Rules
{
    /// <summary>
    /// Implementacion de una accion. Es pura: lee de los hechos y escribe solo en
    /// <see cref="RuleActionContext.Output"/>; sin E/S. Debe ser segura entre hilos.
    /// </summary>
    public interface IRuleActionHandler
    {
        /// <summary>
        /// Aplica la accion.
        /// </summary>
        /// <param name="context">Hechos, salida, valor ya convertido al tipo de la accion y capturas.</param>
        void Apply(RuleActionContext context);
    }

    /// <summary>
    /// Lo que recibe un <see cref="IRuleActionHandler"/>.
    /// </summary>
    /// <param name="Facts">Hechos sobre los que se evaluo la regla.</param>
    /// <param name="Output">Salida acumulada; lo unico que la accion puede modificar.</param>
    /// <param name="Value">Valor de la accion convertido a su <see cref="ActionSpec.ValueType"/> (con <c>#n</c> ya sustituido si la accion usa capturas).</param>
    /// <param name="Captures">Capturas de regex de la regla, en orden.</param>
    public sealed record RuleActionContext(RuleFacts Facts, RuleOutputBuilder Output,
        RuleValue Value, IReadOnlyList<string> Captures);

    /// <summary>
    /// Una accion que el producto pone a disposicion de sus reglas.
    /// </summary>
    /// <param name="Key">Clave unica (por ejemplo <c>assign:group_id</c> o <c>compute_priority</c>).</param>
    /// <param name="ValueType">Tipo del valor; se valida al compilar.</param>
    /// <param name="Handler">Implementacion.</param>
    /// <param name="UsesCaptures">
    /// Si el valor admite <c>#0..#9</c>: se sustituye por las capturas antes de convertirlo, y la regla
    /// debe tener un criterio <c>RegexMatch</c> (<c>rules.capture_without_regex</c>).
    /// </param>
    public sealed record ActionSpec(string Key, RuleValueType ValueType, IRuleActionHandler Handler, bool UsesCaptures = false);

    /// <summary>
    /// Registro inmutable de las acciones disponibles. Cada metodo devuelve un registro nuevo.
    /// </summary>
    public sealed class RuleActionRegistry
    {
        /// <summary>Clave de la accion integrada que detiene el recorrido.</summary>
        public const string StopKey = "stop";

        private readonly ImmutableDictionary<string, ActionSpec> _specs;

        private RuleActionRegistry(ImmutableDictionary<string, ActionSpec> specs) => _specs = specs;

        /// <summary>
        /// Registro con la accion integrada <c>stop</c>. Las acciones por campo (<c>assign:&lt;campo&gt;</c>,
        /// <c>append:&lt;campo&gt;</c>, <c>regex_result:&lt;campo&gt;</c>, <c>append_regex_result:&lt;campo&gt;</c>)
        /// se registran con <see cref="Assign"/>, <see cref="Append"/>, <see cref="RegexResult"/> y
        /// <see cref="AppendRegexResult"/>.
        /// </summary>
        /// <returns>El registro.</returns>
        public static RuleActionRegistry WithBuiltIns() =>
            new RuleActionRegistry(ImmutableDictionary.Create<string, ActionSpec>(StringComparer.Ordinal))
                .Custom(new ActionSpec(StopKey, RuleValueType.Text, StopHandler.Instance));

        /// <summary>
        /// Registra <c>assign:&lt;field&gt;</c>: escribe <c>Output[field] = valor</c>.
        /// </summary>
        /// <param name="field">Campo de salida.</param>
        /// <param name="type">Tipo del valor.</param>
        /// <returns>Registro nuevo.</returns>
        public RuleActionRegistry Assign(string field, RuleValueType type) =>
            Custom(new ActionSpec("assign:" + RequireField(field), type, new AssignHandler(field)));

        /// <summary>
        /// Registra <c>append:&lt;field&gt;</c>: agrega el valor a la lista <c>Output[field]</c>.
        /// </summary>
        /// <param name="field">Campo de salida.</param>
        /// <param name="type">Tipo de cada elemento.</param>
        /// <returns>Registro nuevo.</returns>
        public RuleActionRegistry Append(string field, RuleValueType type) =>
            Custom(new ActionSpec("append:" + RequireField(field), type, new AppendHandler(field)));

        /// <summary>
        /// Registra <c>regex_result:&lt;field&gt;</c>: asigna el texto con <c>#n</c> sustituido por las capturas.
        /// </summary>
        /// <param name="field">Campo de salida.</param>
        /// <returns>Registro nuevo.</returns>
        public RuleActionRegistry RegexResult(string field) =>
            Custom(new ActionSpec("regex_result:" + RequireField(field), RuleValueType.Text, new AssignHandler(field), UsesCaptures: true));

        /// <summary>
        /// Registra <c>append_regex_result:&lt;field&gt;</c>: concatena el texto con <c>#n</c> sustituido al
        /// valor actual del campo en la salida o, si aun no esta, en los hechos.
        /// </summary>
        /// <param name="field">Campo de salida.</param>
        /// <returns>Registro nuevo.</returns>
        public RuleActionRegistry AppendRegexResult(string field) =>
            Custom(new ActionSpec("append_regex_result:" + RequireField(field), RuleValueType.Text, new ConcatHandler(field), UsesCaptures: true));

        /// <summary>
        /// Registra una accion del producto.
        /// </summary>
        /// <param name="spec">Accion; su clave no debe estar registrada.</param>
        /// <returns>Registro nuevo.</returns>
        /// <exception cref="ArgumentException">Si la clave esta vacia o ya existe.</exception>
        public RuleActionRegistry Custom(ActionSpec spec)
        {
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentException.ThrowIfNullOrWhiteSpace(spec.Key, nameof(spec));
            ArgumentNullException.ThrowIfNull(spec.Handler, nameof(spec));
            if (_specs.ContainsKey(spec.Key))
                throw new ArgumentException($"La accion '{spec.Key}' ya esta registrada.", nameof(spec));
            return new RuleActionRegistry(_specs.Add(spec.Key, spec));
        }

        /// <summary>
        /// Busca una accion por clave.
        /// </summary>
        /// <param name="key">Clave.</param>
        /// <param name="spec">La accion, si existe.</param>
        /// <returns>Si existe.</returns>
        public bool TryGet(string key, out ActionSpec spec)
        {
            if (key is not null && _specs.TryGetValue(key, out var found))
            {
                spec = found;
                return true;
            }
            spec = null!;
            return false;
        }

        /// <summary>
        /// Claves registradas.
        /// </summary>
        public IEnumerable<string> Keys => _specs.Keys;

        private static string RequireField(string field)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(field);
            return field;
        }

        private sealed class StopHandler : IRuleActionHandler
        {
            public static readonly StopHandler Instance = new();
            public void Apply(RuleActionContext context) => context.Output.RequestStop();
        }

        private sealed class AssignHandler(string field) : IRuleActionHandler
        {
            public void Apply(RuleActionContext context) => context.Output.Set(field, context.Value);
        }

        private sealed class AppendHandler(string field) : IRuleActionHandler
        {
            public void Apply(RuleActionContext context) => context.Output.Append(field, context.Value);
        }

        private sealed class ConcatHandler(string field) : IRuleActionHandler
        {
            public void Apply(RuleActionContext context)
            {
                var current = context.Output.TryGet(field, out var v) ? v : context.Facts.Get(field);
                context.Output.Set(field, RuleValue.Text((current.AsText ?? string.Empty) + context.Value.AsText));
            }
        }
    }

    /// <summary>
    /// Salida en construccion durante <see cref="RuleCollection.Process"/>. Vive solo durante una
    /// evaluacion; no se comparte entre hilos.
    /// </summary>
    public sealed class RuleOutputBuilder
    {
        private readonly Dictionary<string, RuleValue> _changes = new(StringComparer.Ordinal);

        internal RuleOutputBuilder() { }

        internal bool StopRequested { get; private set; }

        internal int Version { get; private set; }

        /// <summary>
        /// Cambios acumulados hasta ahora (vista de solo lectura).
        /// </summary>
        public IReadOnlyDictionary<string, RuleValue> Changes => _changes;

        /// <summary>
        /// Escribe (o sobrescribe) un campo de salida.
        /// </summary>
        /// <param name="field">Campo.</param>
        /// <param name="value">Valor.</param>
        public void Set(string field, RuleValue value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(field);
            _changes[field] = value;
            Version++;
        }

        /// <summary>
        /// Agrega un valor a la lista del campo; si el campo tenia un valor simple, se convierte en lista.
        /// </summary>
        /// <param name="field">Campo.</param>
        /// <param name="value">Valor a agregar.</param>
        public void Append(string field, RuleValue value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(field);
            IEnumerable<RuleValue> current = !_changes.TryGetValue(field, out var v) || v.Kind == RuleValueKind.Empty
                ? []
                : v.Kind == RuleValueKind.List ? v.Items : [v];
            _changes[field] = RuleValue.List(current.Append(value));
            Version++;
        }

        /// <summary>
        /// Lee un campo de la salida.
        /// </summary>
        /// <param name="field">Campo.</param>
        /// <param name="value">Valor, si existe.</param>
        /// <returns>Si existe.</returns>
        public bool TryGet(string field, out RuleValue value) => _changes.TryGetValue(field, out value);

        internal void RequestStop() => StopRequested = true;

        internal Dictionary<string, RuleValue> Snapshot() => new(_changes, StringComparer.Ordinal);
    }
}
