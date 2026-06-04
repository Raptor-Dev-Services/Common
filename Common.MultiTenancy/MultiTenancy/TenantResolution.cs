using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Common.MultiTenancy
{
    public interface ITenantResolver
    {
        ValueTask<string?> ResolveTenantIdAsync(HttpContext context, CancellationToken cancellationToken = default);
    }

    public sealed class DefaultTenantResolver : ITenantResolver
    {
        private readonly IOptions<MultiTenantOptions> _options;

        public DefaultTenantResolver(IOptions<MultiTenantOptions> options)
        {
            _options = options;
        }

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
                if (!string.IsNullOrWhiteSpace(host))
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
