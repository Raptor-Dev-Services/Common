using System.Text.Json.Serialization;

namespace Common.Workflow.States
{
    /// <summary>
    /// Conjuntos a los que pertenece un estado (GLPI <c>getNewStatusArray</c>, <c>getSolvedStatusArray</c>...).
    /// Un estado puede estar en varios. Rigen la auto-asignacion, la pausa del SLA, las metricas y la
    /// reapertura.
    /// </summary>
    [Flags]
    [JsonConverter(typeof(JsonStringEnumConverter<StateSet>))]
    public enum StateSet
    {
        /// <summary>Sin conjunto.</summary>
        None = 0,

        /// <summary>Recien creado, aun sin atender (GLPI INCOMING, APPROVAL).</summary>
        New = 1,

        /// <summary>En atencion (GLPI ASSIGNED, PLANNED).</summary>
        InProgress = 2,

        /// <summary>En espera: el reloj de servicio se detiene (GLPI WAITING).</summary>
        Paused = 4,

        /// <summary>Resuelto, pendiente de aceptacion (GLPI SOLVED).</summary>
        Solved = 8,

        /// <summary>Cerrado (GLPI CLOSED).</summary>
        Closed = 16,

        /// <summary>Se puede reabrir desde aqui.</summary>
        Reopenable = 32,
    }
}
