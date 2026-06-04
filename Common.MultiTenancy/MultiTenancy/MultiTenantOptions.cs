namespace Common.MultiTenancy
{
    public sealed class MultiTenantOptions
    {
        public const string SectionName = "MultiTenancy";

        public bool RequireTenant { get; set; }
        public bool RejectUnknownTenants { get; set; } = true;
        public string? DefaultTenantId { get; set; }
        public string TenantHeaderName { get; set; } = "X-Tenant-Id";
        public string TenantQueryStringKey { get; set; } = "tenant";
        public bool ResolveFromHeader { get; set; } = true;
        public bool ResolveFromQueryString { get; set; }
        public bool ResolveFromSubdomain { get; set; } = true;
        public string TenantResponseHeaderName { get; set; } = "X-Tenant-Id";
        public HashSet<string> IgnoredSubdomains { get; set; } = new(StringComparer.OrdinalIgnoreCase) { "www", "api" };
        public Dictionary<string, TenantOptions> Tenants { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class TenantOptions
    {
        public bool IsEnabled { get; set; } = true;
        public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> ConnectionStrings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
