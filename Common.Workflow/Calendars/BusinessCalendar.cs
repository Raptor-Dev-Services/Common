using Common.Results;

namespace Common.Workflow.Calendars
{
    /// <summary>
    /// Calendario habil compilado: horario por dia de la semana, feriados y zona horaria de la
    /// sucursal. Calcula cuando vence algo en tiempo habil y cuanto tiempo habil paso entre dos
    /// instantes. Inmutable y seguro entre hilos: se compila una vez y se usa como singleton.
    /// </summary>
    /// <remarks>
    /// <para>Todo instante de entrada y salida es UTC (<c>Kind = Utc</c>); las fechas de negocio son
    /// <see cref="DateOnly"/> locales de la sucursal. El tiempo habil se mide en tiempo real
    /// transcurrido, no en horas de pared: el dia que el reloj se atrasa un tramo dura una hora mas.</para>
    /// <para>Los intervalos son semiabiertos <c>[inicio, fin)</c>: en un horario <c>09:00-18:00</c>
    /// las 18:00 exactas no son habiles.</para>
    /// </remarks>
    public sealed partial class BusinessCalendar
    {
        /// <summary>
        /// Dias consecutivos sin tiempo habil tras los que un recorrido se rinde (REQ-CMN-C01-017).
        /// </summary>
        internal const int HorizonDays = 1098;

        private static readonly IComparer<UtcInterval> ByStart =
            Comparer<UtcInterval>.Create((a, b) => a.StartUtc.CompareTo(b.StartUtc));

        // Tramos fundidos por dia de la semana (indice = (int)DayOfWeek), ordenados por inicio.
        private readonly (int Start, int End)[][] _segments;

        // Ticks habiles de pared por dia de la semana (el "cache_duration" de GLPI).
        private readonly long[] _dayTicks;

        private readonly (DateOnly Begin, DateOnly End)[] _datedHolidays;

        // Perpetuos como mes*100+dia.
        private readonly (int Begin, int End)[] _perpetualHolidays;

        // Desplazamiento fijo cuando la zona no tiene reglas de ajuste (por ejemplo UTC).
        private readonly TimeSpan? _fixedOffset;

        private BusinessCalendar(BusinessCalendarDefinition definition, TimeZoneInfo timeZone)
        {
            Definition = definition;
            TimeZone = timeZone;
            _fixedOffset = timeZone.GetAdjustmentRules().Length == 0 ? timeZone.BaseUtcOffset : null;

            _segments = new (int, int)[7][];
            _dayTicks = new long[7];
            for (var day = 0; day < 7; day++)
            {
                var merged = new List<(int Start, int End)>();
                foreach (var s in definition.Segments.Where(s => (int)s.Day == day).OrderBy(s => s.StartMinute))
                {
                    // Contiguos (09:00-14:00 + 14:00-18:00) se funden; los traslapes ya se rechazaron.
                    if (merged.Count > 0 && merged[^1].End == s.StartMinute)
                        merged[^1] = (merged[^1].Start, s.EndMinute);
                    else
                        merged.Add((s.StartMinute, s.EndMinute));
                }

                _segments[day] = [.. merged];
                _dayTicks[day] = merged.Sum(m => (long)(m.End - m.Start)) * TimeSpan.TicksPerMinute;
            }

            _datedHolidays = [.. definition.Holidays.Where(h => !h.IsPerpetual).Select(h => (h.Begin, h.End))];
            _perpetualHolidays = [.. definition.Holidays.Where(h => h.IsPerpetual)
                .Select(h => (MonthDay(h.Begin), MonthDay(h.End)))];
        }

        /// <summary>
        /// Definicion de la que se compilo el calendario (copia defensiva).
        /// </summary>
        public BusinessCalendarDefinition Definition { get; }

        /// <summary>
        /// Zona horaria de la sucursal.
        /// </summary>
        public TimeZoneInfo TimeZone { get; }

        /// <summary>
        /// Compila y valida una definicion. Devuelve un <see cref="DefinitionFailure{T}"/> con
        /// <b>todos</b> los errores si no es valida.
        /// </summary>
        /// <param name="definition">Definicion a compilar.</param>
        /// <returns>El calendario, o el fallo con los errores encontrados.</returns>
        /// <exception cref="ArgumentNullException">Si <paramref name="definition"/> es nulo.</exception>
        public static Result<BusinessCalendar> Create(BusinessCalendarDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);

            var errors = new List<DefinitionError>();
            var segments = definition.Segments ?? [];
            var holidays = definition.Holidays ?? [];

            TimeZoneInfo? timeZone = null;
            if (string.IsNullOrWhiteSpace(definition.TimeZoneId)
                || !TimeZoneInfo.TryFindSystemTimeZoneById(definition.TimeZoneId, out timeZone))
            {
                errors.Add(new DefinitionError("calendar.timezone_unknown", "TimeZoneId",
                    $"La zona '{definition.TimeZoneId}' no existe en este sistema."));
            }

