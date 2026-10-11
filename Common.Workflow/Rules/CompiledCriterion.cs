using System.Globalization;
using System.Text.RegularExpressions;

namespace Common.Workflow.Rules
{
    /// <summary>
    /// Contexto de una evaluacion: el "ahora", la zona y donde dejar advertencias.
    /// </summary>
    internal sealed class EvalContext(DateTime nowUtc, TimeZoneInfo zone, ITreeResolver? trees, List<string> warnings)
    {
        public DateTime NowUtc { get; } = nowUtc;
        public TimeZoneInfo Zone { get; } = zone;
        public ITreeResolver? Trees { get; } = trees;
        public string RuleId { get; set; } = string.Empty;

        public void Warn(string warning)
        {
            if (!warnings.Contains(warning)) warnings.Add(warning);
        }
    }

    /// <summary>
    /// Criterio validado contra el catalogo, con su patron ya convertido (numero, fecha, regex).
    /// </summary>
    internal sealed class CompiledCriterion
    {
        internal const int MaxRegexLength = 500;
        internal static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

        private const RegexOptions UserRegexOptions =
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;

        private readonly decimal _number;
        private readonly bool _bool;
        private readonly DatePattern? _date;
        private readonly Regex? _regex;

        private CompiledCriterion(int index, CriterionDefinition definition, CriterionSpec spec, string pattern,
            decimal number, bool flag, DatePattern? date, Regex? regex)
        {
            Index = index;
            Definition = definition;
            Spec = spec;
            Pattern = pattern;
            _number = number;
            _bool = flag;
            _date = date;
            _regex = regex;
        }

        public int Index { get; }
        public CriterionDefinition Definition { get; }
        public CriterionSpec Spec { get; }
        public string Pattern { get; }
        public RuleOperator Operator => Definition.Operator;
        public bool IsRegexMatch => Definition.Operator == RuleOperator.RegexMatch;

        private bool IsNegative => Definition.Operator is RuleOperator.IsNot or RuleOperator.NotContains
            or RuleOperator.RegexNotMatch or RuleOperator.NotUnder or RuleOperator.DateNotEquals or RuleOperator.NotExists;

        /// <summary>
        /// Valida el criterio y lo compila. Devuelve <c>null</c> (y agrega errores) si no es valido.
        /// </summary>
        public static CompiledCriterion? Compile(int index, CriterionDefinition definition,
            IReadOnlyDictionary<string, CriterionSpec> catalog, ITreeResolver? trees, string path, List<DefinitionError> errors)
        {
            if (definition is null)
            {
                errors.Add(new DefinitionError("rules.unknown_field", path, "Criterio nulo."));
                return null;
            }
            if (definition.Field is null || !catalog.TryGetValue(definition.Field, out var spec))
            {
                errors.Add(new DefinitionError("rules.unknown_field", path + ".Field",
                    $"El campo '{definition.Field}' no esta en el catalogo."));
                return null;
            }

            var op = definition.Operator;
            var why = !Enum.IsDefined(op) ? "operador desconocido"
                : spec.AllowedOperators?.Contains(op) != true ? "el catalogo no lo permite"
                : WhyIncompatible(op, spec, trees);
            if (why.Length > 0)
            {
                errors.Add(new DefinitionError("rules.operator_not_allowed", path + ".Operator",
                    $"El operador {op} no se permite sobre '{spec.Field}': {why}."));
                return null;
            }

            var raw = definition.Pattern ?? string.Empty;
            var pattern = raw.Trim();
            decimal number = 0;
            var flag = false;
            DatePattern? date = null;
            Regex? regex = null;
            string? invalid = null;

            switch (op)
            {
                case RuleOperator.RegexMatch or RuleOperator.RegexNotMatch:
                    if (raw.Length > MaxRegexLength)
                    {
                        invalid = $"la regex excede {MaxRegexLength} caracteres";
                        break;
                    }
                    try { regex = new Regex(raw, UserRegexOptions, RegexTimeout); }
                    catch (ArgumentException ex) { invalid = "la regex no compila: " + ex.Message; }
                    break;
                case RuleOperator.GreaterThan or RuleOperator.GreaterOrEqual or RuleOperator.LessThan or RuleOperator.LessOrEqual:
                    if (!TryParseNumber(pattern, out number)) invalid = $"'{pattern}' no es un numero invariante";
                    break;
                case RuleOperator.DateBefore or RuleOperator.DateAfter or RuleOperator.DateEquals or RuleOperator.DateNotEquals:
                    if (!DatePattern.TryParse(pattern, out date)) invalid = $"'{pattern}' no es una fecha ISO ni relativa (NOW, -2DAY)";
                    break;
                case RuleOperator.Is or RuleOperator.IsNot:
                    switch (spec.ValueType)
                    {
                        case RuleValueType.Number when !TryParseNumber(pattern, out number):
                            invalid = $"'{pattern}' no es un numero invariante";
                            break;
                        case RuleValueType.Bool when !TryParseBool(pattern, out flag):
                            invalid = $"'{pattern}' no es un booleano (true/false)";
                            break;
                        case RuleValueType.Date or RuleValueType.DateTime when !DatePattern.TryParse(pattern, out date):
                            invalid = $"'{pattern}' no es una fecha ISO ni relativa";
                            break;
                    }
                    break;
            }

            if (invalid is not null)
            {
                errors.Add(new DefinitionError("rules.invalid_pattern", path + ".Pattern", invalid + "."));
                return null;
            }

            return new CompiledCriterion(index, definition, spec, pattern, number, flag, date, regex);
        }

