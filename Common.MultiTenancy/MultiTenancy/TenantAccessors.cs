using Microsoft.AspNetCore.Http;

namespace Common.MultiTenancy
{
    /// <summary>
    /// Atajos para leer el tenant actual.
    /// </summary>
    public static class TenantAccessors
    {
        /// <summary>
        /// Devuelve el id del tenant del contexto de ejecucion actual.
        /// </summary>
        /// <param name="tenantContextAccessor">Accesor del contexto de tenant.</param>
        /// <returns>El id, o <c>null</c> si no hay tenant.</returns>
        public static string? GetTenantId(this ITenantContextAccessor tenantContextAccessor)
        {
            return tenantContextAccessor.Current?.TenantId;
        }

        /// <summary>
        /// Devuelve el id del tenant que el middleware de resolucion dejo en
        /// <see cref="HttpContext.Items"/> (clave <c>"TenantContext"</c>).
        /// </summary>
        /// <param name="httpContext">Contexto HTTP de la peticion.</param>
        /// <returns>El id, o <c>null</c> si no se resolvio tenant para la peticion.</returns>
        public static string? GetTenantId(this HttpContext httpContext)
        {
            if (httpContext.Items.TryGetValue(nameof(TenantContext), out var tenantContext) &&
                tenantContext is TenantContext value)
            {
                return value.TenantId;
            }

            return null;
        }
    }
}
