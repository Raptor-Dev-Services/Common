using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Common.Workflow.Rules
{
    /// <summary>
    /// Hechos sobre los que se evalua una coleccion de reglas: diccionario inmutable
    /// campo -> <see cref="RuleValue"/>. Las claves distinguen mayusculas (ordinal). Un campo ausente
    /// se evalua como <see cref="RuleValue.Empty"/>.
    /// </summary>
    public sealed class RuleFacts : IReadOnlyDictionary<string, RuleValue>
    {
        private readonly Dictionary<string, RuleValue> _values;

        private RuleFacts(Dictionary<string, RuleValue> values) => _values = values;

        /// <summary>
        /// Hechos vacios.
        /// </summary>
        public static readonly RuleFacts Empty = new(new Dictionary<string, RuleValue>(StringComparer.Ordinal));

        /// <summary>
        /// Crea los hechos a partir de pares campo/valor. Si un campo se repite, gana el ultimo.
        /// </summary>
        /// <param name="values">Pares campo/valor; no nulo.</param>
        /// <returns>Los hechos.</returns>
        /// <exception cref="ArgumentNullException">Si <paramref name="values"/> es nulo.</exception>
        public static RuleFacts From(IEnumerable<KeyValuePair<string, RuleValue>> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            var dict = new Dictionary<string, RuleValue>(StringComparer.Ordinal);
            foreach (var (key, value) in values)
            {
                ArgumentNullException.ThrowIfNull(key, nameof(values));
                dict[key] = value;
            }
            return new RuleFacts(dict);
        }

        /// <summary>
        /// Devuelve unos hechos nuevos con <paramref name="overrides"/> sobrescribiendo a estos. Estos no cambian.
        /// </summary>
        /// <param name="overrides">Valores que sustituyen o agregan campos; no nulo.</param>
        /// <returns>Los hechos combinados.</returns>
        /// <exception cref="ArgumentNullException">Si <paramref name="overrides"/> es nulo.</exception>
        public RuleFacts With(IReadOnlyDictionary<string, RuleValue> overrides)
        {
            ArgumentNullException.ThrowIfNull(overrides);
            if (overrides.Count == 0) return this;
            var dict = new Dictionary<string, RuleValue>(_values, StringComparer.Ordinal);
            foreach (var (key, value) in overrides) dict[key] = value;
            return new RuleFacts(dict);
        }

        /// <summary>
        /// Valor del campo, o <see cref="RuleValue.Empty"/> si no existe.
        /// </summary>
        /// <param name="field">Campo.</param>
        /// <returns>El valor.</returns>
        public RuleValue Get(string field) => _values.TryGetValue(field, out var v) ? v : RuleValue.Empty;

        /// <inheritdoc />
        public RuleValue this[string key] => _values[key];

        /// <inheritdoc />
        public IEnumerable<string> Keys => _values.Keys;

        /// <inheritdoc />
        public IEnumerable<RuleValue> Values => _values.Values;

        /// <inheritdoc />
        public int Count => _values.Count;

        /// <inheritdoc />
        public bool ContainsKey(string key) => _values.ContainsKey(key);

        /// <inheritdoc />
        public bool TryGetValue(string key, [MaybeNullWhen(false)] out RuleValue value) => _values.TryGetValue(key, out value);

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, RuleValue>> GetEnumerator() => _values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
