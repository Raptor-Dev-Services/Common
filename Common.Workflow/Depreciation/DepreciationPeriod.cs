namespace Common.Workflow.Depreciation
{
    /// <summary>
    /// Un ejercicio de la tabla de depreciacion.
    /// </summary>
    /// <param name="FiscalYear">Anio en que cierra el ejercicio.</param>
    /// <param name="Start">Primer dia del ejercicio (o del alta, en el primero).</param>
    /// <param name="End">Dia de cierre del ejercicio.</param>
    /// <param name="OpeningValue">Valor en libros al abrir el ejercicio.</param>
    /// <param name="Charge">Depreciacion del ejercicio, redondeada.</param>
    /// <param name="ClosingValue">Valor en libros al cierre: apertura menos cargo.</param>
    /// <param name="Factor">
    /// Fraccion de la anualidad aplicada: la prorrata en el primero, 1 en los completos, 0 si el primer
    /// ejercicio se difiere, y en linea recta la fraccion que queda en el ultimo (el residuo).
    /// </param>
    /// <param name="MethodApplied">
    /// Metodo con que se calculo el cargo; en saldo decreciente cambia a linea recta en los ultimos anios.
    /// </param>
    public sealed record DepreciationPeriod(int FiscalYear, DateOnly Start, DateOnly End,
        decimal OpeningValue, decimal Charge, decimal ClosingValue, decimal Factor,
        DepreciationMethod MethodApplied);
}