            if (segments.Count == 0)
                errors.Add(new DefinitionError("calendar.no_working_time", "Segments", "El calendario no tiene ningun tramo habil."));

            var valid = new List<(int Index, WorkSegment Segment)>();
            for (var i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                if (s is null || !Enum.IsDefined(s.Day) || s.StartMinute < 0 || s.EndMinute > 1440 || s.StartMinute >= s.EndMinute)
                {
                    errors.Add(new DefinitionError("calendar.segment_range", $"Segments[{i}]",
                        "El tramo debe cumplir 0 <= inicio < fin <= 1440 con un dia de la semana valido."));
                }
                else
                {
                    valid.Add((i, s));
                }
            }

            foreach (var day in valid.GroupBy(v => v.Segment.Day))
            {
                var maxEnd = -1;
                foreach (var (index, segment) in day.OrderBy(v => v.Segment.StartMinute).ThenBy(v => v.Index))
                {
                    if (segment.StartMinute < maxEnd)
                    {
                        errors.Add(new DefinitionError("calendar.segment_overlap", $"Segments[{index}]",
                            $"El tramo se traslapa con otro del {segment.Day}."));
                    }

                    maxEnd = Math.Max(maxEnd, segment.EndMinute);
                }
            }

            for (var i = 0; i < holidays.Count; i++)
            {
                var h = holidays[i];
                if (h is null || (!h.IsPerpetual && h.Begin > h.End))
                {
                    errors.Add(new DefinitionError("calendar.holiday_range", $"Holidays[{i}]",
                        "El feriado no perpetuo debe cumplir Begin <= End."));
                }
            }

            if (errors.Count > 0)
                return new DefinitionFailure<BusinessCalendar>(errors);

            var copy = new BusinessCalendarDefinition(
                definition.TimeZoneId,
                Array.AsReadOnly(segments.ToArray()),
                Array.AsReadOnly(holidays.ToArray()));
            return Result.OK(new BusinessCalendar(copy, timeZone!));
        }

        /// <summary>
        /// Calendario 24x7 sin feriados: sustituye a la rama "sin calendario" de GLPI.
        /// </summary>
        /// <param name="timeZoneId">Zona IANA.</param>
        /// <returns>Un calendario con <c>00:00-24:00</c> todos los dias.</returns>
        /// <exception cref="ArgumentException">Si la zona no existe.</exception>
        public static BusinessCalendar AlwaysOpen(string timeZoneId)
        {
            var result = Create(new BusinessCalendarDefinition(
                timeZoneId,
                [.. Enum.GetValues<DayOfWeek>().Select(d => new WorkSegment(d, 0, 1440))],
                []));

            return result is SuccessResult<BusinessCalendar> ok
                ? ok.Data
                : throw new ArgumentException(((IFailure)result).Message, nameof(timeZoneId));
        }

        /// <summary>
        /// Indica si la fecha local cae en un feriado (con rango o perpetuo, incluido el que cruza el anio).
        /// </summary>
        /// <param name="localDate">Fecha local de la sucursal.</param>
        public bool IsHoliday(DateOnly localDate)
        {
            foreach (var (begin, end) in _datedHolidays)
            {
                if (localDate >= begin && localDate <= end) return true;
            }

            var md = MonthDay(localDate);
            foreach (var (begin, end) in _perpetualHolidays)
            {
                if (begin <= end ? md >= begin && md <= end : md >= begin || md <= end) return true;
            }

            return false;
        }

        /// <summary>
        /// Indica si la fecha local no es feriado y tiene al menos un tramo ese dia de la semana.
        /// </summary>
        /// <param name="localDate">Fecha local de la sucursal.</param>
        public bool IsWorkingDay(DateOnly localDate) =>
            _segments[(int)localDate.DayOfWeek].Length > 0 && !IsHoliday(localDate);

        /// <summary>
        /// Indica si el instante cae dentro de algun intervalo habil <c>[inicio, fin)</c>.
        /// </summary>
        /// <param name="instantUtc">Instante en UTC.</param>
        /// <exception cref="ArgumentException">Si el instante no es UTC.</exception>
        public bool IsWorkingTime(DateTime instantUtc)
        {
            WallClock.RequireUtc(instantUtc, nameof(instantUtc));
            var date = LocalDateOf(instantUtc);
            var buffer = new List<UtcInterval>(8);
            for (var d = date.AddDays(-1); d <= date.AddDays(1); d = d.AddDays(1))
                AppendDay(d, buffer);
            return buffer.Exists(iv => iv.StartUtc <= instantUtc && instantUtc < iv.EndUtc);
        }

