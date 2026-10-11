namespace Common.Workflow.Depreciation
{
    /// <summary>
    /// Costo total de propiedad de un activo.
    /// </summary>
    /// <param name="Total">Costo del activo mas los costos adicionales.</param>
    /// <param name="Monthly">Total entre los meses completos desde la compra (minimo 1), al centavo.</param>
    /// <param name="Months">Meses completos transcurridos desde la compra.</param>
    public sealed record TcoResult(decimal Total, decimal Monthly, int Months);

    /// <summary>
    /// Calculo del TCO (GLPI Infocom: "suma de costos de tickets + valor; TCO mensual = TCO / meses").
    /// Los costos adicionales (mantenimiento, refacciones, contratos) los suma el producto.
    /// </summary>
    public static class Tco
    {
        /// <summary>
        /// Calcula el TCO a una fecha.
        /// </summary>
        /// <param name="cost">Costo del activo.</param>
        /// <param name="additionalCosts">Costos adicionales a sumar.</param>
        /// <param name="purchaseDate">Fecha de compra.</param>
        /// <param name="asOf">Fecha de corte; no anterior a la compra.</param>
        /// <exception cref="ArgumentNullException">Si <paramref name="additionalCosts"/> es nulo.</exception>
        /// <exception cref="ArgumentException">Si <paramref name="asOf"/> es anterior a <paramref name="purchaseDate"/>.</exception>
        public static TcoResult Compute(decimal cost, IEnumerable<decimal> additionalCosts,
            DateOnly purchaseDate, DateOnly asOf)
        {
            ArgumentNullException.ThrowIfNull(additionalCosts);
            if (asOf < purchaseDate)
                throw new ArgumentException("La fecha de corte no puede ser anterior a la compra.", nameof(asOf));

            var total = cost + additionalCosts.Sum();
            var months = 0;
            while (purchaseDate.AddMonths(months + 1) <= asOf) months++;

            var monthly = Math.Round(total / Math.Max(1, months), 2, MidpointRounding.AwayFromZero);
            return new TcoResult(total, monthly, months);
        }
    }
}
