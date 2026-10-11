namespace Common.Workflow.Calendars
{
    /// <summary>
    /// Conversion de hora de pared a instante con las reglas de Zeit <c>BranchClock</c> (copiadas, no
    /// referenciadas): hora inexistente por cambio de horario se adelanta al primer minuto valido;
    /// hora ambigua toma la primera ocurrencia para lo que abre y la ultima para lo que cierra.
    /// </summary>
    internal static class WallClock
    {
        // Los huecos reales son de 30 minutos a 2 horas; un dia es holgura y garantiza terminar.
        private const int MaxGapProbeMinutes = 24 * 60;

        internal static DateTime ToUtc(DateOnly localDate, int minuteOfDay, TimeZoneInfo timeZone, bool last) =>
            ToUtc(localDate.ToDateTime(TimeOnly.MinValue).AddMinutes(minuteOfDay), timeZone, last);

        internal static DateTime ToUtc(DateTime localWallClock, TimeZoneInfo timeZone, bool last)
        {
            var wall = DateTime.SpecifyKind(localWallClock, DateTimeKind.Unspecified);

            if (timeZone.IsInvalidTime(wall))
            {
                // Los limites del hueco son minutos exactos: se trunca al minuto y se avanza.
                var probe = new DateTime(wall.Ticks - wall.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Unspecified);
                for (var i = 0; i < MaxGapProbeMinutes && timeZone.IsInvalidTime(probe); i++)
                    probe = probe.AddMinutes(1);
                wall = probe;
            }

            if (timeZone.IsAmbiguousTime(wall))
            {
                // UTC = local - desplazamiento: el desplazamiento MAYOR es la primera ocurrencia.
                var offsets = timeZone.GetAmbiguousTimeOffsets(wall);
                var offset = last ? offsets.Min() : offsets.Max();
                return DateTime.SpecifyKind(wall - offset, DateTimeKind.Utc);
            }

            return TimeZoneInfo.ConvertTimeToUtc(wall, timeZone);
        }

        internal static DateTime ToLocal(DateTime instantUtc, TimeZoneInfo timeZone) =>
            TimeZoneInfo.ConvertTimeFromUtc(instantUtc, timeZone);

        internal static void RequireUtc(DateTime value, string paramName)
        {
            if (value.Kind != DateTimeKind.Utc)
                throw new ArgumentException("El instante debe tener Kind = Utc; no se adivina la zona.", paramName);
        }
    }
}