        /// <summary>
        /// El propio instante si es habil; si no, el inicio del siguiente intervalo habil.
        /// </summary>
        /// <param name="instantUtc">Instante en UTC.</param>
        /// <exception cref="ArgumentException">Si el instante no es UTC.</exception>
        /// <exception cref="InvalidOperationException">Si no hay tiempo habil en el horizonte de 1098 dias.</exception>
        public DateTime NextWorkingMoment(DateTime instantUtc)
        {
            WallClock.RequireUtc(instantUtc, nameof(instantUtc));
            return Forward(instantUtc, 0);
        }

        /// <summary>
        /// El propio instante si es habil o es el fin exacto de un intervalo; si no, el fin del
        /// intervalo habil anterior.
        /// </summary>
        /// <param name="instantUtc">Instante en UTC.</param>
        /// <exception cref="ArgumentException">Si el instante no es UTC.</exception>
        /// <exception cref="InvalidOperationException">Si no hay tiempo habil en el horizonte de 1098 dias.</exception>
        public DateTime PreviousWorkingMoment(DateTime instantUtc)
        {
            WallClock.RequireUtc(instantUtc, nameof(instantUtc));
            return Backward(instantUtc, 0);
        }

        /// <summary>
        /// Fecha local de la sucursal a la que pertenece un instante.
        /// </summary>
        /// <param name="instantUtc">Instante en UTC.</param>
        /// <exception cref="ArgumentException">Si el instante no es UTC.</exception>
        public DateOnly LocalDateOf(DateTime instantUtc)
        {
            WallClock.RequireUtc(instantUtc, nameof(instantUtc));
            return DateOnly.FromDateTime(WallClock.ToLocal(instantUtc, TimeZone));
        }

        /// <summary>
        /// Tiempo habil real de los tramos de un dia local (cero si es feriado). El dia del cambio de
        /// horario puede durar una hora mas o una menos que en la pared.
        /// </summary>
        /// <param name="localDate">Fecha local de la sucursal.</param>
        public TimeSpan WorkingTimeOn(DateOnly localDate)
        {
            var buffer = new List<UtcInterval>(4);
            AppendDay(localDate, buffer);
            return new TimeSpan(buffer.Sum(iv => iv.Duration.Ticks));
        }

        /// <summary>
        /// Intervalos habiles fundidos que intersectan <c>[desde, hasta)</c>, recortados a ese rango.
        /// Un turno que cruza la medianoche sale como un solo intervalo.
        /// </summary>
        /// <param name="fromUtc">Inicio del rango, en UTC.</param>
        /// <param name="toUtc">Fin del rango, exclusivo, en UTC.</param>
        /// <exception cref="ArgumentException">Si algun instante no es UTC.</exception>
        public IEnumerable<UtcInterval> WorkingIntervals(DateTime fromUtc, DateTime toUtc)
        {
            WallClock.RequireUtc(fromUtc, nameof(fromUtc));
            WallClock.RequireUtc(toUtc, nameof(toUtc));
            var result = new List<UtcInterval>();
            if (toUtc <= fromUtc) return result;

            var buffer = new List<UtcInterval>(4);
            var lastEnd = DateTime.MinValue;
            var until = LocalDateOf(toUtc).AddDays(1);
            for (var d = LocalDateOf(fromUtc).AddDays(-1); d <= until; d = d.AddDays(1))
            {
                buffer.Clear();
                AppendDay(d, buffer);
                foreach (var iv in buffer)
                {
                    var s = Max(Max(iv.StartUtc, lastEnd), fromUtc);
                    var e = Min(iv.EndUtc, toUtc);
                    lastEnd = Max(lastEnd, iv.EndUtc);
                    if (e <= s) continue;

                    if (result.Count > 0 && result[^1].EndUtc == s)
                        result[^1] = new UtcInterval(result[^1].StartUtc, e);
                    else
                        result.Add(new UtcInterval(s, e));
                }
            }

            return result;
        }

        /// <summary>
        /// Tiempo habil en <c>[desde, hasta)</c>: suma de su interseccion con los intervalos habiles.
        /// Cero si <paramref name="toUtc"/> no es posterior a <paramref name="fromUtc"/>.
        /// </summary>
        /// <param name="fromUtc">Inicio, en UTC.</param>
        /// <param name="toUtc">Fin, exclusivo, en UTC.</param>
        /// <exception cref="ArgumentException">Si algun instante no es UTC.</exception>
        public TimeSpan ActiveTimeBetween(DateTime fromUtc, DateTime toUtc)
        {
            WallClock.RequireUtc(fromUtc, nameof(fromUtc));
            WallClock.RequireUtc(toUtc, nameof(toUtc));
            if (toUtc <= fromUtc) return TimeSpan.Zero;

            var total = 0L;
            var buffer = new List<UtcInterval>(4);
            var lastEnd = DateTime.MinValue;
            var until = LocalDateOf(toUtc).AddDays(1);
            for (var d = LocalDateOf(fromUtc).AddDays(-1); d <= until; d = d.AddDays(1))
            {
                // Dia completo dentro del rango y sin cambio de horario: se suma sin materializarlo.
                if (TryWholeDay(d, out var dayStart, out var dayTicks)
                    && dayStart >= fromUtc && dayStart.AddDays(1) <= toUtc)
                {
                    total += dayTicks;
                    continue;
                }

                buffer.Clear();
                AppendDay(d, buffer);
                foreach (var iv in buffer)
                {
                    var s = Max(Max(iv.StartUtc, lastEnd), fromUtc);
                    var e = Min(iv.EndUtc, toUtc);
                    lastEnd = Max(lastEnd, iv.EndUtc);
                    if (e > s) total += (e - s).Ticks;
                }
            }

            return new TimeSpan(total);
        }

