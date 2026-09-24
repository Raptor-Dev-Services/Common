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
        /// Asignar un tenant crea un contenedor nuevo para este flujo y sus derivados, sin tocar el del
        /// flujo que lo llamo. Asignar <c>null</c> vacia el contenedor actual, de modo que todo lo que
        /// derivo de el (por ejemplo, trabajo lanzado durante una peticion que ya termino) deja de ver
        /// el tenant.
        /// </remarks>
        public TenantContext? Current
        {
            get => Holder.Value?.Context;
            set
            {
                if (value is null)
                {
                    // Fin de alcance: se vacia el contenedor para que los flujos derivados que lo
                    // capturaron tampoco vean ya el tenant.
                    var current = Holder.Value;
                    if (current is not null)
                    {
                        current.Context = null;
                    }

                    return;
                }

                // Un tenant nuevo NO vacia el contenedor anterior. Antes si lo hacia, y ese
                // contenedor se comparte con el flujo que llamo: un RunAsync dentro de una
                // peticion con tenant dejaba a la peticion sin tenant al volver.
                Holder.Value = new TenantContextHolder { Context = value };
            }
        }

        private sealed class TenantContextHolder
        {
            public TenantContext? Context;
        }
    }
}
