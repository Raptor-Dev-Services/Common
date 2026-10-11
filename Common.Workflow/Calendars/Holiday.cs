namespace Common.Workflow.Calendars
{
    /// <summary>
    /// Un feriado: rango de fechas locales, ambos extremos incluidos, que no cuentan como dias habiles
    /// aunque tengan tramos de horario.
    /// </summary>
    /// <param name="Begin">Primer dia del feriado.</param>
    /// <param name="End">Ultimo dia del feriado (incluido).</param>
    /// <param name="IsPerpetual">
    /// Si es verdadero se ignora el anio y se compara el mes y el dia (<c>MM-dd</c>); un rango con
    /// <c>Begin &gt; End</c> en <c>MM-dd</c> cruza el fin de anio (<c>12-24..01-06</c>). Un perpetuo
    /// <c>02-29</c> solo coincide en anios bisiestos.
    /// </param>
    /// <param name="Name">Nombre opcional, solo informativo.</param>
    public sealed record Holiday(DateOnly Begin, DateOnly End, bool IsPerpetual, string? Name = null);
}
