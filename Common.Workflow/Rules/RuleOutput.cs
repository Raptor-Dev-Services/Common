namespace Common.Workflow.Rules
{
    /// <summary>
    /// Una regla que coincidio y se aplico.
    /// </summary>
    /// <param name="Id">Id de la regla.</param>
    /// <param name="Ranking">Ranking de la regla.</param>
    /// <param name="Captures">Capturas de regex que aporto (sin la coincidencia completa).</param>
    public sealed record MatchedRule(string Id, int Ranking, IReadOnlyList<string> Captures);

    /// <summary>
    /// Resultado de <see cref="RuleCollection.Process"/>.
    /// </summary>
    /// <param name="Changes">Campos a cambiar; si dos reglas escriben el mismo, gana la ultima aplicada.</param>
    /// <param name="MatchedRules">Todas las reglas que coincidieron, en orden de aplicacion.</param>
    /// <param name="StoppedBy">Por que se detuvo el recorrido, o <c>null</c> si llego al final.</param>
    /// <param name="Warnings">Advertencias de evaluacion (por ejemplo <c>rules.regex_timeout:R99</c>).</param>
    public sealed record RuleOutput(IReadOnlyDictionary<string, RuleValue> Changes,
        IReadOnlyList<MatchedRule> MatchedRules, StopReason? StoppedBy, IReadOnlyList<string> Warnings)
    {
        /// <summary>
        /// Verdadero si ninguna regla coincidio.
        /// </summary>
        public bool NoRuleMatched => MatchedRules.Count == 0;
    }

    /// <summary>
    /// Explicacion de un criterio en el modo explicacion.
    /// </summary>
    /// <param name="Index">Posicion del criterio en la regla.</param>
    /// <param name="Field">Campo.</param>
    /// <param name="Operator">Operador.</param>
    /// <param name="Pattern">Patron tal como esta en la definicion.</param>
    /// <param name="Value">Valor del hecho que se evaluo.</param>
    /// <param name="Result">Si el criterio fue verdadero.</param>
    public sealed record CriterionExplanation(int Index, string Field, RuleOperator Operator, string Pattern,
        RuleValue Value, bool Result);

    /// <summary>
    /// Explicacion de una regla: todos sus criterios evaluados, sin corte.
    /// </summary>
    /// <param name="Id">Id de la regla.</param>
    /// <param name="Name">Nombre.</param>
    /// <param name="Ranking">Ranking.</param>
    /// <param name="Skipped">Por que <see cref="RuleCollection.Process"/> no la aplicaria, o <c>null</c>.</param>
    /// <param name="Matched">Si sus criterios se cumplen (sin criterios: nunca).</param>
    /// <param name="Applied">Si <see cref="RuleCollection.Process"/> la aplico.</param>
    /// <param name="Criteria">Cada criterio con su valor y resultado.</param>
    /// <param name="Captures">Capturas de todos los <c>RegexMatch</c> verdaderos (sin corte).</param>
    public sealed record RuleExplanationEntry(string Id, string Name, int Ranking, RuleSkipReason? Skipped,
        bool Matched, bool Applied, IReadOnlyList<CriterionExplanation> Criteria, IReadOnlyList<string> Captures);

    /// <summary>
    /// Resultado de <see cref="RuleCollection.Explain"/>: la explicacion de cada regla en orden de ranking
    /// y la salida que daria <see cref="RuleCollection.Process"/>.
    /// </summary>
    /// <param name="Rules">Explicacion por regla.</param>
    /// <param name="Output">Salida real del proceso.</param>
    public sealed record RuleExplanation(IReadOnlyList<RuleExplanationEntry> Rules, RuleOutput Output);
}
