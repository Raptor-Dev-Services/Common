using System.Text.Json.Serialization;

namespace Common.Workflow.Forms
{
    /// <summary>
    /// Como se une una condicion con la anterior. AND tiene precedencia sobre OR.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<LogicOperator>))]
    public enum LogicOperator
    {
        /// <summary>Y.</summary>
        And,
        /// <summary>O: abre un grupo nuevo.</summary>
        Or,
    }

    /// <summary>
    /// Clase del elemento al que se refiere una condicion.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<SubjectKind>))]
    public enum SubjectKind
    {
        /// <summary>Una pregunta (su respuesta o su visibilidad).</summary>
        Question,
        /// <summary>Una seccion (solo su visibilidad).</summary>
        Section,
        /// <summary>Un comentario (solo su visibilidad).</summary>
        Comment,
    }

    /// <summary>
    /// Operador de una condicion. Que operadores admite cada tipo de pregunta lo decide su
    /// <see cref="IConditionHandler"/>; <see cref="Visible"/> y <see cref="NotVisible"/> valen para todo sujeto.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ValueOperator>))]
    public enum ValueOperator
    {
        /// <summary>Igual.</summary>
        Equals,
        /// <summary>Distinto.</summary>
        NotEquals,
        /// <summary>Contiene (texto: subcadena; opciones: todas las del valor).</summary>
        Contains,
        /// <summary>No contiene.</summary>
        NotContains,
        /// <summary>Mayor que.</summary>
        GreaterThan,
        /// <summary>Mayor o igual que.</summary>
        GreaterThanOrEquals,
        /// <summary>Menor que.</summary>
        LessThan,
        /// <summary>Menor o igual que.</summary>
        LessThanOrEquals,
        /// <summary>Coincide con la expresion regular (multivaluado: todos los valores).</summary>
        MatchRegex,
        /// <summary>No coincide con la expresion regular (multivaluado: ninguno).</summary>
        NotMatchRegex,
        /// <summary>Longitud en caracteres de texto mayor que.</summary>
        LengthGreaterThan,
        /// <summary>Longitud mayor o igual que.</summary>
        LengthGreaterThanOrEquals,
        /// <summary>Longitud menor que.</summary>
        LengthLessThan,
        /// <summary>Longitud menor o igual que.</summary>
        LengthLessThanOrEquals,
        /// <summary>El sujeto esta visible.</summary>
        Visible,
        /// <summary>El sujeto esta oculto.</summary>
        NotVisible,
        /// <summary>La respuesta esta vacia (una pregunta oculta cuenta como vacia).</summary>
        Empty,
        /// <summary>La respuesta no esta vacia.</summary>
        NotEmpty,
    }

    /// <summary>
    /// Cuando se ve un elemento.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<VisibilityStrategy>))]
    public enum VisibilityStrategy
    {
        /// <summary>Siempre visible.</summary>
        AlwaysVisible,
        /// <summary>Visible si las condiciones se cumplen (sin condiciones: oculto).</summary>
        VisibleIf,
        /// <summary>Oculto si las condiciones se cumplen (sin condiciones: visible).</summary>
        HiddenIf,
    }

    /// <summary>
    /// Cuando se crea un destino al enviar.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<CreationStrategy>))]
    public enum CreationStrategy
    {
        /// <summary>Siempre.</summary>
        AlwaysCreated,
        /// <summary>Si las condiciones se cumplen (sin condiciones: no se crea).</summary>
        CreatedIf,
        /// <summary>Salvo que las condiciones se cumplan (sin condiciones: se crea).</summary>
        CreatedUnless,
    }

    /// <summary>
    /// Validacion condicional de una pregunta.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ValidationStrategy>))]
    public enum ValidationStrategy
    {
        /// <summary>Sin validacion condicional.</summary>
        NoValidation,
        /// <summary>Valida si las condiciones se cumplen.</summary>
        ValidIf,
        /// <summary>Invalida si las condiciones se cumplen.</summary>
        InvalidIf,
    }

