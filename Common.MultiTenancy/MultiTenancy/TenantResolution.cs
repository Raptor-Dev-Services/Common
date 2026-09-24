using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Common.MultiTenancy
{
    /// <summary>
    /// Decide a que tenant pertenece una peticion HTTP.
    /// </summary>
    public interface ITenantResolver
    {
        /// <summary>
        /// Resuelve el id del tenant de la peticion.
        /// </summary>
        /// <param name="context">Contexto HTTP de la peticion.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>El id del tenant, o <c>null</c> si no se pudo resolver.</returns>
        ValueTask<string?> ResolveTenantIdAsync(HttpContext context, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// <see cref="ITenantResolver"/> que prueba, en este orden: header, query string, subdominio
    /// y tenant por defecto.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Orden: (1) header <see cref="MultiTenantOptions.TenantHeaderName"/> si
    /// <see cref="MultiTenantOptions.ResolveFromHeader"/>; (2) query string
    /// <see cref="MultiTenantOptions.TenantQueryStringKey"/> si
    /// <see cref="MultiTenantOptions.ResolveFromQueryString"/>; (3) primer segmento del host si
    /// <see cref="MultiTenantOptions.ResolveFromSubdomain"/>, el host es un nombre (no una direccion IP),
    /// tiene 3 o mas segmentos y ese segmento no esta en <see cref="MultiTenantOptions.IgnoredSubdomains"/>; (4)
    /// <see cref="MultiTenantOptions.DefaultTenantId"/>. Gana el primero con valor no vacio; header
    /// y query se recortan de espacios. No comprueba que el tenant exista.
    /// </para>
    /// <para>
    /// ADVERTENCIA: resolver el tenant desde un header o un query string que manda el cliente no
    /// autentica nada; cualquiera puede pedir el tenant que quiera. Detras de autenticacion, el
    /// tenant deberia salir de un claim verificado, no de estos valores.
    /// </para>
    /// </remarks>
    public sealed class DefaultTenantResolver : ITenantResolver
    {
        private readonly IOptions<MultiTenantOptions> _options;

        /// <summary>
        /// Crea el resolvedor.
        /// </summary>
        /// <param name="options">Opciones de multi-tenancy.</param>
        public DefaultTenantResolver(IOptions<MultiTenantOptions> options)
        {
            _options = options;
        }

        /// <summary>
        /// Resuelve el tenant con el orden header, query string, subdominio y tenant por defecto
        /// (ver remarks de la clase).
        /// </summary>
        /// <param name="context">Contexto HTTP de la peticion.</param>
        /// <param name="cancellationToken">No se usa.</param>
        /// <returns>El id del tenant, o <see cref="MultiTenantOptions.DefaultTenantId"/> (que puede ser <c>null</c>).</returns>
        public ValueTask<string?> ResolveTenantIdAsync(HttpContext context, CancellationToken cancellationToken = default)
        {
            var options = _options.Value;

            if (options.ResolveFromHeader &&
                context.Request.Headers.TryGetValue(options.TenantHeaderName, out var headerValue))
            {
                var tenantIdFromHeader = headerValue.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(tenantIdFromHeader))
                {
                    return ValueTask.FromResult<string?>(tenantIdFromHeader.Trim());
                }
            }

            if (options.ResolveFromQueryString &&
                context.Request.Query.TryGetValue(options.TenantQueryStringKey, out var queryValue))
            {
                var tenantIdFromQuery = queryValue.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(tenantIdFromQuery))
                {
                    return ValueTask.FromResult<string?>(tenantIdFromQuery.Trim());
                }
            }

            if (options.ResolveFromSubdomain)
            {
                var host = context.Request.Host.Host;
                // Una IP no tiene subdominio. Sin este corte, 192.168.1.10 tiene cuatro segmentos y
                // resolvia el tenant "192": las llamadas por IP (sondas, pruebas, trafico interno)
                // entraban con un tenant inventado.
                if (!string.IsNullOrWhiteSpace(host) && !IPAddress.TryParse(host, out _))
                {
                    var segments = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length >= 3)
                    {
                        var subdomain = segments[0].Trim();
                        if (!options.IgnoredSubdomains.Contains(subdomain))
                        {
                            return ValueTask.FromResult<string?>(subdomain);
                        }
                    }
                }
            }

            return ValueTask.FromResult<string?>(options.DefaultTenantId);
        }
    }
}
