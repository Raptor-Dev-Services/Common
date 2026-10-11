namespace Common.Workflow.Depreciation
{
    /// <summary>
    /// Tabla de depreciacion calculada. Es inmutable: se puede compartir entre hilos.
    /// </summary>
    /// <param name="Periods">Ejercicios en orden cronologico; al menos uno.</param>
    /// <param name="Cost">Monto original de la inversion.</param>
    /// <param name="DepreciableBase">Base depreciable: <c>min(Cost, DepreciableCap)</c>.</param>
    /// <param name="NonDeductibleAmount">Parte del costo que el tope deja fuera (<c>Cost - DepreciableBase</c>).</param>
    /// <param name="FullyDepreciatedOn">Cierre del ejercicio en que el valor llega al residual.</param>
    /// <param name="Rounding">Decimales con que se redondea el valor en libros mensual.</param>
    public sealed record DepreciationSchedule(IReadOnlyList<DepreciationPeriod> Periods,
        decimal Cost, decimal DepreciableBase, decimal NonDeductibleAmount, DateOnly? FullyDepreciatedOn,
        int Rounding = 2)
    {
        /// <summary>
        /// Valor en libros a una fecha. Antes del arranque de la tabla devuelve la base depreciable
        /// (igual al costo cuando no hay tope); despues de <see cref="FullyDepreciatedOn"/>, el residual.
        /// </summary>
        /// <param name="date">Fecha de negocio.</param>
        /// <param name="mode">
        /// <see cref="BookValueMode.Monthly"/> (por omision): apertura del ejercicio menos
        /// <c>cargo * mesesTranscurridos / mesesDelEjercicio</c>; un mes cuenta cuando la fecha llega a su
        /// ultimo dia. <see cref="BookValueMode.Stepwise"/>: cierre del ultimo ejercicio cuyo fin es
        /// <c>&lt;= date</c>.
        /// </param>
        public decimal BookValueAt(DateOnly date, BookValueMode mode = BookValueMode.Monthly)
        {
            if (Periods.Count == 0)
                throw new InvalidOperationException("La tabla no tiene ejercicios.");

            var first = Periods[0];
            if (date < first.Start)
                return first.OpeningValue;

            if (mode == BookValueMode.Stepwise)
            {
                var value = first.OpeningValue;
                foreach (var period in Periods)
                {
                    if (period.End > date) break;
                    value = period.ClosingValue;
                }
                return value;
            }

            foreach (var period in Periods)
            {
                if (date > period.End) continue;
                if (date == period.End) return period.ClosingValue;

                var (total, elapsed) = CountMonths(period.Start, period.End, date);
                var consumed = Math.Round(period.Charge * elapsed / total, Rounding, MidpointRounding.AwayFromZero);
                return period.OpeningValue - consumed;
            }

            return Periods[^1].ClosingValue;
        }

        /// <summary>
        /// Depreciacion acumulada a una fecha: base depreciable menos <see cref="BookValueAt"/>.
        /// </summary>
        /// <param name="date">Fecha de negocio.</param>
        /// <param name="mode">Modo de calculo; ver <see cref="BookValueAt"/>.</param>
        public decimal AccumulatedAt(DateOnly date, BookValueMode mode = BookValueMode.Monthly)
            => Periods.Count == 0 ? 0m : Periods[0].OpeningValue - BookValueAt(date, mode);

        // Cuenta los tramos mensuales del ejercicio (meses de calendario recortados a [start, end]) y
        // cuantos ya transcurrieron: un tramo transcurre cuando date alcanza su ultimo dia.
        private static (int Total, int Elapsed) CountMonths(DateOnly start, DateOnly end, DateOnly date)
        {
            var total = 0;
            var elapsed = 0;
            var cursor = start;
            while (cursor <= end)
            {
                var monthEnd = new DateOnly(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
                var segmentEnd = monthEnd < end ? monthEnd : end;
                total++;
                if (segmentEnd <= date) elapsed++;
                cursor = segmentEnd.AddDays(1);
            }
            return (total, elapsed);
        }
    }
}
