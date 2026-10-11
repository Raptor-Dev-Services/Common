using System.Globalization;

namespace Common.Workflow.Folios
{
    /// <summary>
    /// Como se escribe un folio y como se reconoce lo que alguien teclea en un buscador. En un solo sitio
    /// para que todas las pantallas digan lo mismo. Portado de Zeit <c>SupportTicketFolio</c> (ADR-0094).
    /// </summary>
    /// <remarks>
    /// Los parametros se validan al construir (tambien al deserializar y en un <c>with</c>): un formato
    /// invalido lanza <see cref="ArgumentException"/>. Es inmutable y seguro entre hilos.
    /// </remarks>
    /// <param name="Prefix">
    /// Prefijo (<c>T-</c>): 0 a 8 caracteres <c>[A-Z0-9-]</c>; si no esta vacio, empieza con letra para que
    /// el parser no lo confunda con el numero.
    /// </param>
    /// <param name="MinDigits">Cifras minimas del numero, de 1 a 12; el numero crece si tiene mas.</param>
    /// <param name="PeriodSeparator">
    /// Separador entre el periodo y el numero (<c>-</c> en <c>OT-2026-00042</c>), o nulo si el folio no lleva
    /// periodo: 1 a 3 caracteres de <c>- / . _</c>.
    /// </param>
    public sealed record FolioFormat(string Prefix = "T-", int MinDigits = 6, string? PeriodSeparator = null)
    {
        /// <summary>Cifras maximas que acepta el parser: caben en un long y nadie dicta un folio mas largo.</summary>
        public const int MaxParsedDigits = 18;

        /// <summary>Prefijo del folio, validado.</summary>
        public string Prefix { get; init => field = ValidatePrefix(value); } = ValidatePrefix(Prefix);

        /// <summary>Cifras minimas del numero, validadas.</summary>
        public int MinDigits { get; init => field = ValidateMinDigits(value); } = ValidateMinDigits(MinDigits);

        /// <summary>Separador del periodo, validado; nulo si el folio no lleva periodo.</summary>
        public string? PeriodSeparator { get; init => field = ValidateSeparator(value); } = ValidateSeparator(PeriodSeparator);

        /// <summary>
        /// Escribe el folio: prefijo, periodo y separador si se da periodo, y el numero rellenado con ceros a
        /// <see cref="MinDigits"/> (crece si tiene mas cifras). Siempre con cultura invariante.
        /// </summary>
        /// <param name="number">Numero positivo.</param>
        /// <param name="period">Periodo (<c>2026</c>) o nulo. Exige <see cref="PeriodSeparator"/>.</param>
        /// <returns>El folio, p. ej. <c>T-000123</c> u <c>OT-2026-00042</c>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Si el numero no es positivo.</exception>
        /// <exception cref="ArgumentException">Si se da periodo y el formato no lo admite, o el periodo no es valido.</exception>
        public string Format(long number, string? period = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
            var digits = number.ToString("D" + MinDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            if (period is null)
                return Prefix + digits;

            if (PeriodSeparator is null)
                throw new ArgumentException("Este formato no lleva periodo (PeriodSeparator es nulo).", nameof(period));
            if (!IsValidPeriod(period))
                throw new ArgumentException($"El periodo debe tener de 1 a 16 letras o cifras ASCII: '{period}'.", nameof(period));
            return Prefix + period.ToUpperInvariant() + PeriodSeparator + digits;
        }

        /// <summary>
        /// El numero dentro de lo que se tecleo, o nulo si no es un folio de este formato. Admite, con espacios
        /// alrededor y sin distinguir mayusculas, <c>T-000123</c>, <c>t-123</c>, <c>T123</c>, <c>#123</c>,
        /// <c># 123</c> y <c>123</c>. Un texto suelto o un folio de otro prefijo no es un folio: el buscador lo
        /// dice con 422 en vez de devolver una lista vacia que parece "no hay".
        /// </summary>
        /// <param name="raw">Texto tecleado.</param>
        /// <returns>El numero positivo, o nulo.</returns>
        public long? TryParse(string? raw) => TryParseWithPeriod(raw)?.Number;

        /// <summary>
        /// Como <see cref="TryParse"/>, pero devuelve tambien el periodo si el formato lo lleva y el texto lo
        /// trae (<c>ot-2026-42</c> da <c>("2026", 42)</c>). Sin periodo en el texto, el periodo es nulo.
        /// </summary>
        /// <param name="raw">Texto tecleado.</param>
        /// <returns>El periodo (en mayusculas, o nulo) y el numero; o nulo si no es un folio.</returns>
        public (string? Period, long Number)? TryParseWithPeriod(string? raw)
        {
            var text = (raw ?? string.Empty).Trim();
            var core = Prefix.TrimEnd('-');
            if (text.StartsWith('#'))
                text = text[1..].TrimStart();
            else if (core.Length > 0 && text.StartsWith(core, StringComparison.OrdinalIgnoreCase))
                text = text[core.Length..].TrimStart('-');

            string? period = null;
            if (PeriodSeparator is not null)
            {
                var at = text.IndexOf(PeriodSeparator, StringComparison.Ordinal);
                if (at >= 0)
                {
                    period = text[..at];
                    if (!IsValidPeriod(period)) return null;
                    period = period.ToUpperInvariant();
                    text = text[(at + PeriodSeparator.Length)..];
                }
            }

            // Solo cifras ASCII: char.IsDigit aceptaria cifras arabigas o devanagari.
            if (text.Length is 0 or > MaxParsedDigits || !text.All(char.IsAsciiDigit)) return null;

            var number = long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
            return number > 0 ? (period, number) : null;
        }

        private static bool IsValidPeriod(string period) =>
            period.Length is >= 1 and <= 16 && period.All(char.IsAsciiLetterOrDigit);

        private static string ValidatePrefix(string value)
        {
            if (value is null || value.Length > 8
                || !value.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-')
                || (value.Length > 0 && !char.IsAsciiLetterUpper(value[0])))
                throw new ArgumentException(
                    $"El prefijo debe tener de 0 a 8 caracteres [A-Z0-9-] y empezar con letra: '{value}'.", nameof(Prefix));
            return value;
        }

        private static int ValidateMinDigits(int value)
        {
            if (value is < 1 or > 12)
                throw new ArgumentException($"MinDigits debe estar entre 1 y 12: {value}.", nameof(MinDigits));
            return value;
        }

        private static string? ValidateSeparator(string? value)
        {
            if (value is not null && (value.Length is 0 or > 3 || !value.All(c => c is '-' or '/' or '.' or '_')))
                throw new ArgumentException(
                    $"El separador de periodo debe tener de 1 a 3 caracteres de '- / . _': '{value}'.", nameof(PeriodSeparator));
            return value;
        }
    }
}
