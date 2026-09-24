using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Common.MultiTenancy;

namespace Common.Http
{
    /// <summary>
    /// Extensiones para clientes HTTP registrados con <c>AddHttpClient</c>.
    /// </summary>
    public static class HttpClientBuilderEx
    {
        /// <summary>
        /// Agrega el manejador de resiliencia estandar de Microsoft (reintentos, circuit breaker y timeouts).
        /// </summary>
        /// <param name="builder">Constructor del cliente HTTP.</param>
        /// <param name="section">
        /// Seccion opcional que se enlaza sobre las opciones estandar. Sin seccion se usan los valores por defecto.
        /// </param>
        /// <returns>El mismo constructor, para encadenar.</returns>
        public static IHttpClientBuilder AddCoreResilience(
            this IHttpClientBuilder builder,
            IConfigurationSection? section = null)
        {
            builder.AddStandardResilienceHandler(options =>
            {
                if (section is null)
                {
                    return;
                }

                section.Bind(options);
            });

            return builder;
        }

        /// <summary>
        /// Agrega <see cref="TenantPropagationHttpMessageHandler"/> para que cada llamada saliente lleve el
        /// tenant actual en un header.
        /// </summary>
        /// <remarks>Requiere <c>AddMultiTenancy</c>, que registra el accesor de tenant y las opciones.</remarks>
        /// <param name="builder">Constructor del cliente HTTP.</param>
        /// <returns>El mismo constructor, para encadenar.</returns>
        public static IHttpClientBuilder AddTenantPropagation(this IHttpClientBuilder builder)
        {
            builder.Services.AddTransient<TenantPropagationHttpMessageHandler>();
            builder.AddHttpMessageHandler<TenantPropagationHttpMessageHandler>();
            return builder;
        }
    }

    /// <summary>
    /// Manejador HTTP que copia el tenant actual al header <see cref="MultiTenantOptions.TenantHeaderName"/>
    /// de cada peticion saliente.
    /// </summary>
    /// <remarks>
    /// Si hay tenant, reemplaza cualquier valor previo del header. Si no hay tenant, no toca la peticion.
    /// </remarks>
    public sealed class TenantPropagationHttpMessageHandler : DelegatingHandler
    {
        private readonly ITenantContextAccessor _tenantContextAccessor;
        private readonly IOptions<MultiTenantOptions> _options;

        /// <summary>
        /// Crea el manejador.
        /// </summary>
        /// <param name="tenantContextAccessor">Accesor del tenant actual.</param>
        /// <param name="options">Opciones de multi-tenancy (nombre del header).</param>
        public TenantPropagationHttpMessageHandler(
            ITenantContextAccessor tenantContextAccessor,
            IOptions<MultiTenantOptions> options)
        {
            _tenantContextAccessor = tenantContextAccessor;
            _options = options;
        }

        /// <summary>
        /// Escribe el header de tenant, si hay tenant, y pasa la peticion al siguiente manejador.
        /// </summary>
        /// <param name="request">Peticion saliente.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>La respuesta del siguiente manejador.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var tenantId = _tenantContextAccessor.GetTenantId();
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                var headerName = _options.Value.TenantHeaderName;
                request.Headers.Remove(headerName);
                request.Headers.TryAddWithoutValidation(headerName, tenantId);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
