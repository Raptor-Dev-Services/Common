using System.Text.Json.Serialization;

namespace Common.Workflow.Calendars
{
    /// <summary>
    /// Unidad de una meta o de un desplazamiento de servicio.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<DurationUnit>))]
    public enum DurationUnit
    {
        /// <summary>Minutos habiles.</summary>
        Minute = 0,

        /// <summary>Horas habiles.</summary>
        Hour = 1,

        /// <summary>Dias habiles.</summary>
        Day = 2,

        /// <summary>Meses de calendario (no 30 dias).</summary>
        Month = 3,
    }
}
