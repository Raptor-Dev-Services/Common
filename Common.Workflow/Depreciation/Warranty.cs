using System.Text.Json.Serialization;

namespace Common.Workflow.Depreciation
{
    /// <summary>
    /// Vencimiento de una garantia: sin garantia, de por vida, o una fecha.
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(None), "none")]
    [JsonDerivedType(typeof(Lifetime), "lifetime")]
    [JsonDerivedType(typeof(On), "on")]
    public abstract record WarrantyExpiry
    {
        private WarrantyExpiry() { }

        /// <summary>
        /// Sin garantia (duracion 0).
        /// </summary>
        public sealed record None : WarrantyExpiry;

        /// <summary>
        /// Garantia de por vida (duracion -1 en GLPI): nunca vence.
        /// </summary>
        public sealed record Lifetime : WarrantyExpiry;

        /// <summary>
        /// La garantia vence en una fecha.
        /// </summary>
        /// <param name="Date">Fecha de vencimiento (local de la sucursal).</param>
        public sealed record On(DateOnly Date) : WarrantyExpiry;
    }

    /// <summary>
    /// Calculo del vencimiento de garantia (GLPI <c>Infocom::getWarrantyExpir</c>).
    /// </summary>
    public static class Warranty
    {
        /// <summary>
        /// Vencimiento de una garantia que inicia en <paramref name="start"/> y dura <paramref name="months"/>.
        /// </summary>
        /// <param name="start">Inicio de la garantia.</param>
        /// <param name="months">
        /// Duracion en meses: <c>-1</c> de por vida, <c>0</c> sin garantia, positivo una duracion; los meses se
        /// suman con <see cref="DateOnly.AddMonths(int)"/> (2026-03-31 + 11 = 2027-02-28).
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">Si <paramref name="months"/> es menor que -1.</exception>
        public static WarrantyExpiry ExpiresOn(DateOnly start, int months)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(months, -1);
            return months switch
            {
                -1 => new WarrantyExpiry.Lifetime(),
                0 => new WarrantyExpiry.None(),
                _ => new WarrantyExpiry.On(start.AddMonths(months)),
            };
        }
    }
}
