using System.Threading;

namespace Common.MultiTenancy
{
    /// <summary>
    /// Tenant del contexto de ejecucion actual.
    /// </summary>
    public sealed class TenantContext
    {
        /// <summary>
        /// Crea el contexto. No valida el id.
        /// </summary>
        /// <param name="tenantId">Id del tenant.</param>
        public TenantContext(string tenantId)
        {
            TenantId = tenantId;
        }

        /// <summary>
        /// Id del tenant.
        /// </summary>
        public string TenantId { get; }
    }

    /// <summary>
    /// Da acceso al <see cref="TenantContext"/> del flujo de ejecucion actual.
    /// </summary>
    public interface ITenantContextAccessor
    {
        /// <summary>
        /// Tenant actual, o <c>null</c> si no hay.
        /// </summary>
        TenantContext? Current { get; set; }
    }

    /// <summary>
    /// <see cref="ITenantContextAccessor"/> basado en <see cref="AsyncLocal{T}"/>: el tenant
    /// fluye con el contexto de ejecucion (a traves de <c>await</c>), no por hilo.
    /// </summary>
    /// <remarks>
    /// El almacenamiento es estatico: todas las instancias comparten el mismo valor para un
    /// mismo flujo de ejecucion.
    /// </remarks>
    public sealed class TenantContextAccessor : ITenantContextAccessor
    {
        private static readonly AsyncLocal<TenantContextHolder> Holder = new();

        /// <summary>
        /// Id del tenant actual sin necesidad de una instancia (lo usa el enriquecedor de logs).
        /// <c>null</c> si no hay tenant.
        /// </summary>
        public static string? CurrentTenantId => Holder.Value?.Context?.TenantId;

        /// <summary>
        /// Tenant actual, o <c>null</c> si no hay.
        /// </summary>
        /// <remarks>
        /// Al asignar, primero se vacia el contenedor actual, y ese contenedor es un objeto
        /// compartido con los flujos de los que este deriva: vaciarlo tambien deja sin tenant
        /// a quien lo habia puesto. Despues, si el valor no es <c>null</c>, se crea un contenedor
        /// nuevo solo para este flujo y sus derivados.
        /// </remarks>
        public TenantContext? Current
        {
            get => Holder.Value?.Context;
            set
            {
                var current = Holder.Value;
                if (current is not null)
                {
                    current.Context = null;
                }

                if (value is not null)
                {
                    Holder.Value = new TenantContextHolder { Context = value };
                }
            }
        }

        private sealed class TenantContextHolder
        {
            public TenantContext? Context;
        }
    }
}
