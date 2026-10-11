using System.Globalization;

namespace Common.Workflow.Folios
{
    /// <summary>
    /// Identifica un contador de folios: la secuencia, su ambito y, si reinicia, el periodo. Dos claves
    /// iguales comparten contador; dos distintas son independientes.
    /// </summary>
    /// <remarks>
    /// Se construye con <see cref="Global"/>, <see cref="ForTenant"/> o <see cref="ForBranch"/>, que validan
    /// los datos. El constructor posicional existe para serializar; una clave incoherente construida a mano
    /// se rechaza con <see cref="ArgumentException"/> al llamar a <see cref="ToScopeKey"/>.
    /// </remarks>
    /// <param name="Sequence">
    /// Nombre logico del contador (<c>support_ticket</c>): 1 a 64 caracteres <c>[a-z0-9_]</c>.
    /// </param>
    /// <param name="Scope">Ambito del contador.</param>
    /// <param name="TenantId">Id del tenant como texto (admite uuid y long). Obligatorio en Tenant y Branch.</param>
    /// <param name="BranchId">Id de la sucursal como texto. Obligatorio solo en Branch.</param>
    /// <param name="Period">Periodo del contador (<c>2026</c>) si reinicia; nulo si no.</param>
    public sealed record FolioKey(string Sequence, FolioScope Scope, string? TenantId, string? BranchId, string? Period)
    {
        /// <summary>Largo maximo de <see cref="ToScopeKey"/> (columna <c>scope_key varchar(160)</c>).</summary>
        public const int MaxScopeKeyLength = 160;

        /// <summary>Largo maximo de <see cref="Sequence"/> (columna <c>sequence_name varchar(64)</c>).</summary>
        public const int MaxSequenceLength = 64;

        /// <summary>
        /// Clave de un contador global: uno solo en todo el producto.
        /// </summary>
        /// <param name="sequence">Nombre logico del contador.</param>
        /// <returns>La clave.</returns>
        /// <exception cref="ArgumentException">Si el nombre no es valido.</exception>
        public static FolioKey Global(string sequence) =>
            Validated(new FolioKey(sequence, FolioScope.Global, null, null, null));

        /// <summary>
        /// Clave de un contador por tenant.
        /// </summary>
        /// <param name="sequence">Nombre logico del contador.</param>
        /// <param name="tenantId">Id del tenant como texto.</param>
        /// <returns>La clave.</returns>
        /// <exception cref="ArgumentException">Si el nombre o el id no son validos.</exception>
        public static FolioKey ForTenant(string sequence, string tenantId) =>
            Validated(new FolioKey(sequence, FolioScope.Tenant, tenantId, null, null));

        /// <summary>
        /// Clave de un contador por sucursal.
        /// </summary>
        /// <param name="sequence">Nombre logico del contador.</param>
        /// <param name="tenantId">Id del tenant como texto.</param>
        /// <param name="branchId">Id de la sucursal como texto.</param>
        /// <returns>La clave.</returns>
        /// <exception cref="ArgumentException">Si el nombre o algun id no son validos.</exception>
        public static FolioKey ForBranch(string sequence, string tenantId, string branchId) =>
            Validated(new FolioKey(sequence, FolioScope.Branch, tenantId, branchId, null));

        /// <summary>
        /// La misma clave con el periodo de un ano. El ano es el <b>local</b> de la sucursal o tenant en el
        /// momento de asignar, nunca el ano UTC: lo calcula el producto con su reloj de sucursal.
        /// </summary>
        /// <param name="year">Ano local, de 1 a 9999.</param>
        /// <returns>Una clave nueva con <see cref="Period"/> = el ano en cuatro cifras.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Si el ano esta fuera de rango.</exception>
        public FolioKey WithPeriod(int year)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(year, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(year, 9999);
            return Validated(this with { Period = year.ToString("D4", CultureInfo.InvariantCulture) });
        }

        /// <summary>
        /// La clave de ambito tal como se guarda en <c>folio_counter.scope_key</c>: <c>global</c>,
        /// <c>t:&lt;tenant&gt;</c> o <c>t:&lt;tenant&gt;/b:&lt;sucursal&gt;</c>, con <c>/p:&lt;periodo&gt;</c> al final
        /// si hay periodo.
        /// </summary>
        /// <returns>La clave de ambito, de 160 caracteres como maximo.</returns>
        /// <exception cref="ArgumentException">Si la clave es incoherente o excede 160 caracteres.</exception>
        public string ToScopeKey()
        {
            Validate();
            var scope = Scope switch
            {
                FolioScope.Global => "global",
                FolioScope.Tenant => "t:" + TenantId,
                _ => "t:" + TenantId + "/b:" + BranchId,
            };
            if (Period is not null)
                scope += "/p:" + Period;

            if (scope.Length > MaxScopeKeyLength)
                throw new ArgumentException(
                    $"La clave de ambito '{scope}' excede {MaxScopeKeyLength} caracteres.", nameof(TenantId));
            return scope;
        }

        private static FolioKey Validated(FolioKey key)
        {
            key.ToScopeKey();
            return key;
        }

        private void Validate()
        {
            if (string.IsNullOrEmpty(Sequence) || Sequence.Length > MaxSequenceLength
                || !Sequence.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
                throw new ArgumentException(
                    $"La secuencia debe tener de 1 a {MaxSequenceLength} caracteres [a-z0-9_]: '{Sequence}'.",
                    nameof(Sequence));

            switch (Scope)
            {
                case FolioScope.Global:
                    RequireAbsent(TenantId, nameof(TenantId));
                    RequireAbsent(BranchId, nameof(BranchId));
                    break;
                case FolioScope.Tenant:
                    RequireId(TenantId, nameof(TenantId));
                    RequireAbsent(BranchId, nameof(BranchId));
                    break;
                case FolioScope.Branch:
                    RequireId(TenantId, nameof(TenantId));
                    RequireId(BranchId, nameof(BranchId));
                    break;
                default:
                    throw new ArgumentException($"Ambito desconocido: {Scope}.", nameof(Scope));
            }

            if (Period is not null && (Period.Length is 0 or > 16 || !Period.All(char.IsAsciiLetterOrDigit)))
                throw new ArgumentException(
                    $"El periodo debe tener de 1 a 16 letras o cifras ASCII: '{Period}'.", nameof(Period));
        }

        private void RequireId(string? id, string name)
        {
            // La diagonal separa las partes de la clave: un id con '/' podria chocar con otro ambito.
            if (string.IsNullOrWhiteSpace(id) || id.Contains('/') || id.Any(char.IsWhiteSpace))
                throw new ArgumentException(
                    $"El ambito {Scope} exige {name} sin espacios ni '/': '{id}'.", name);
        }

        private void RequireAbsent(string? id, string name)
        {
            if (id is not null)
                throw new ArgumentException($"El ambito {Scope} no lleva {name}.", name);
        }
    }
}
