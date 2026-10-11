namespace Common.Workflow.Rules
{
    /// <summary>
    /// Un criterio que el producto pone a disposicion de sus reglas (lo declara en codigo).
    /// </summary>
    /// <param name="Field">Nombre del campo en los hechos.</param>
    /// <param name="ValueType">Tipo de dato del campo.</param>
    /// <param name="AllowedOperators">Operadores que el producto permite sobre este campo.</param>
    /// <param name="IsMultiValued">Si el hecho es una lista (por ejemplo, grupos del solicitante).</param>
    /// <param name="TreeName">Arbol del que cuelgan los ids, para <c>Under</c>/<c>NotUnder</c>.</param>
    public sealed record CriterionSpec(string Field, RuleValueType ValueType,
        IReadOnlySet<RuleOperator> AllowedOperators, bool IsMultiValued = false, string? TreeName = null);

    /// <summary>
    /// Criterio de una regla: campo, operador y patron.
    /// </summary>
    /// <param name="Field">Campo del catalogo.</param>
    /// <param name="Operator">Operador.</param>
    /// <param name="Pattern">Patron como texto (numero/fecha invariante, regex sin delimitadores).</param>
    public sealed record CriterionDefinition(string Field, RuleOperator Operator, string Pattern);

    /// <summary>
    /// Accion de una regla: clave registrada en el <see cref="RuleActionRegistry"/> y valor como texto.
    /// </summary>
    /// <param name="ActionKey">Clave de la accion (por ejemplo <c>assign:group_id</c> o <c>stop</c>).</param>
    /// <param name="Value">Valor como texto; en acciones de regex admite <c>#0..#9</c>.</param>
    public sealed record ActionDefinition(string ActionKey, string Value);

    /// <summary>
    /// Definicion serializable de una regla: criterios y acciones.
    /// </summary>
    /// <param name="Id">Identificador unico en la coleccion.</param>
    /// <param name="Name">Nombre legible.</param>
    /// <param name="Ranking">Orden de evaluacion (ascendente).</param>
    /// <param name="IsActive">Si se evalua.</param>
    /// <param name="Match">AND/OR de los criterios.</param>
    /// <param name="Triggers">Disparos en que se evalua.</param>
    /// <param name="Criteria">Criterios, en orden de evaluacion.</param>
    /// <param name="Actions">Acciones, en orden de aplicacion.</param>
    public sealed record RuleDefinition(string Id, string Name, int Ranking, bool IsActive,
        RuleMatch Match, RuleTrigger Triggers,
        IReadOnlyList<CriterionDefinition> Criteria, IReadOnlyList<ActionDefinition> Actions);

    /// <summary>
    /// Opciones de una coleccion de reglas.
    /// </summary>
    /// <param name="Name">Nombre de la coleccion (por ejemplo <c>tickets</c>).</param>
    /// <param name="StopOnFirstMatch">Detener el recorrido tras la primera regla que coincida.</param>
    /// <param name="ChainOutputToInput">Evaluar cada regla sobre los hechos sobrescritos por la salida acumulada.</param>
    /// <param name="TimeZoneId">Zona IANA en que se comparan las fechas con precision de dia.</param>
    public sealed record RuleCollectionOptions(string Name, bool StopOnFirstMatch = false,
        bool ChainOutputToInput = false, string TimeZoneId = "UTC");

    /// <summary>
    /// Resuelve la jerarquia de un arbol del producto (ubicaciones, categorias) para <c>Under</c>.
    /// Debe ser puro y seguro entre hilos.
    /// </summary>
    public interface ITreeResolver
    {
        /// <summary>
        /// Verdadero si <paramref name="id"/> es descendiente de <paramref name="ancestorId"/> en el arbol
        /// <paramref name="treeName"/>. La igualdad la resuelve el motor antes de llamar.
        /// </summary>
        /// <param name="treeName">Nombre del arbol (el <see cref="CriterionSpec.TreeName"/>).</param>
        /// <param name="id">Id del hecho.</param>
        /// <param name="ancestorId">Id del patron.</param>
        /// <returns>Si cuelga de el.</returns>
        bool IsUnder(string treeName, string id, string ancestorId);
    }
}
