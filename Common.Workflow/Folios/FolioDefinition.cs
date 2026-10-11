namespace Common.Workflow.Folios
{
    /// <summary>
    /// Declaracion de una secuencia de folios del producto: nombre, ambito, formato, modo y reinicio. Es un
    /// record serializable; el producto la declara en codigo o la guarda en su configuracion.
    /// </summary>
    /// <param name="Sequence">Nombre logico del contador (<c>support_ticket</c>).</param>
    /// <param name="Scope">Ambito del contador.</param>
    /// <param name="Format">Como se escribe y se reconoce el folio.</param>
    /// <param name="Mode">Con huecos (por omision) o sin huecos.</param>
    /// <param name="Reset">Si reinicia cada ano local (por omision nunca).</param>
    public sealed record FolioDefinition(string Sequence, FolioScope Scope, FolioFormat Format,
        FolioMode Mode = FolioMode.AllowGaps, FolioReset Reset = FolioReset.Never)
    {
        /// <summary>
        /// La clave del contador para una alta. Con <see cref="FolioReset.Yearly"/> el periodo es el ano de
        /// <paramref name="localDate"/>, que es la fecha <b>local</b> de la sucursal o tenant en el momento de
        /// asignar (nunca la fecha UTC); con <see cref="FolioReset.Never"/> no hay periodo.
        /// </summary>
        /// <param name="localDate">Fecha local de la sucursal/tenant al asignar.</param>
        /// <param name="tenantId">Id del tenant, obligatorio en ambitos Tenant y Branch; se ignora en Global.</param>
        /// <param name="branchId">Id de la sucursal, obligatorio en ambito Branch; se ignora en los demas.</param>
        /// <returns>La clave lista para <see cref="IFolioGenerator.NextAsync"/>.</returns>
        /// <exception cref="ArgumentException">Si faltan ids que el ambito exige o algun dato no es valido.</exception>
        public FolioKey KeyFor(DateOnly localDate, string? tenantId = null, string? branchId = null)
        {
            var key = Scope switch
            {
                FolioScope.Global => FolioKey.Global(Sequence),
                FolioScope.Tenant => FolioKey.ForTenant(Sequence, tenantId!),
                FolioScope.Branch => FolioKey.ForBranch(Sequence, tenantId!, branchId!),
                _ => throw new ArgumentException($"Ambito desconocido: {Scope}.", nameof(Scope)),
            };
            return Reset == FolioReset.Yearly ? key.WithPeriod(localDate.Year) : key;
        }

        /// <summary>
        /// Escribe el folio de un numero asignado con <paramref name="key"/>, incluyendo su periodo si lo tiene.
        /// </summary>
        /// <param name="number">Numero asignado.</param>
        /// <param name="key">Clave con la que se asigno.</param>
        /// <returns>El folio formateado.</returns>
        public string FormatFor(long number, FolioKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return Format.Format(number, key.Period);
        }
    }
}
