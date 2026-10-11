namespace Common.Workflow.Calendars
{
    /// <summary>
    /// Desplazamiento de un nivel de escalamiento respecto del vencimiento. Negativo dispara antes.
    /// </summary>
    /// <param name="Amount">Cantidad; puede ser negativa.</param>
    /// <param name="Unit">Unidad de la cantidad.</param>
    public sealed record ServiceOffset(int Amount, DurationUnit Unit);
}