        private static string WhyIncompatible(RuleOperator op, CriterionSpec spec, ITreeResolver? trees)
        {
            var t = spec.ValueType;
            return op switch
            {
                RuleOperator.Contains or RuleOperator.NotContains or RuleOperator.BeginsWith or RuleOperator.EndsWith
                    or RuleOperator.RegexMatch or RuleOperator.RegexNotMatch
                    when t is not (RuleValueType.Text or RuleValueType.Id) => "solo aplica a texto o id",
                RuleOperator.Under or RuleOperator.NotUnder when t != RuleValueType.Id => "solo aplica a campos id",
                RuleOperator.Under or RuleOperator.NotUnder when string.IsNullOrWhiteSpace(spec.TreeName) => "el campo no declara TreeName",
                RuleOperator.Under or RuleOperator.NotUnder when trees is null => "la coleccion no tiene ITreeResolver",
                RuleOperator.DateBefore or RuleOperator.DateAfter or RuleOperator.DateEquals or RuleOperator.DateNotEquals
                    when t is not (RuleValueType.Date or RuleValueType.DateTime) => "solo aplica a fechas",
                RuleOperator.GreaterThan or RuleOperator.GreaterOrEqual or RuleOperator.LessThan or RuleOperator.LessOrEqual
                    when t != RuleValueType.Number => "solo aplica a numeros",
                _ => string.Empty,
            };
        }

        // Signo y punto decimal; sin separador de miles ("3,5" no es 35).
        internal const NumberStyles InvariantNumber = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint
            | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

        internal static bool TryParseNumber(string text, out decimal value) =>
            decimal.TryParse(text, InvariantNumber, CultureInfo.InvariantCulture, out value);

        internal static bool TryParseBool(string text, out bool value)
        {
            switch (text.Trim().ToLowerInvariant())
            {
                case "true" or "1": value = true; return true;
                case "false" or "0": value = false; return true;
                default: value = false; return false;
            }
        }

        /// <summary>
        /// Evalua el criterio sobre el valor del hecho. Si es un <c>RegexMatch</c> verdadero y
        /// <paramref name="captures"/> no es nulo, le agrega sus capturas.
        /// </summary>
        public bool Evaluate(RuleValue value, EvalContext ctx, List<string>? captures)
        {
            if (value.Kind == RuleValueKind.List && value.Items.Count > 0)
            {
                if (IsNegative)
                {
                    foreach (var item in value.Items)
                        if (!EvaluateSingle(item, ctx, null)) return false;
                    return true;
                }
                foreach (var item in value.Items)
                    if (EvaluateSingle(item, ctx, captures)) return true;
                return false;
            }
            return EvaluateSingle(value.Kind == RuleValueKind.List ? RuleValue.Empty : value, ctx, captures);
        }

