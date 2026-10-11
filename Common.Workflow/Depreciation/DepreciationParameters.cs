namespace Common.Workflow.Depreciation
{
    /// <summary>
    /// Parametros de una tabla de depreciacion. La tabla fiscal (LISR) y la contable (NIF C-6) del mismo
    /// activo son dos juegos distintos de parametros. Las tasas legales <b>no</b> viven en la libreria: el
    /// producto las siembra en su catalogo de clases de activo y las pasa en <see cref="AnnualRate"/>.
    /// </summary>
    /// <param name="Method">Metodo de depreciacion.</param>
    /// <param name="Cost">Monto original de la inversion (MOI); mayor que cero.</param>
    /// <param name="InServiceDate">Fecha de inicio de uso; la linea recta arranca aqui.</param>
    /// <param name="LifeYears">Vida util en anios. Exactamente uno de <c>LifeYears</c> o <c>AnnualRate</c>.</param>
    /// <param name="AnnualRate">Tasa anual sobre la base, en (0, 1] (p. ej. 0.30 para computo).</param>
    /// <param name="AcquisitionDate">
    /// Fecha de compra; el saldo decreciente arranca aqui. Nula: se usa <paramref name="InServiceDate"/>.
    /// </param>
    /// <param name="DecliningCoefficient">Coeficiente del saldo decreciente; mayor que 1.</param>
    /// <param name="FiscalYearEnd">Cierre del ejercicio; nulo equivale a 12-31.</param>
    /// <param name="FirstYearProration">Prorrata del primer ejercicio en linea recta.</param>
    /// <param name="SalvageValue">Valor residual; mayor o igual a cero y menor que la base.</param>
    /// <param name="DepreciableCap">Tope de la base deducible (LISR art. 36 fr. II); nulo = sin tope.</param>
    /// <param name="StartInNextFiscalYear">
    /// Si es <c>true</c>, el primer ejercicio no carga nada y la depreciacion empieza completa en el
    /// siguiente (LISR art. 31, opcion del contribuyente).
    /// </param>
    /// <param name="Rounding">Decimales de cada cargo (0-28); por omision 2.</param>
    public sealed record DepreciationParameters(
        DepreciationMethod Method, decimal Cost, DateOnly InServiceDate,
        decimal? LifeYears = null, decimal? AnnualRate = null,
        DateOnly? AcquisitionDate = null, decimal DecliningCoefficient = 2m,
        FiscalYearEnd? FiscalYearEnd = null,
        FirstYearProration FirstYearProration = FirstYearProration.Days360,
        decimal SalvageValue = 0m, decimal? DepreciableCap = null,
        bool StartInNextFiscalYear = false, int Rounding = 2);
}
