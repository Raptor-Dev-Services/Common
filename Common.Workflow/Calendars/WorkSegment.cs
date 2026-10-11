using System.Text.Json.Serialization;

namespace Common.Workflow.Calendars
{
    /// <summary>
    /// Un tramo de horario habil de un dia de la semana, en minutos de pared de la sucursal.
    /// Un dia puede tener varios tramos (por ejemplo <c>09:00-14:00</c> y <c>16:00-19:00</c>).
    /// </summary>
    /// <param name="Day">Dia de la semana del tramo. Se serializa como texto (<c>"Monday"</c>).</param>
    /// <param name="StartMinute">Minuto de inicio, <c>0..1439</c>.</param>
    /// <param name="EndMinute">
    /// Minuto de fin, exclusivo, <c>1..1440</c>; <c>1440</c> es las 24:00. Un turno de noche se modela
    /// como dos tramos contiguos (<c>sabado 22:00-24:00</c> y <c>domingo 00:00-06:00</c>) que el
    /// calendario funde en un solo intervalo.
    /// </param>
    public sealed record WorkSegment(
        [property: JsonConverter(typeof(JsonStringEnumConverter<DayOfWeek>))] DayOfWeek Day,
        int StartMinute,
        int EndMinute);
}
