using System.Globalization;

namespace Common.Workflow.Calendars
{
    // Recorridos dia a dia. Cada dia local produce sus intervalos UTC; los de dias consecutivos se
    // recortan contra el anterior (lastEnd / lastStart) para que la union nunca cuente dos veces el
    // mismo instante -- el dia que el reloj se atrasa a medianoche, las 24:00 del sabado (ultima
    // ocurrencia) quedan despues de las 00:00 del domingo (primera) -- y para que un turno que cruza
    // la medianoche se recorra como un solo intervalo continuo.
    public sealed partial class BusinessCalendar
    {
        /// <summary>
        /// Primer instante e &gt;= pos con exactamente <paramref name="ticks"/> de tiempo habil en
        /// [pos, e). Con cero es <see cref="NextWorkingMoment"/>.
        /// </summary>
        private DateTime Forward(DateTime pos, long ticks)
        {
            var remaining = ticks;
            var buffer = new List<UtcInterval>(4);
            var lastEnd = DateTime.MinValue;
            var emptyDays = 0;

            for (var d = LocalDateOf(pos).AddDays(-1); ; d = d.AddDays(1))
            {
                // Dia completo despues de pos que no alcanza a agotar el retraso: se salta entero.
                if (TryWholeDay(d, out var dayStart, out var dayTicks) && dayStart >= pos && remaining > dayTicks)
                {
                    remaining -= dayTicks;
                    emptyDays = 0;
                    continue;
                }

                buffer.Clear();
                AppendDay(d, buffer);
                if (buffer.Count == 0)
                {
                    if (++emptyDays > HorizonDays) throw Horizon(pos, "siguientes");
                    continue;
                }

                emptyDays = 0;
                foreach (var iv in buffer)
                {
                    var s = Max(Max(iv.StartUtc, lastEnd), pos);
                    lastEnd = Max(lastEnd, iv.EndUtc);
                    if (iv.EndUtc <= s) continue;

                    var length = (iv.EndUtc - s).Ticks;
                    if (remaining <= length) return s.AddTicks(remaining);
                    remaining -= length;
                }
            }
        }

        /// <summary>
        /// Ultimo instante e &lt;= pos con exactamente <paramref name="ticks"/> de tiempo habil en
        /// [e, pos). Con cero es <see cref="PreviousWorkingMoment"/>.
        /// </summary>
        private DateTime Backward(DateTime pos, long ticks)
        {
            var remaining = ticks;
            var buffer = new List<UtcInterval>(4);
            var lastStart = DateTime.MaxValue;
            var emptyDays = 0;

            for (var d = LocalDateOf(pos).AddDays(1); ; d = d.AddDays(-1))
            {
                if (TryWholeDay(d, out var dayStart, out var dayTicks) && dayStart.AddDays(1) <= pos && remaining > dayTicks)
                {
                    remaining -= dayTicks;
                    emptyDays = 0;
                    continue;
                }

                buffer.Clear();
                AppendDay(d, buffer);
                if (buffer.Count == 0)
                {
                    if (++emptyDays > HorizonDays) throw Horizon(pos, "anteriores");
                    continue;
                }

                emptyDays = 0;
                for (var i = buffer.Count - 1; i >= 0; i--)
                {
                    var iv = buffer[i];
                    var e = Min(Min(iv.EndUtc, lastStart), pos);
                    lastStart = Min(lastStart, iv.StartUtc);
                    // e == inicio se conserva: con cero, un instante que abre un intervalo es habil.
                    if (e < iv.StartUtc) continue;

                    var length = (e - iv.StartUtc).Ticks;
                    if (remaining <= length) return e.AddTicks(-remaining);
                    remaining -= length;
                }
            }
        }

        /// <summary>
        /// Modo dias habiles (REQ-009): mueve al siguiente momento habil, avanza N dias habiles
        /// conservando la hora de pared y la acota al horario de ese dia.
        /// </summary>
        private DateTime AddWorkingDays(DateTime startUtc, int days)
        {
            var s = Forward(startUtc, 0);
            if (days == 0) return s;

            var local = WallClock.ToLocal(s, TimeZone);
            var date = StepWorkingDays(DateOnly.FromDateTime(local), days, startUtc);
            var segments = _segments[(int)date.DayOfWeek];
            var wall = local.TimeOfDay.Ticks;

            if (wall <= segments[0].Start * TimeSpan.TicksPerMinute)
                return WallClock.ToUtc(date, segments[0].Start, TimeZone, last: false);
            if (wall >= segments[^1].End * TimeSpan.TicksPerMinute)
                return WallClock.ToUtc(date, segments[^1].End, TimeZone, last: true);
            return WallClock.ToUtc(date.ToDateTime(TimeOnly.MinValue).AddTicks(wall), TimeZone, last: false);
        }

