using System.Globalization;

namespace Common.Workflow.Rules
{
    /// <summary>
    /// Valor de un hecho o de una accion: texto, numero, instante UTC, fecha local, id, booleano o
    /// lista (campo multivaluado). Es inmutable; <c>default</c> equivale a <see cref="Empty"/>.
    /// </summary>
    public readonly record struct RuleValue
    {
        private readonly string? _text;
        private readonly decimal _number;
        private readonly DateTime _instant;
        private readonly DateOnly _date;
        private readonly bool _bool;
        private readonly RuleValue[]? _items;

        private RuleValue(RuleValueKind kind, string? text = null, decimal number = 0, DateTime instant = default,
            DateOnly date = default, bool flag = false, RuleValue[]? items = null)
        {
            Kind = kind;
            _text = text;
            _number = number;
            _instant = instant;
            _date = date;
            _bool = flag;
            _items = items;
        }

        /// <summary>
        /// Valor vacio: un campo ausente o nulo.
        /// </summary>
        public static readonly RuleValue Empty = default;

        /// <summary>
        /// Clase de valor que lleva.
        /// </summary>
        public RuleValueKind Kind { get; }

        /// <summary>
        /// Crea un valor de texto. <c>null</c> da <see cref="Empty"/>.
        /// </summary>
        /// <param name="v">Texto.</param>
        /// <returns>El valor.</returns>
        public static RuleValue Text(string? v) => v is null ? Empty : new RuleValue(RuleValueKind.Text, text: v);

        /// <summary>
        /// Crea un valor numerico.
        /// </summary>
        /// <param name="v">Numero.</param>
        /// <returns>El valor.</returns>
        public static RuleValue Number(decimal v) => new(RuleValueKind.Number, number: v);

        /// <summary>
        /// Crea un instante.
        /// </summary>
        /// <param name="utc">Instante con <c>Kind = Utc</c>.</param>
        /// <returns>El valor.</returns>
        /// <exception cref="ArgumentException">Si <paramref name="utc"/> no es UTC.</exception>
        public static RuleValue Instant(DateTime utc)
        {
            if (utc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("El instante debe tener Kind = Utc.", nameof(utc));
            return new RuleValue(RuleValueKind.Instant, instant: utc);
        }

        /// <summary>
        /// Crea una fecha de negocio local de la sucursal.
        /// </summary>
        /// <param name="v">Fecha.</param>
        /// <returns>El valor.</returns>
        public static RuleValue Date(DateOnly v) => new(RuleValueKind.Date, date: v);

        /// <summary>
        /// Crea un identificador.
        /// </summary>
        /// <param name="id">Identificador; no nulo.</param>
        /// <returns>El valor.</returns>
        /// <exception cref="ArgumentNullException">Si <paramref name="id"/> es nulo.</exception>
        public static RuleValue Id(string id)
        {
            ArgumentNullException.ThrowIfNull(id);
            return new RuleValue(RuleValueKind.Id, text: id);
        }

        /// <summary>
        /// Crea un booleano.
        /// </summary>
        /// <param name="v">Booleano.</param>
        /// <returns>El valor.</returns>
        public static RuleValue Bool(bool v) => new(RuleValueKind.Bool, flag: v);

        /// <summary>
        /// Crea una lista (campo multivaluado). Una lista vacia equivale a campo vacio.
        /// </summary>
        /// <param name="items">Valores; no nulo.</param>
        /// <returns>El valor.</returns>
        /// <exception cref="ArgumentNullException">Si <paramref name="items"/> es nulo.</exception>
        public static RuleValue List(IEnumerable<RuleValue> items)
        {
            ArgumentNullException.ThrowIfNull(items);
            return new RuleValue(RuleValueKind.List, items: items.ToArray());
        }

        /// <summary>
        /// Verdadero si no hay valor: <see cref="Empty"/>, texto o id en blanco, o lista sin elementos.
        /// </summary>
        public bool IsEmpty => Kind switch
        {
            RuleValueKind.Empty => true,
            RuleValueKind.Text or RuleValueKind.Id => string.IsNullOrWhiteSpace(_text),
            RuleValueKind.List => _items!.Length == 0,
            _ => false,
        };

        /// <summary>
        /// Elementos de una lista; vacio para cualquier otra clase de valor.
        /// </summary>
        public IReadOnlyList<RuleValue> Items => _items ?? [];

        /// <summary>
        /// Representacion como texto: el texto o id tal cual, el numero y el booleano en cultura
        /// invariante, el instante en ISO-8601 con <c>Z</c>, la fecha como <c>yyyy-MM-dd</c>, y la lista
        /// separada por comas. <c>null</c> si esta vacio.
        /// </summary>
        public string? AsText => Kind switch
        {
            RuleValueKind.Text or RuleValueKind.Id => _text,
            RuleValueKind.Number => _number.ToString(CultureInfo.InvariantCulture),
            RuleValueKind.Bool => _bool ? "true" : "false",
            RuleValueKind.Instant => _instant.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture),
            RuleValueKind.Date => _date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            RuleValueKind.List => string.Join(",", _items!.Select(i => i.AsText)),
            _ => null,
        };

        /// <summary>
        /// El numero, o el texto si se puede leer como numero invariante; si no, <c>null</c>.
        /// </summary>
        public decimal? AsNumber => Kind switch
        {
            RuleValueKind.Number => _number,
            RuleValueKind.Text or RuleValueKind.Id when decimal.TryParse(_text, CompiledCriterion.InvariantNumber,
                CultureInfo.InvariantCulture, out var n) => n,
            _ => null,
        };

        /// <summary>
        /// El instante UTC si es <see cref="RuleValueKind.Instant"/>; si no, <c>null</c>.
        /// </summary>
        public DateTime? AsInstant => Kind == RuleValueKind.Instant ? _instant : null;

        /// <summary>
        /// La fecha si es <see cref="RuleValueKind.Date"/>; si no, <c>null</c>.
        /// </summary>
        public DateOnly? AsDate => Kind == RuleValueKind.Date ? _date : null;

        /// <summary>
        /// El booleano si es <see cref="RuleValueKind.Bool"/>; si no, <c>null</c>.
        /// </summary>
        public bool? AsBool => Kind == RuleValueKind.Bool ? _bool : null;

        /// <summary>
        /// Igualdad estructural (las listas se comparan elemento a elemento).
        /// </summary>
        /// <param name="other">Otro valor.</param>
        /// <returns>Verdadero si son iguales.</returns>
        public bool Equals(RuleValue other)
        {
            if (Kind != other.Kind) return false;
            return Kind switch
            {
                RuleValueKind.Empty => true,
                RuleValueKind.Text or RuleValueKind.Id => string.Equals(_text, other._text, StringComparison.Ordinal),
                RuleValueKind.Number => _number == other._number,
                RuleValueKind.Instant => _instant == other._instant,
                RuleValueKind.Date => _date == other._date,
                RuleValueKind.Bool => _bool == other._bool,
                _ => _items!.AsSpan().SequenceEqual(other._items),
            };
        }

        /// <summary>
        /// Codigo hash coherente con <see cref="Equals(RuleValue)"/>.
        /// </summary>
        /// <returns>El hash.</returns>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Kind);
            if (Kind == RuleValueKind.List)
            {
                foreach (var item in _items!) hash.Add(item);
            }
            else
            {
                hash.Add(AsText, StringComparer.Ordinal);
            }
            return hash.ToHashCode();
        }

        /// <summary>
        /// Texto legible: <see cref="AsText"/>, con la lista entre corchetes.
        /// </summary>
        /// <returns>El texto.</returns>
        public override string ToString() => Kind switch
        {
            RuleValueKind.Empty => "(vacio)",
            RuleValueKind.List => "[" + string.Join(", ", _items!.Select(i => i.ToString())) + "]",
            _ => AsText ?? string.Empty,
        };
    }
}
