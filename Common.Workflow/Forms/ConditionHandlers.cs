using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Common.Workflow.Forms
{
    /// <summary>
    /// Evalua las condiciones sobre las respuestas de un tipo de pregunta. Debe ser puro y seguro entre
    /// hilos. <see cref="ValueOperator.Visible"/>/<see cref="ValueOperator.NotVisible"/> los resuelve el motor:
    /// no hace falta declararlos.
    /// </summary>
    public interface IConditionHandler
    {
        /// <summary>Operadores que admite el tipo.</summary>
        IReadOnlySet<ValueOperator> SupportedOperators { get; }

        /// <summary>
        /// Si la respuesta cuenta como vacia (para <c>empty</c>/<c>not_empty</c> y la obligatoriedad).
        /// </summary>
        /// <param name="answer">Respuesta.</param>
        /// <returns>Si esta vacia.</returns>
        bool IsEmpty(AnswerValue answer);

        /// <summary>
        /// Aplica el operador a una respuesta de una pregunta visible.
        /// </summary>
        /// <param name="answer">Respuesta.</param>
        /// <param name="op">Operador (uno de <see cref="SupportedOperators"/>).</param>
        /// <param name="value">Valor de la condicion.</param>
        /// <returns>Si la condicion se cumple.</returns>
        bool Apply(AnswerValue answer, ValueOperator op, string? value);

        /// <summary>
        /// Valida el valor de una condicion al guardar el formulario. La regex la valida el motor.
        /// </summary>
        /// <param name="op">Operador.</param>
        /// <param name="value">Valor.</param>
        /// <returns>El error, o <c>null</c> si es valido. El motor sustituye la ruta.</returns>
        DefinitionError? ValidateValue(ValueOperator op, string? value);
    }

    /// <summary>
    /// Registro inmutable de manejadores por tipo de pregunta. <see cref="Register"/> devuelve uno nuevo.
    /// </summary>
    public sealed class ConditionHandlerRegistry
    {
        private readonly ImmutableDictionary<string, IConditionHandler> _handlers;

        private ConditionHandlerRegistry(ImmutableDictionary<string, IConditionHandler> handlers) => _handlers = handlers;

        /// <summary>
        /// Registro con los manejadores integrados: <c>text</c>, <c>long_text</c>, <c>email</c>, <c>number</c>,
        /// <c>date</c>, <c>datetime</c>, <c>time</c>, <c>single_choice</c>, <c>multiple_choice</c> y <c>file</c>.
        /// </summary>
        /// <returns>El registro.</returns>
        public static ConditionHandlerRegistry WithBuiltIns()
        {
            var text = new TextConditionHandler();
            return new ConditionHandlerRegistry(ImmutableDictionary.Create<string, IConditionHandler>(StringComparer.Ordinal))
                .Register("text", text)
                .Register("long_text", text)
                .Register("email", text)
                .Register("number", new NumberConditionHandler())
                .Register("date", new TemporalConditionHandler(AnswerKind.Date))
                .Register("datetime", new TemporalConditionHandler(AnswerKind.DateTime))
                .Register("time", new TemporalConditionHandler(AnswerKind.Time))
                .Register("single_choice", new SingleChoiceConditionHandler())
                .Register("multiple_choice", new MultipleChoiceConditionHandler())
                .Register("file", new FileConditionHandler());
        }

        /// <summary>
        /// Registra (o sustituye) el manejador de un tipo de pregunta.
        /// </summary>
        /// <param name="questionType">Tipo de pregunta.</param>
        /// <param name="handler">Manejador.</param>
        /// <returns>Registro nuevo.</returns>
        /// <exception cref="ArgumentException">Tipo vacio o manejador nulo.</exception>
        public ConditionHandlerRegistry Register(string questionType, IConditionHandler handler)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(questionType);
            ArgumentNullException.ThrowIfNull(handler);
            return new ConditionHandlerRegistry(_handlers.SetItem(questionType, handler));
        }

        /// <summary>
        /// Manejador de un tipo, o <c>null</c>.
        /// </summary>
        /// <param name="questionType">Tipo de pregunta.</param>
        /// <returns>El manejador.</returns>
        public IConditionHandler? Find(string? questionType) =>
            questionType is not null && _handlers.TryGetValue(questionType, out var h) ? h : null;

        /// <summary>Tipos registrados.</summary>
        public IEnumerable<string> QuestionTypes => _handlers.Keys;
    }

    /// <summary>
    /// Regex escrita por un usuario del tenant: CultureInvariant, timeout de 100 ms, maximo 500 caracteres.
    /// Las compiladas se guardan en una cache acotada y segura entre hilos.
    /// </summary>
    internal static class FormRegex
    {
        public const int MaxLength = 500;
        private const int MaxCached = 1024;
        private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
        private static readonly ConcurrentDictionary<string, Regex?> Cache = new(StringComparer.Ordinal);

        /// <summary>Motivo por el que el patron no sirve, o <c>null</c> si sirve.</summary>
        public static string? Validate(string? pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return "la regex esta vacia";
            if (pattern.Length > MaxLength) return $"la regex excede {MaxLength} caracteres";
            try
            {
                _ = new Regex(pattern, RegexOptions.CultureInvariant, Timeout);
                return null;
            }
            catch (ArgumentException ex)
            {
                return "la regex no compila: " + ex.Message;
            }
        }

        /// <summary>
        /// Verdadero/falso si se evaluo; <c>null</c> si el patron no sirve o vencio el tiempo.
        /// </summary>
        public static bool? IsMatch(string? pattern, string input)
        {
            var regex = Get(pattern);
            if (regex is null) return null;
            try { return regex.IsMatch(input); }
            catch (RegexMatchTimeoutException) { return null; }
        }

        private static Regex? Get(string? pattern)
        {
            if (pattern is null) return null;
            if (Cache.TryGetValue(pattern, out var cached)) return cached;
            var compiled = Validate(pattern) is null ? new Regex(pattern, RegexOptions.CultureInvariant, Timeout) : null;
            if (Cache.Count >= MaxCached) Cache.Clear();
            Cache.TryAdd(pattern, compiled);
            return compiled;
        }
    }

    /// <summary>Base comun: <c>empty</c>/<c>not_empty</c> y el error de valor.</summary>
    internal abstract class BuiltInHandler : IConditionHandler
    {
        public abstract IReadOnlySet<ValueOperator> SupportedOperators { get; }

        public virtual bool IsEmpty(AnswerValue answer) => answer.IsEmpty;

        public bool Apply(AnswerValue answer, ValueOperator op, string? value) => op switch
        {
            ValueOperator.Empty => IsEmpty(answer),
            ValueOperator.NotEmpty => !IsEmpty(answer),
            _ => SupportedOperators.Contains(op) && ApplyValue(answer, op, value),
        };

        protected abstract bool ApplyValue(AnswerValue answer, ValueOperator op, string? value);

        public virtual DefinitionError? ValidateValue(ValueOperator op, string? value) => null;

        protected static DefinitionError InvalidValue(string message) => new("forms.invalid_value", string.Empty, message);

        protected static bool Compare(int cmp, ValueOperator op) => op switch
        {
            ValueOperator.Equals => cmp == 0,
            ValueOperator.NotEquals => cmp != 0,
            ValueOperator.GreaterThan => cmp > 0,
            ValueOperator.GreaterThanOrEquals => cmp >= 0,
            ValueOperator.LessThan => cmp < 0,
            ValueOperator.LessThanOrEquals => cmp <= 0,
            _ => false,
        };

        protected static readonly ValueOperator[] Comparisons =
        [
            ValueOperator.Equals, ValueOperator.NotEquals, ValueOperator.GreaterThan, ValueOperator.GreaterThanOrEquals,
            ValueOperator.LessThan, ValueOperator.LessThanOrEquals, ValueOperator.Empty, ValueOperator.NotEmpty,
        ];
    }

    /// <summary>
    /// <c>text</c>, <c>long_text</c>, <c>email</c>: minusculas invariantes y longitud en caracteres de texto.
    /// </summary>
    internal sealed class TextConditionHandler : BuiltInHandler
    {
        private static readonly IReadOnlySet<ValueOperator> Ops = new HashSet<ValueOperator>
        {
            ValueOperator.Equals, ValueOperator.NotEquals, ValueOperator.Contains, ValueOperator.NotContains,
            ValueOperator.LengthGreaterThan, ValueOperator.LengthGreaterThanOrEquals,
            ValueOperator.LengthLessThan, ValueOperator.LengthLessThanOrEquals,
            ValueOperator.MatchRegex, ValueOperator.NotMatchRegex, ValueOperator.Empty, ValueOperator.NotEmpty,
        };

        public override IReadOnlySet<ValueOperator> SupportedOperators => Ops;

        protected override bool ApplyValue(AnswerValue answer, ValueOperator op, string? value)
        {
            var raw = answer.AsText ?? string.Empty;
            var a = raw.ToLowerInvariant();
            var b = (value ?? string.Empty).ToLowerInvariant();
            switch (op)
            {
                case ValueOperator.Equals: return a == b;
                case ValueOperator.NotEquals: return a != b;
                case ValueOperator.Contains: return a.Contains(b, StringComparison.Ordinal);
                case ValueOperator.NotContains: return !a.Contains(b, StringComparison.Ordinal);
                case ValueOperator.MatchRegex: return FormRegex.IsMatch(value, raw) == true;
                case ValueOperator.NotMatchRegex: return FormRegex.IsMatch(value, raw) == false;
            }
            if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var length)) return false;
            var actual = new StringInfo(raw).LengthInTextElements;
            return op switch
            {
                ValueOperator.LengthGreaterThan => actual > length,
                ValueOperator.LengthGreaterThanOrEquals => actual >= length,
                ValueOperator.LengthLessThan => actual < length,
                _ => actual <= length,
            };
        }

        public override DefinitionError? ValidateValue(ValueOperator op, string? value) =>
            op is ValueOperator.LengthGreaterThan or ValueOperator.LengthGreaterThanOrEquals
                or ValueOperator.LengthLessThan or ValueOperator.LengthLessThanOrEquals
            && !int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)
                ? InvalidValue($"'{value}' no es una longitud entera.")
                : null;
    }

    /// <summary>
    /// <c>number</c>: decimal invariante. Respuesta vacia: toda comparacion es falsa (GLPI la tomaba como 0).
    /// </summary>
    internal sealed class NumberConditionHandler : BuiltInHandler
    {
        private static readonly IReadOnlySet<ValueOperator> Ops = Comparisons.ToHashSet();

        public override IReadOnlySet<ValueOperator> SupportedOperators => Ops;

        protected override bool ApplyValue(AnswerValue answer, ValueOperator op, string? value) =>
            answer.AsNumber is { } a && TryParse(value, out var b) && Compare(a.CompareTo(b), op);

        public override DefinitionError? ValidateValue(ValueOperator op, string? value) =>
            op is ValueOperator.Empty or ValueOperator.NotEmpty || TryParse(value, out _)
                ? null
                : InvalidValue($"'{value}' no es un numero invariante.");

        private static bool TryParse(string? text, out decimal value) =>
            decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint
                | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// <c>date</c>, <c>datetime</c>, <c>time</c>: valores locales sin zona. Respuesta vacia: comparacion falsa.
    /// </summary>
    internal sealed class TemporalConditionHandler(AnswerKind kind) : BuiltInHandler
    {
        private static readonly IReadOnlySet<ValueOperator> Ops = Comparisons.ToHashSet();
        private static readonly string[] DateTimeFormats =
            ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"];
        private static readonly string[] TimeFormats = ["HH:mm", "HH:mm:ss"];

        public override IReadOnlySet<ValueOperator> SupportedOperators => Ops;

        protected override bool ApplyValue(AnswerValue answer, ValueOperator op, string? value)
        {
            var inv = CultureInfo.InvariantCulture;
            switch (kind)
            {
                case AnswerKind.Date:
                    return answer.AsDate is { } d
                        && DateOnly.TryParseExact(value, "yyyy-MM-dd", inv, DateTimeStyles.None, out var bd)
                        && Compare(d.CompareTo(bd), op);
                case AnswerKind.DateTime:
                    return answer.AsDateTime is { } dt
                        && DateTime.TryParseExact(value, DateTimeFormats, inv, DateTimeStyles.None, out var bdt)
                        && Compare(dt.CompareTo(bdt), op);
                default:
                    return answer.AsTime is { } t
                        && TimeOnly.TryParseExact(value, TimeFormats, inv, DateTimeStyles.None, out var bt)
                        && Compare(t.CompareTo(bt), op);
            }
        }

        public override DefinitionError? ValidateValue(ValueOperator op, string? value)
        {
            if (op is ValueOperator.Empty or ValueOperator.NotEmpty) return null;
            var inv = CultureInfo.InvariantCulture;
            var ok = kind switch
            {
                AnswerKind.Date => DateOnly.TryParseExact(value, "yyyy-MM-dd", inv, DateTimeStyles.None, out _),
                AnswerKind.DateTime => DateTime.TryParseExact(value, DateTimeFormats, inv, DateTimeStyles.None, out _),
                _ => TimeOnly.TryParseExact(value, TimeFormats, inv, DateTimeStyles.None, out _),
            };
            return ok ? null : InvalidValue($"'{value}' no es un valor {kind} valido.");
        }
    }

    /// <summary>
    /// Claves de opcion separadas por coma, recortadas y sin vacias.
    /// </summary>
    internal static class ChoiceKeys
    {
        public static string[] Parse(string? value) =>
            (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// <c>single_choice</c>: por clave de opcion. <c>contains</c> = la clave elegida esta en la lista del valor.
    /// </summary>
    internal sealed class SingleChoiceConditionHandler : BuiltInHandler
    {
        private static readonly IReadOnlySet<ValueOperator> Ops = new HashSet<ValueOperator>
        {
            ValueOperator.Equals, ValueOperator.NotEquals, ValueOperator.Contains, ValueOperator.NotContains,
            ValueOperator.Empty, ValueOperator.NotEmpty,
        };

        public override IReadOnlySet<ValueOperator> SupportedOperators => Ops;

        protected override bool ApplyValue(AnswerValue answer, ValueOperator op, string? value)
        {
            var key = answer.Keys.Count > 0 ? answer.Keys[0] : string.Empty;
            var equals = string.Equals(key, (value ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
            var inList = key.Length > 0 && ChoiceKeys.Parse(value).Contains(key, StringComparer.OrdinalIgnoreCase);
            return op switch
            {
                ValueOperator.Equals => equals,
                ValueOperator.NotEquals => !equals,
                ValueOperator.Contains => inList,
                _ => !inList,
            };
        }
    }

    /// <summary>
    /// <c>multiple_choice</c>: conjuntos de claves sin orden. <c>contains</c> = incluye todas las del valor;
    /// <c>match_regex</c> = todas coinciden; <c>not_match_regex</c> = ninguna.
    /// </summary>
    internal sealed class MultipleChoiceConditionHandler : BuiltInHandler
    {
        private static readonly IReadOnlySet<ValueOperator> Ops = new HashSet<ValueOperator>
        {
            ValueOperator.Equals, ValueOperator.NotEquals, ValueOperator.Contains, ValueOperator.NotContains,
            ValueOperator.MatchRegex, ValueOperator.NotMatchRegex, ValueOperator.Empty, ValueOperator.NotEmpty,
        };

        public override IReadOnlySet<ValueOperator> SupportedOperators => Ops;

        public override bool IsEmpty(AnswerValue answer) => answer.Keys.Count == 0;

        protected override bool ApplyValue(AnswerValue answer, ValueOperator op, string? value)
        {
            var keys = answer.Keys.Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);
            if (op is ValueOperator.MatchRegex or ValueOperator.NotMatchRegex)
            {
                if (keys.Count == 0) return op == ValueOperator.NotMatchRegex;
                var matches = 0;
                foreach (var key in keys)
                {
                    var m = FormRegex.IsMatch(value, key);
                    if (m is null) return false;   // patron invalido o tiempo vencido: falla cerrado
                    if (m.Value) matches++;
                }
                return op == ValueOperator.MatchRegex ? matches == keys.Count : matches == 0;
            }

            var wanted = ChoiceKeys.Parse(value).ToHashSet(StringComparer.Ordinal);
            return op switch
            {
                ValueOperator.Equals => keys.SetEquals(wanted),
                ValueOperator.NotEquals => !keys.SetEquals(wanted),
                ValueOperator.Contains => wanted.IsSubsetOf(keys),
                _ => !wanted.IsSubsetOf(keys),
            };
        }
    }

    /// <summary>
    /// <c>file</c>: solo <c>empty</c>/<c>not_empty</c>.
    /// </summary>
    internal sealed class FileConditionHandler : BuiltInHandler
    {
        private static readonly IReadOnlySet<ValueOperator> Ops = new HashSet<ValueOperator> { ValueOperator.Empty, ValueOperator.NotEmpty };

        public override IReadOnlySet<ValueOperator> SupportedOperators => Ops;

        protected override bool ApplyValue(AnswerValue answer, ValueOperator op, string? value) => false;
    }
}