        /// <summary>
        /// Modo fin de jornada (REQ-010): el dia 0 es la fecha local de un momento habil.
        /// </summary>
        private DateTime EndOfWorkingDay(DateTime workingMomentUtc, int days)
        {
            var date = StepWorkingDays(DateOnly.FromDateTime(WallClock.ToLocal(workingMomentUtc, TimeZone)), days, workingMomentUtc);
            return WallClock.ToUtc(date, _segments[(int)date.DayOfWeek][^1].End, TimeZone, last: true);
        }

        private DateOnly StepWorkingDays(DateOnly date, int days, DateTime origin)
        {
            var step = Math.Sign(days);
            var pending = Math.Abs((long)days);
            var emptyDays = 0;
            while (pending > 0)
            {
                date = date.AddDays(step);
                if (IsWorkingDay(date))
                {
                    pending--;
                    emptyDays = 0;
                }
                else if (++emptyDays > HorizonDays)
                {
                    throw Horizon(origin, step > 0 ? "siguientes" : "anteriores");
                }
            }

            return date;
        }

        /// <summary>
        /// Intervalos UTC de los tramos de un dia local, ordenados y sin traslapes entre si. Vacio si
        /// es feriado o no tiene tramos.
        /// </summary>
        private void AppendDay(DateOnly date, List<UtcInterval> into)
        {
            var segments = _segments[(int)date.DayOfWeek];
            if (segments.Length == 0 || IsHoliday(date)) return;

            if (TryFixedDayStart(date, out var dayStart))
            {
                foreach (var (start, end) in segments)
                    into.Add(new UtcInterval(dayStart.AddMinutes(start), dayStart.AddMinutes(end)));
                return;
            }

            // Dia con cambio de horario cerca: reglas de BranchClock en cada punta.
            var from = into.Count;
            foreach (var (start, end) in segments)
            {
                var s = WallClock.ToUtc(date, start, TimeZone, last: false);
                var e = WallClock.ToUtc(date, end, TimeZone, last: true);
                if (e > s) into.Add(new UtcInterval(s, e));
            }

            // Con la ultima ocurrencia en el fin, dos tramos del mismo dia pueden pisarse: se funden.
            into.Sort(from, into.Count - from, ByStart);
            var write = from;
            for (var read = from + 1; read < into.Count; read++)
            {
                if (into[read].StartUtc <= into[write].EndUtc)
                    into[write] = new UtcInterval(into[write].StartUtc, Max(into[write].EndUtc, into[read].EndUtc));
                else
                    into[++write] = into[read];
            }

            into.RemoveRange(write + 1, into.Count - write - 1);
        }

        /// <summary>
        /// Si el dia es habil y su zona no cambia de horario cerca, devuelve su medianoche UTC y su
        /// tiempo habil precalculado: permite saltar dias completos sin materializar intervalos.
        /// </summary>
        private bool TryWholeDay(DateOnly date, out DateTime dayStartUtc, out long dayTicks)
        {
            dayTicks = _dayTicks[(int)date.DayOfWeek];
            dayStartUtc = default;
            return dayTicks > 0 && !IsHoliday(date) && TryFixedDayStart(date, out dayStartUtc);
        }

        /// <summary>
        /// Medianoche local en UTC si el desplazamiento es el mismo desde 3 h antes hasta 27 h
        /// despues: entonces ninguna hora de ese dia es inexistente ni ambigua y la conversion es una
        /// resta. Los cambios de horario reales mueven a lo mas 2 h y estan separados por meses.
        /// </summary>
        private bool TryFixedDayStart(DateOnly date, out DateTime dayStartUtc)
        {
            var wall = date.ToDateTime(TimeOnly.MinValue);
            if (_fixedOffset is { } fixedOffset)
            {
                dayStartUtc = DateTime.SpecifyKind(wall - fixedOffset, DateTimeKind.Utc);
                return true;
            }

            var offset = TimeZone.GetUtcOffset(DateTime.SpecifyKind(wall - TimeZone.BaseUtcOffset, DateTimeKind.Utc));
            dayStartUtc = DateTime.SpecifyKind(wall - offset, DateTimeKind.Utc);
            return TimeZone.GetUtcOffset(dayStartUtc.AddHours(-3)) == offset
                   && TimeZone.GetUtcOffset(dayStartUtc.AddHours(27)) == offset;
        }

        private InvalidOperationException Horizon(DateTime originUtc, string direction) =>
            new($"El calendario de la zona {TimeZone.Id} no tiene tiempo habil en los {HorizonDays} dias "
                + $"{direction} a {originUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)}.");
    }
}
