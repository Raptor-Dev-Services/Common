namespace Common.Workflow.Calendars
{
    /// <summary>
    /// Definicion serializable de un calendario habil: zona horaria, tramos por dia de la semana y
    /// feriados. El producto la guarda como quiera y la compila con <see cref="BusinessCalendar.Create"/>.
    /// </summary>
    /// <param name="TimeZoneId">Zona IANA de la sucursal (por ejemplo <c>America/Mexico_City</c>).</param>
    /// <param name="Segments">Tramos de horario; al menos uno, sin traslapes en el mismo dia.</param>
    /// <param name="Holidays">Feriados, con rango o perpetuos.</param>
    public sealed record BusinessCalendarDefinition(
        string TimeZoneId,
        IReadOnlyList<WorkSegment> Segments,
        IReadOnlyList<Holiday> Holidays);
}
