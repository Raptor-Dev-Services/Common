namespace Common.Workflow.Calendars
{
    /// <summary>
    /// Reloj de nivel de servicio (SLA/OLA) sobre un <see cref="BusinessCalendar"/>: vencimiento con
    /// pausa acumulada, fecha de escalamiento, retraso y tiempo efectivo. Sin estado: la pausa la
    /// guarda el producto (<c>PausedSinceUtc</c>, <c>AccumulatedPause</c>) y aqui solo se calcula.
    /// </summary>
    public static class ServiceClock
    {
        /// <summary>
        /// Vencimiento de una meta, siempre recalculado desde el inicio original: la fecha de la meta
        /// en su modo y despues la pausa acumulada en tiempo habil.
        /// </summary>
        /// <param name="calendar">Calendario de la sucursal.</param>
        /// <param name="target">Meta de servicio.</param>
        /// <param name="startUtc">Inicio original (apertura del ticket), en UTC.</param>
        /// <param name="accumulatedPause">Tiempo habil acumulado en pausa; mayor o igual a cero.</param>
        /// <returns>El vencimiento, en UTC.</returns>
        /// <exception cref="ArgumentException">Si la meta es invalida o el instante no es UTC.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Si la cantidad o la pausa son negativas, o la unidad no existe.</exception>
        public static DateTime ComputeDue(BusinessCalendar calendar, ServiceTarget target,
            DateTime startUtc, TimeSpan accumulatedPause)
        {
            ArgumentNullException.ThrowIfNull(calendar);
            ArgumentNullException.ThrowIfNull(target);
            ArgumentOutOfRangeException.ThrowIfNegative(target.Amount, nameof(target));
            ArgumentOutOfRangeException.ThrowIfLessThan(accumulatedPause, TimeSpan.Zero);
            if (target.EndOfWorkingDay && target.Unit is DurationUnit.Minute or DurationUnit.Hour)
                throw new ArgumentException("EndOfWorkingDay solo aplica a las unidades Day y Month.", nameof(target));

            switch (target.Unit)
            {
                case DurationUnit.Month:
                    var due = calendar.AddMonths(startUtc, target.Amount, target.EndOfWorkingDay);
                    return accumulatedPause > TimeSpan.Zero ? calendar.ComputeEndDate(due, accumulatedPause) : due;
                case DurationUnit.Day:
                    return calendar.ComputeEndDate(startUtc, TimeSpan.FromDays(target.Amount), accumulatedPause,
                        target.EndOfWorkingDay ? EndDateMode.EndOfWorkingDay : EndDateMode.WorkingDays);
                default:
                    return calendar.ComputeEndDate(startUtc, ToTimeSpan(target.Amount, target.Unit, nameof(target)), accumulatedPause);
            }
        }

        /// <summary>
        /// Tiempo habil que duro una pausa; el producto lo suma a su pausa acumulada.
        /// </summary>
        /// <param name="calendar">Calendario de la sucursal.</param>
        /// <param name="pausedSinceUtc">Inicio de la pausa, en UTC.</param>
        /// <param name="resumedAtUtc">Fin de la pausa, en UTC.</param>
        /// <returns>El tiempo habil entre ambos instantes.</returns>
        public static TimeSpan PauseIncrement(BusinessCalendar calendar, DateTime pausedSinceUtc, DateTime resumedAtUtc)
        {
            ArgumentNullException.ThrowIfNull(calendar);
            return calendar.ActiveTimeBetween(pausedSinceUtc, resumedAtUtc);
        }

        /// <summary>
        /// Desplaza en tiempo habil un vencimiento fijado a mano. Con incremento cero devuelve el
        /// vencimiento sin tocarlo.
        /// </summary>
        /// <param name="calendar">Calendario de la sucursal.</param>
        /// <param name="manualDueUtc">Vencimiento fijado a mano, en UTC.</param>
        /// <param name="pauseIncrement">Incremento de pausa; mayor o igual a cero.</param>
        /// <returns>El nuevo vencimiento, en UTC.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Si el incremento es negativo.</exception>
        public static DateTime ShiftManualDue(BusinessCalendar calendar, DateTime manualDueUtc, TimeSpan pauseIncrement)
        {
            ArgumentNullException.ThrowIfNull(calendar);
            WallClock.RequireUtc(manualDueUtc, nameof(manualDueUtc));
            ArgumentOutOfRangeException.ThrowIfLessThan(pauseIncrement, TimeSpan.Zero);
            return pauseIncrement == TimeSpan.Zero ? manualDueUtc : calendar.ComputeEndDate(manualDueUtc, pauseIncrement);
        }

        /// <summary>
        /// Fecha de ejecucion de un nivel de escalamiento: el vencimiento (con su pausa) desplazado
        /// por el offset del nivel. Un offset negativo dispara antes del vencimiento; uno de cero, en
        /// el vencimiento mismo.
        /// </summary>
        /// <param name="calendar">Calendario de la sucursal.</param>
        /// <param name="target">Meta de servicio.</param>
        /// <param name="startUtc">Inicio original, en UTC.</param>
        /// <param name="accumulatedPause">Pausa acumulada; mayor o igual a cero.</param>
        /// <param name="levelOffset">Desplazamiento del nivel.</param>
        /// <returns>El instante de ejecucion, en UTC.</returns>
        public static DateTime ComputeEscalationAt(BusinessCalendar calendar, ServiceTarget target,
            DateTime startUtc, TimeSpan accumulatedPause, ServiceOffset levelOffset)
        {
            ArgumentNullException.ThrowIfNull(levelOffset);
            var due = ComputeDue(calendar, target, startUtc, accumulatedPause);
            if (levelOffset.Amount == 0) return due;

            return levelOffset.Unit switch
            {
                DurationUnit.Month => calendar.AddMonths(due, levelOffset.Amount),
                DurationUnit.Day => calendar.ComputeEndDate(due, TimeSpan.FromDays(levelOffset.Amount), TimeSpan.Zero, EndDateMode.WorkingDays),
                _ => calendar.ComputeEndDate(due, ToTimeSpan(levelOffset.Amount, levelOffset.Unit, nameof(levelOffset))),
            };
        }

        /// <summary>
        /// Indica si va retrasado: nunca mientras esta en pausa; con fin, si termino despues del
        /// vencimiento; sin fin, si el vencimiento ya paso.
        /// </summary>
        /// <param name="dueUtc">Vencimiento, en UTC.</param>
        /// <param name="completedAtUtc">Fin (resolucion, atencion), si lo hay, en UTC.</param>
        /// <param name="nowUtc">Instante actual, en UTC.</param>
        /// <param name="isPaused">Si esta en pausa.</param>
        public static bool IsLate(DateTime dueUtc, DateTime? completedAtUtc, DateTime nowUtc, bool isPaused)
        {
            WallClock.RequireUtc(dueUtc, nameof(dueUtc));
            WallClock.RequireUtc(nowUtc, nameof(nowUtc));
            if (completedAtUtc is { } completed) WallClock.RequireUtc(completed, nameof(completedAtUtc));

            if (isPaused) return false;
            return completedAtUtc is { } end ? end > dueUtc : dueUtc < nowUtc;
        }

        /// <summary>
        /// Tiempo habil efectivo entre dos instantes descontando la pausa, nunca negativo. No aplica
        /// el piso de 1 segundo de GLPI.
        /// </summary>
        /// <param name="calendar">Calendario de la sucursal.</param>
        /// <param name="startUtc">Inicio, en UTC.</param>
        /// <param name="endUtc">Fin, en UTC.</param>
        /// <param name="accumulatedPause">Pausa acumulada; mayor o igual a cero.</param>
        /// <returns><c>max(0, tiempo habil - pausa)</c>.</returns>
        public static TimeSpan ElapsedActive(BusinessCalendar calendar, DateTime startUtc, DateTime endUtc, TimeSpan accumulatedPause)
        {
            ArgumentNullException.ThrowIfNull(calendar);
            ArgumentOutOfRangeException.ThrowIfLessThan(accumulatedPause, TimeSpan.Zero);
            var elapsed = calendar.ActiveTimeBetween(startUtc, endUtc) - accumulatedPause;
            return elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero;
        }

        private static TimeSpan ToTimeSpan(int amount, DurationUnit unit, string paramName) => unit switch
        {
            DurationUnit.Minute => TimeSpan.FromMinutes(amount),
            DurationUnit.Hour => TimeSpan.FromHours(amount),
            _ => throw new ArgumentOutOfRangeException(paramName, unit, "Unidad de duracion desconocida."),
        };
    }
}
