using System.Text.Json.Serialization;

namespace Common.Workflow.Calendars
{
    /// <summary>
    /// Como interpreta <see cref="BusinessCalendar.ComputeEndDate"/> el retraso pedido.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<EndDateMode>))]
    public enum EndDateMode
    {
        /// <summary>Tiempo habil real transcurrido (horas y minutos habiles).</summary>
        WorkingTime = 0,

        /// <summary>Dias habiles conservando la hora de pared, acotada al horario de ese dia.</summary>
        WorkingDays = 1,

        /// <summary>Fin del ultimo tramo del dia habil que esta N dias habiles despues.</summary>
        EndOfWorkingDay = 2,
    }
}
