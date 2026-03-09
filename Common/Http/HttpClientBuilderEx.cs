using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Common.MultiTenancy;

namespace Common.Http
{
    public static class HttpClientBuilderEx
    {
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

        public static IHttpClientBuilder AddTenantPropagation(this IHttpClientBuilder builder)
        {
            builder.Services.AddTransient<TenantPropagationHttpMessageHandler>();
            builder.AddHttpMessageHandler<TenantPropagationHttpMessageHandler>();
            return builder;
        }
    }

    public sealed class TenantPropagationHttpMessageHandler : DelegatingHandler
    {
        private readonly ITenantContextAccessor _tenantContextAccessor;
        private readonly IOptions<MultiTenantOptions> _options;

        public TenantPropagationHttpMessageHandler(
            ITenantContextAccessor tenantContextAccessor,
            IOptions<MultiTenantOptions> options)
        {
            _tenantContextAccessor = tenantContextAccessor;
            _options = options;
        }

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
