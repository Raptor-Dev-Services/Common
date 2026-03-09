using Microsoft.AspNetCore.Http;

namespace Common.MultiTenancy
{
    public static class TenantAccessors
    {
        public static string? GetTenantId(this ITenantContextAccessor tenantContextAccessor)
        {
            return tenantContextAccessor.Current?.TenantId;
        }

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