    /// <summary>
    /// Una condicion: sujeto, operador, valor y como se une con la anterior.
    /// </summary>
    /// <param name="SubjectId">Id del sujeto; vacio = la condicion se ignora.</param>
    /// <param name="SubjectKind">Clase del sujeto.</param>
    /// <param name="Operator">Operador.</param>
    /// <param name="Value">Valor de comparacion como texto (opciones multiples: claves separadas por coma).</param>
    /// <param name="Logic">Union con la condicion anterior; se ignora en la primera.</param>
    public sealed record Condition(string SubjectId, SubjectKind SubjectKind,
        ValueOperator Operator, string? Value, LogicOperator Logic = LogicOperator.And);

    /// <summary>
    /// Regla de visibilidad de un elemento.
    /// </summary>
    /// <param name="Strategy">Estrategia.</param>
    /// <param name="Conditions">Condiciones.</param>
    public sealed record VisibilityRule(VisibilityStrategy Strategy, IReadOnlyList<Condition> Conditions)
    {
        /// <summary>Siempre visible, sin condiciones.</summary>
        public static readonly VisibilityRule Always = new(VisibilityStrategy.AlwaysVisible, []);
    }

    /// <summary>
    /// Regla de validacion condicional de una pregunta.
    /// </summary>
    /// <param name="Strategy">Estrategia.</param>
    /// <param name="Conditions">Condiciones.</param>
    public sealed record ValidationRule(ValidationStrategy Strategy, IReadOnlyList<Condition> Conditions)
    {
        /// <summary>Sin validacion condicional.</summary>
        public static readonly ValidationRule None = new(ValidationStrategy.NoValidation, []);
    }

    /// <summary>
    /// Regla de creacion de un destino.
    /// </summary>
    /// <param name="Strategy">Estrategia.</param>
    /// <param name="Conditions">Condiciones.</param>
    public sealed record CreationRule(CreationStrategy Strategy, IReadOnlyList<Condition> Conditions);

    /// <summary>
    /// Seccion del formulario. La de menor <see cref="Rank"/> siempre es visible.
    /// </summary>
    /// <param name="Id">Id.</param>
    /// <param name="Rank">Orden.</param>
    /// <param name="Visibility">Visibilidad.</param>
    public sealed record SectionModel(string Id, int Rank, VisibilityRule Visibility);

    /// <summary>
    /// Pregunta del formulario.
    /// </summary>
    /// <param name="Id">Id.</param>
    /// <param name="SectionId">Seccion a la que pertenece.</param>
    /// <param name="Type">Tipo; clave del <see cref="IConditionHandler"/> (por ejemplo <c>text</c>).</param>
    /// <param name="IsMandatory">Obligatoria cuando esta visible.</param>
    /// <param name="Visibility">Visibilidad.</param>
    /// <param name="Validation">Validacion condicional.</param>
    public sealed record QuestionModel(string Id, string SectionId, string Type, bool IsMandatory,
        VisibilityRule Visibility, ValidationRule Validation);

    /// <summary>
    /// Comentario (bloque de texto) del formulario.
    /// </summary>
    /// <param name="Id">Id.</param>
    /// <param name="SectionId">Seccion a la que pertenece.</param>
    /// <param name="Visibility">Visibilidad.</param>
    public sealed record CommentModel(string Id, string SectionId, VisibilityRule Visibility);

    /// <summary>
    /// Destino que se genera al enviar (ticket, solicitud...).
    /// </summary>
    /// <param name="Id">Id.</param>
    /// <param name="Creation">Regla de creacion.</param>
    public sealed record DestinationModel(string Id, CreationRule Creation);

    /// <summary>
    /// Formulario completo, serializable. El producto lo guarda como quiera y lo reconstruye para evaluar.
    /// </summary>
    /// <param name="Id">Id.</param>
    /// <param name="SubmitVisibility">Visibilidad del boton de envio.</param>
    /// <param name="Sections">Secciones.</param>
    /// <param name="Questions">Preguntas, en orden de evaluacion.</param>
    /// <param name="Comments">Comentarios.</param>
    /// <param name="Destinations">Destinos.</param>
    public sealed record FormModel(string Id, VisibilityRule SubmitVisibility,
        IReadOnlyList<SectionModel> Sections, IReadOnlyList<QuestionModel> Questions,
        IReadOnlyList<CommentModel> Comments, IReadOnlyList<DestinationModel> Destinations);

    /// <summary>
    /// Opciones del motor.
    /// </summary>
    /// <param name="MaxIterations">Tope de pasadas del punto fijo de visibilidad.</param>
    public sealed record FormEngineOptions(int MaxIterations = 100);
}
