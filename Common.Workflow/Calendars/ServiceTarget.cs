namespace Common.Workflow.Calendars
{
    /// <summary>
    /// Meta de un nivel de servicio (SLA/OLA): por ejemplo "8 horas" o "2 dias al fin de jornada".
    /// </summary>
    /// <param name="Amount">Cantidad, mayor o igual a cero.</param>
    /// <param name="Unit">Unidad de la cantidad.</param>
    /// <param name="EndOfWorkingDay">
    /// Si vence al fin de la jornada del dia resultante. Solo valido con <see cref="DurationUnit.Day"/>
    /// y <see cref="DurationUnit.Month"/>.
    /// </param>
    public sealed record ServiceTarget(int Amount, DurationUnit Unit, bool EndOfWorkingDay = false);
}