        private bool EvaluateSingle(RuleValue v, EvalContext ctx, List<string>? captures)
        {
            var text = v.AsText ?? string.Empty;
            switch (Operator)
            {
                case RuleOperator.Is: return Is(v, ctx);
                case RuleOperator.IsNot: return !Is(v, ctx);
                case RuleOperator.Contains:
                    return Pattern.Length > 0 && !v.IsEmpty && text.Contains(Pattern, StringComparison.OrdinalIgnoreCase);
                case RuleOperator.NotContains:
                    return Pattern.Length > 0 && (v.IsEmpty || !text.Contains(Pattern, StringComparison.OrdinalIgnoreCase));
                case RuleOperator.BeginsWith:
                    return Pattern.Length > 0 && !v.IsEmpty && text.StartsWith(Pattern, StringComparison.OrdinalIgnoreCase);
                case RuleOperator.EndsWith:
                    return Pattern.Length > 0 && !v.IsEmpty && text.EndsWith(Pattern, StringComparison.OrdinalIgnoreCase);
                case RuleOperator.RegexMatch: return RegexMatch(text, ctx, captures) == true;
                case RuleOperator.RegexNotMatch: return RegexMatch(text, ctx, null) == false;
                case RuleOperator.Exists: return !v.IsEmpty;
                case RuleOperator.NotExists: return v.IsEmpty;
                case RuleOperator.Under: return !v.IsEmpty && Under(text, ctx);
                case RuleOperator.NotUnder: return v.IsEmpty || !Under(text, ctx);
                case RuleOperator.DateBefore: return CompareDate(v, ctx) < 0;
                case RuleOperator.DateAfter: return CompareDate(v, ctx) > 0;
                case RuleOperator.DateEquals: return CompareDate(v, ctx) == 0;
                case RuleOperator.DateNotEquals: return CompareDate(v, ctx) is not 0;
                case RuleOperator.GreaterThan: return v.AsNumber > _number;
                case RuleOperator.GreaterOrEqual: return v.AsNumber >= _number;
                case RuleOperator.LessThan: return v.AsNumber < _number;
                default: return v.AsNumber <= _number; // LessOrEqual
            }
        }

        private bool Is(RuleValue v, EvalContext ctx) => Spec.ValueType switch
        {
            RuleValueType.Number => v.AsNumber == _number,
            RuleValueType.Bool => (v.AsBool ?? (TryParseBool(v.AsText ?? string.Empty, out var b) ? b : null)) == _bool,
            RuleValueType.Date or RuleValueType.DateTime => CompareDate(v, ctx) == 0,
            RuleValueType.Id => string.Equals(v.AsText ?? string.Empty, Pattern, StringComparison.Ordinal),
            _ => string.Equals(v.AsText ?? string.Empty, Pattern, StringComparison.OrdinalIgnoreCase),
        };

        private bool Under(string id, EvalContext ctx) =>
            string.Equals(id, Pattern, StringComparison.Ordinal) || ctx.Trees!.IsUnder(Spec.TreeName!, id, Pattern);

        private int? CompareDate(RuleValue v, EvalContext ctx) => _date!.Compare(v, ctx.NowUtc, ctx.Zone);

        // true/false si se pudo evaluar; null si vencio el tiempo (se avisa y el criterio es falso).
        private bool? RegexMatch(string text, EvalContext ctx, List<string>? captures)
        {
            try
            {
                var match = _regex!.Match(text);
                if (!match.Success) return false;
                if (captures is not null)
                {
                    // Como preg_match_all de GLPI: por grupo y luego por coincidencia, sin la completa.
                    var matches = new List<Match>();
                    for (var m = match; m.Success; m = m.NextMatch()) matches.Add(m);
                    for (var g = 1; g < match.Groups.Count; g++)
                        foreach (var m in matches) captures.Add(m.Groups[g].Value);
                }
                return true;
            }
            catch (RegexMatchTimeoutException)
            {
                ctx.Warn("rules.regex_timeout:" + ctx.RuleId);
                return null;
            }
        }
    }

