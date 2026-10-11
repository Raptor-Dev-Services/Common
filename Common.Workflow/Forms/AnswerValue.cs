using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Common.Workflow.Forms
{
    /// <summary>
    /// Clase de respuesta que lleva un <see cref="AnswerValue"/>.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<AnswerKind>))]
    public enum AnswerKind
    {
        /// <summary>Sin respuesta.</summary>
        None,
        /// <summary>Texto.</summary>
        Text,
        /// <summary>Numero.</summary>
        Number,
        /// <summary>Una opcion (por clave).</summary>
        Choice,
        /// <summary>Varias opciones (por clave).</summary>
        Choices,
        /// <summary>Fecha.</summary>
        Date,
        /// <summary>Fecha y hora de pared, sin zona.</summary>
        DateTime,
        /// <summary>Hora.</summary>
        Time,
        /// <summary>Archivos adjuntos (solo cuenta).</summary>
        Files,
    }

    /// <summary>
    /// Respuesta a una pregunta. Inmutable; <c>default</c> equivale a <see cref="None"/>.
    /// Los valores nulos dan <see cref="None"/>.
    /// </summary>
    public readonly record struct AnswerValue
    {
        private readonly string? _text;
        private readonly decimal _number;
        private readonly string[]? _keys;
        private readonly DateOnly _date;
        private readonly System.DateTime _dateTime;
        private readonly TimeOnly _time;
        private readonly int _files;

        private AnswerValue(AnswerKind kind, string? text = null, decimal number = 0, string[]? keys = null,
            DateOnly date = default, System.DateTime dateTime = default, TimeOnly time = default, int files = 0)
        {
            Kind = kind;
            _text = text;
            _number = number;
            _keys = keys;
            _date = date;
            _dateTime = dateTime;
            _time = time;
            _files = files;
        }

        /// <summary>Sin respuesta.</summary>
        public static readonly AnswerValue None = default;

        /// <summary>Clase de respuesta.</summary>
        public AnswerKind Kind { get; }

        /// <summary>Respuesta de texto.</summary>
        /// <param name="v">Texto.</param>
        /// <returns>La respuesta.</returns>
        public static AnswerValue Text(string? v) => v is null ? None : new(AnswerKind.Text, text: v);

        /// <summary>Respuesta numerica.</summary>
        /// <param name="v">Numero.</param>
        /// <returns>La respuesta.</returns>
        public static AnswerValue Number(decimal? v) => v is { } n ? new(AnswerKind.Number, number: n) : None;

        /// <summary>Una opcion elegida, por clave.</summary>
        /// <param name="key">Clave.</param>
        /// <returns>La respuesta.</returns>
        public static AnswerValue Choice(string? key) => key is null ? None : new(AnswerKind.Choice, text: key);

        /// <summary>Varias opciones elegidas, por clave. Las claves vacias se descartan.</summary>
        /// <param name="keys">Claves; no nulo.</param>
        /// <returns>La respuesta.</returns>
        /// <exception cref="ArgumentNullException">Si <paramref name="keys"/> es nulo.</exception>
        public static AnswerValue Choices(IEnumerable<string> keys)
        {
            ArgumentNullException.ThrowIfNull(keys);
            return new(AnswerKind.Choices, keys: keys.Where(k => !string.IsNullOrEmpty(k)).ToArray());
        }

        /// <summary>Respuesta de fecha.</summary>
        /// <param name="v">Fecha.</param>
        /// <returns>La respuesta.</returns>
        public static AnswerValue Date(DateOnly? v) => v is { } d ? new(AnswerKind.Date, date: d) : None;

        /// <summary>Respuesta de fecha y hora de pared (sin zona; el <c>Kind</c> se descarta).</summary>
        /// <param name="localWallClock">Fecha y hora tal como la escribio el usuario.</param>
        /// <returns>La respuesta.</returns>
        public static AnswerValue DateTime(System.DateTime? localWallClock) => localWallClock is { } dt
            ? new(AnswerKind.DateTime, dateTime: System.DateTime.SpecifyKind(dt, DateTimeKind.Unspecified))
            : None;

        /// <summary>Respuesta de hora.</summary>
        /// <param name="v">Hora.</param>
        /// <returns>La respuesta.</returns>
        public static AnswerValue Time(TimeOnly? v) => v is { } t ? new(AnswerKind.Time, time: t) : None;

        /// <summary>Archivos adjuntos.</summary>
        /// <param name="count">Cuantos; no negativo.</param>
        /// <returns>La respuesta.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Si es negativo.</exception>
        public static AnswerValue Files(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            return new(AnswerKind.Files, files: count);
        }

        /// <summary>Texto o clave de opcion; <c>null</c> en otras clases.</summary>
        public string? AsText => Kind is AnswerKind.Text or AnswerKind.Choice ? _text : null;

        /// <summary>Numero; <c>null</c> en otras clases.</summary>
        public decimal? AsNumber => Kind == AnswerKind.Number ? _number : null;

        /// <summary>Claves elegidas: las de <see cref="AnswerKind.Choices"/>, la de <see cref="AnswerKind.Choice"/>, o ninguna.</summary>
        public IReadOnlyList<string> Keys => Kind switch
        {
            AnswerKind.Choices => _keys!,
            AnswerKind.Choice when !string.IsNullOrEmpty(_text) => [_text],
            _ => [],
        };

        /// <summary>Fecha; <c>null</c> en otras clases.</summary>
        public DateOnly? AsDate => Kind == AnswerKind.Date ? _date : null;

        /// <summary>Fecha y hora de pared; <c>null</c> en otras clases.</summary>
        public System.DateTime? AsDateTime => Kind == AnswerKind.DateTime ? _dateTime : null;

        /// <summary>Hora; <c>null</c> en otras clases.</summary>
        public TimeOnly? AsTime => Kind == AnswerKind.Time ? _time : null;

        /// <summary>Numero de archivos (0 si no es de archivos).</summary>
        public int FileCount => Kind == AnswerKind.Files ? _files : 0;

        /// <summary>
        /// Vacio generico: sin respuesta, texto u opcion vacios, sin opciones o sin archivos.
        /// </summary>
        public bool IsEmpty => Kind switch
        {
            AnswerKind.None => true,
            AnswerKind.Text or AnswerKind.Choice => string.IsNullOrEmpty(_text),
            AnswerKind.Choices => _keys!.Length == 0,
            AnswerKind.Files => _files == 0,
            _ => false,
        };

        /// <summary>Igualdad por valor (las opciones multiples, en orden).</summary>
        /// <param name="other">Otra respuesta.</param>
        /// <returns>Si son iguales.</returns>
        public bool Equals(AnswerValue other) =>
            Kind == other.Kind && _text == other._text && _number == other._number && _date == other._date
            && _dateTime == other._dateTime && _time == other._time && _files == other._files
            && (_keys ?? []).AsSpan().SequenceEqual(other._keys ?? []);

        /// <summary>Hash coherente con la igualdad.</summary>
        /// <returns>El hash.</returns>
        public override int GetHashCode() => HashCode.Combine(Kind, _text, _number, _date, _dateTime, _time, _files, _keys?.Length);
    }

    /// <summary>
    /// Respuestas de un formulario: pregunta -> <see cref="AnswerValue"/>. Una pregunta sin entrada
    /// cuenta como <see cref="AnswerValue.None"/>.
    /// </summary>
    public sealed class FormAnswers : IReadOnlyDictionary<string, AnswerValue>
    {
        private readonly Dictionary<string, AnswerValue> _values;

        private FormAnswers(Dictionary<string, AnswerValue> values) => _values = values;

        /// <summary>Sin respuestas.</summary>
        public static readonly FormAnswers Empty = new(new Dictionary<string, AnswerValue>(StringComparer.Ordinal));

        /// <summary>
        /// Crea las respuestas. Si una pregunta se repite, gana la ultima.
        /// </summary>
        /// <param name="values">Pares pregunta/respuesta; no nulo.</param>
        /// <returns>Las respuestas.</returns>
        /// <exception cref="ArgumentNullException">Si <paramref name="values"/> es nulo.</exception>
        public static FormAnswers From(IEnumerable<KeyValuePair<string, AnswerValue>> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            var dict = new Dictionary<string, AnswerValue>(StringComparer.Ordinal);
            foreach (var (key, value) in values)
            {
                ArgumentNullException.ThrowIfNull(key, nameof(values));
                dict[key] = value;
            }
            return new FormAnswers(dict);
        }

        /// <summary>
        /// Respuesta de la pregunta, o <see cref="AnswerValue.None"/>.
        /// </summary>
        /// <param name="questionId">Pregunta.</param>
        /// <returns>La respuesta.</returns>
        public AnswerValue Get(string questionId) => _values.TryGetValue(questionId, out var v) ? v : AnswerValue.None;

        /// <inheritdoc />
        public AnswerValue this[string key] => _values[key];

        /// <inheritdoc />
        public IEnumerable<string> Keys => _values.Keys;

        /// <inheritdoc />
        public IEnumerable<AnswerValue> Values => _values.Values;

        /// <inheritdoc />
        public int Count => _values.Count;

        /// <inheritdoc />
        public bool ContainsKey(string key) => _values.ContainsKey(key);

        /// <inheritdoc />
        public bool TryGetValue(string key, [MaybeNullWhen(false)] out AnswerValue value) => _values.TryGetValue(key, out value);

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, AnswerValue>> GetEnumerator() => _values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
