namespace Common.Workflow.Depreciation
{
    /// <summary>
    /// Dia y mes en que cierra el ejercicio. El ejercicio <c>N</c> es el que termina en el anio <c>N</c>.
    /// </summary>
    /// <param name="Month">Mes de cierre (1-12).</param>
    /// <param name="Day">Dia de cierre; debe existir en un anio no bisiesto (no se admite 02-29).</param>
    public readonly record struct FiscalYearEnd(int Month, int Day)
    {
        /// <summary>
        /// Cierre al 31 de diciembre: el ejercicio fiscal mexicano (CFF art. 11) y el valor por omision.
        /// </summary>
        public static readonly FiscalYearEnd December31 = new(12, 31);

        /// <summary>
        /// Indica si el cierre existe todos los anios. 02-29 no cuenta: el ejercicio cambiaria de largo.
        /// </summary>
        public bool IsValid => Month is >= 1 and <= 12 && Day >= 1 && Day <= DateTime.DaysInMonth(2001, Month);

        /// <summary>
        /// Fecha de cierre del ejercicio que termina en <paramref name="year"/>.
        /// </summary>
        /// <param name="year">Anio en que termina el ejercicio.</param>
        public DateOnly EndOf(int year) => new(year, Month, Day);

        /// <summary>
        /// Fecha de cierre del ejercicio que contiene <paramref name="date"/>.
        /// </summary>
        /// <param name="date">Fecha cualquiera.</param>
        public DateOnly EndContaining(DateOnly date)
        {
            var end = EndOf(date.Year);
            return date <= end ? end : EndOf(date.Year + 1);
        }
    }
}