    /// <summary>
    /// Patron de fecha: absoluto (<c>2026-11-16</c> o instante ISO con zona) o relativo al "ahora"
    /// (<c>NOW</c>, <c>-2DAY</c>, <c>+3HOUR</c>). Con precision de dia o mayor compara fechas locales.
    /// </summary>
    internal sealed class DatePattern
    {
        private static readonly Regex Relative = new(@"^([+-])(\d{1,5})(MINUTE|HOUR|DAY|WEEK|MONTH|YEAR)S?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        private static readonly string[] InstantFormats =
        [
            "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mmK",
        ];

        private DateOnly? _absoluteDate;
        private DateTime? _absoluteInstant;
        private int _amount;
        private string? _unit;

        private DatePattern() { }

        public static bool TryParse(string text, out DatePattern? pattern)
        {
            pattern = null;
            if (string.Equals(text, "NOW", StringComparison.OrdinalIgnoreCase))
            {
                pattern = new DatePattern { _unit = "NOW" };
                return true;
            }
            var rel = Relative.Match(text);
            if (rel.Success)
            {
                var n = int.Parse(rel.Groups[2].Value, CultureInfo.InvariantCulture);
                pattern = new DatePattern { _amount = rel.Groups[1].Value == "-" ? -n : n, _unit = rel.Groups[3].Value.ToUpperInvariant() };
                return true;
            }
            if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                pattern = new DatePattern { _absoluteDate = d };
                return true;
            }
            if (TryParseInstant(text, out var instant))
            {
                pattern = new DatePattern { _absoluteInstant = instant };
                return true;
            }
            return false;
        }

        /// <summary>
        /// Lee un instante ISO-8601 que trae zona (<c>Z</c> u offset); sin zona no se adivina la hora de pared.
        /// </summary>
        public static bool TryParseInstant(string text, out DateTime utc)
        {
            utc = default;
            if (!(text.EndsWith('Z') || text.EndsWith('z') || HasOffset(text))) return false;
            if (!DateTimeOffset.TryParseExact(text, InstantFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var dto))
                return false;
            utc = dto.UtcDateTime;
            return true;
        }

        private static bool HasOffset(string text) =>
            text.Length > 6 && (text[^6] == '+' || text[^6] == '-') && text[^3] == ':';

        /// <summary>
        /// Compara el hecho con el patron: negativo si es anterior, 0 si igual, positivo si posterior;
        /// <c>null</c> si el hecho no es una fecha.
        /// </summary>
        public int? Compare(RuleValue fact, DateTime nowUtc, TimeZoneInfo zone)
        {
            DateOnly? refDate = null;
            DateTime? refInstant = null;
            if (_absoluteDate is { } ad) refDate = ad;
            else if (_absoluteInstant is { } ai) refInstant = ai;
            else
            {
                switch (_unit)
                {
                    case "NOW": refInstant = nowUtc; break;
                    case "MINUTE": refInstant = nowUtc.AddMinutes(_amount); break;
                    case "HOUR": refInstant = nowUtc.AddHours(_amount); break;
                    default:
                        var today = LocalDate(nowUtc, zone);
                        refDate = _unit switch
                        {
                            "DAY" => today.AddDays(_amount),
                            "WEEK" => today.AddDays(7 * _amount),
                            "MONTH" => today.AddMonths(_amount),
                            _ => today.AddYears(_amount),
                        };
                        break;
                }
            }

            if (fact.AsInstant is { } instant)
                return refDate is { } rd ? LocalDate(instant, zone).CompareTo(rd) : instant.CompareTo(refInstant!.Value);
            if (fact.AsDate is { } date)
                return date.CompareTo(refDate ?? LocalDate(refInstant!.Value, zone));
            return null;
        }

        private static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
    }
}