        /// <summary>
        /// Fecha fin de sumar (o restar) un retraso en tiempo habil, mas un retraso adicional.
        /// </summary>
        /// <param name="startUtc">Instante de inicio, en UTC.</param>
        /// <param name="delay">
        /// Positivo o negativo. En <see cref="EndDateMode.WorkingTime"/> devuelve el primer instante
        /// (o el ultimo, si es negativo) con exactamente ese tiempo habil; con cero devuelve
        /// <see cref="NextWorkingMoment"/>. En <see cref="EndDateMode.WorkingDays"/> y
        /// <see cref="EndDateMode.EndOfWorkingDay"/> debe ser multiplo exacto de 1 dia.
        /// </param>
        /// <param name="additionalDelay">Mayor o igual a cero; se suma despues, siempre en tiempo habil.</param>
        /// <param name="mode">Como interpretar <paramref name="delay"/>.</param>
        /// <returns>El instante de fin, en UTC.</returns>
        /// <exception cref="ArgumentException">Si el instante no es UTC o el retraso en dias no es entero.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="additionalDelay"/> es negativo o el modo no existe.</exception>
        /// <exception cref="InvalidOperationException">Si no hay tiempo habil en el horizonte de 1098 dias.</exception>
        public DateTime ComputeEndDate(
            DateTime startUtc,
            TimeSpan delay,
            TimeSpan additionalDelay = default,
            EndDateMode mode = EndDateMode.WorkingTime)
        {
            WallClock.RequireUtc(startUtc, nameof(startUtc));
            ArgumentOutOfRangeException.ThrowIfLessThan(additionalDelay, TimeSpan.Zero);

            var end = mode switch
            {
                EndDateMode.WorkingTime => delay >= TimeSpan.Zero
                    ? Forward(startUtc, delay.Ticks)
                    : Backward(startUtc, -delay.Ticks),
                EndDateMode.WorkingDays => AddWorkingDays(startUtc, WholeDays(delay)),
                EndDateMode.EndOfWorkingDay => EndOfWorkingDay(NextWorkingMoment(startUtc), WholeDays(delay)),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Modo de calculo desconocido."),
            };

            return additionalDelay > TimeSpan.Zero ? Forward(end, additionalDelay.Ticks) : end;
        }

        /// <summary>
        /// Suma meses de calendario a la fecha local conservando la hora de pared (31-ene + 1 mes =
        /// 28 o 29-feb) y lleva el resultado al siguiente momento habil, o al fin de esa jornada.
        /// </summary>
        /// <param name="startUtc">Instante de origen, en UTC.</param>
        /// <param name="months">Meses; puede ser negativo. Siempre se suman desde el origen.</param>
        /// <param name="endOfWorkingDay">Si el resultado es el fin de la jornada habil.</param>
        /// <returns>El instante resultante, en UTC.</returns>
        /// <exception cref="ArgumentException">Si el instante no es UTC.</exception>
        public DateTime AddMonths(DateTime startUtc, int months, bool endOfWorkingDay = false)
        {
            WallClock.RequireUtc(startUtc, nameof(startUtc));
            var local = WallClock.ToLocal(startUtc, TimeZone);
            var date = DateOnly.FromDateTime(local).AddMonths(months);
            var target = WallClock.ToUtc(date.ToDateTime(TimeOnly.MinValue).AddTicks(local.TimeOfDay.Ticks), TimeZone, last: false);
            var next = Forward(target, 0);
            return endOfWorkingDay ? EndOfWorkingDay(next, 0) : next;
        }

        private static int MonthDay(DateOnly date) => date.Month * 100 + date.Day;

        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

        private static int WholeDays(TimeSpan delay)
        {
            if (delay.Ticks % TimeSpan.TicksPerDay != 0)
                throw new ArgumentException("En modos por dias el retraso debe ser un multiplo exacto de 1 dia.", nameof(delay));
            return checked((int)(delay.Ticks / TimeSpan.TicksPerDay));
        }
    }
}
