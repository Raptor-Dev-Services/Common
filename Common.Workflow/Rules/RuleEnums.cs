using System.Text.Json.Serialization;

namespace Common.Workflow.Rules
{
    /// <summary>
    /// Tipo de dato de un criterio o de una accion, tal como lo declara el producto en su catalogo.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<RuleValueType>))]
    public enum RuleValueType
    {
        /// <summary>Texto libre; se compara sin distinguir mayusculas.</summary>
        Text,
        /// <summary>Numero <see cref="decimal"/> en cultura invariante.</summary>
        Number,
        /// <summary>Fecha de negocio (<see cref="DateOnly"/>) local de la sucursal.</summary>
        Date,
        /// <summary>Instante en UTC.</summary>
        DateTime,
        /// <summary>Booleano.</summary>
        Bool,
        /// <summary>Identificador opaco (se compara ordinal, distinguiendo mayusculas).</summary>
        Id,
    }

    /// <summary>
    /// Como se combinan los criterios de una regla.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<RuleMatch>))]
    public enum RuleMatch
    {
        /// <summary>Todos los criterios (AND); se corta en el primer falso.</summary>
        All,
        /// <summary>Alguno de los criterios (OR); se corta en el primer verdadero.</summary>
        Any,
    }

    /// <summary>
    /// Momento en que una regla se dispara. Es una mascara de bits.
    /// </summary>
    [Flags]
    [JsonConverter(typeof(JsonStringEnumConverter<RuleTrigger>))]
    public enum RuleTrigger
    {
        /// <summary>Nunca.</summary>
        None = 0,
        /// <summary>Al crear el elemento.</summary>
        OnAdd = 1,
        /// <summary>Al actualizar el elemento.</summary>
        OnUpdate = 2,
        /// <summary>Al crear y al actualizar.</summary>
        Any = OnAdd | OnUpdate,
    }

    /// <summary>
    /// Operadores de un criterio. La semantica de cada uno esta en el SRS CMN-C03 (REQ-002).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<RuleOperator>))]
    public enum RuleOperator
    {
        /// <summary>Igualdad (texto sin mayusculas, numero, id, bool, fecha).</summary>
        Is,
        /// <summary>Desigualdad. Negativo: en multivaluados se exige para todos los valores.</summary>
        IsNot,
        /// <summary>Subcadena sin mayusculas. Patron o campo vacio: falso.</summary>
        Contains,
        /// <summary>No contiene. Patron vacio: falso; campo vacio: verdadero. Negativo.</summary>
        NotContains,
        /// <summary>Prefijo sin mayusculas. Patron o campo vacio: falso.</summary>
        BeginsWith,
        /// <summary>Sufijo sin mayusculas. Patron o campo vacio: falso.</summary>
        EndsWith,
        /// <summary>Expresion regular .NET; aporta capturas <c>#0..#9</c>.</summary>
        RegexMatch,
        /// <summary>La expresion regular no coincide. Negativo.</summary>
        RegexNotMatch,
        /// <summary>El campo esta presente y no vacio.</summary>
        Exists,
        /// <summary>El campo esta ausente o vacio. Negativo.</summary>
        NotExists,
        /// <summary>El id es el patron o un descendiente suyo en el arbol del criterio.</summary>
        Under,
        /// <summary>El id no es el patron ni un descendiente suyo. Negativo.</summary>
        NotUnder,
        /// <summary>La fecha del campo es anterior al patron.</summary>
        DateBefore,
        /// <summary>La fecha del campo es posterior al patron.</summary>
        DateAfter,
        /// <summary>La fecha del campo es igual al patron.</summary>
        DateEquals,
        /// <summary>La fecha del campo es distinta del patron. Negativo.</summary>
        DateNotEquals,
        /// <summary>Numero mayor que el patron.</summary>
        GreaterThan,
        /// <summary>Numero mayor o igual que el patron.</summary>
        GreaterOrEqual,
        /// <summary>Numero menor que el patron.</summary>
        LessThan,
        /// <summary>Numero menor o igual que el patron.</summary>
        LessOrEqual,
    }

    /// <summary>
    /// Por que se detuvo el recorrido de una coleccion.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<StopReason>))]
    public enum StopReason
    {
        /// <summary>La coleccion tiene <c>StopOnFirstMatch</c> y una regla coincidio.</summary>
        FirstMatch,
        /// <summary>Una regla que coincidio ejecuto la accion integrada <c>stop</c>.</summary>
        StopAction,
    }

    /// <summary>
    /// Por que el modo explicacion marca una regla como no aplicada aunque la evalue.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<RuleSkipReason>))]
    public enum RuleSkipReason
    {
        /// <summary>La regla esta inactiva.</summary>
        Inactive,
        /// <summary>Los disparadores de la regla no incluyen el disparo pedido.</summary>
        TriggerNotIncluded,
        /// <summary>Una regla anterior detuvo el recorrido.</summary>
        AfterStop,
    }

    /// <summary>
    /// Clase de valor que lleva un <see cref="RuleValue"/>.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<RuleValueKind>))]
    public enum RuleValueKind
    {
        /// <summary>Sin valor (campo ausente o nulo).</summary>
        Empty,
        /// <summary>Texto.</summary>
        Text,
        /// <summary>Numero.</summary>
        Number,
        /// <summary>Instante UTC.</summary>
        Instant,
        /// <summary>Fecha local.</summary>
        Date,
        /// <summary>Identificador.</summary>
        Id,
        /// <summary>Booleano.</summary>
        Bool,
        /// <summary>Lista de valores (campo multivaluado).</summary>
        List,
    }
}
