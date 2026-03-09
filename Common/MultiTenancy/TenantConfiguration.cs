using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Common.MultiTenancy
{
    public interface ITenantConfigurationStore
    {
        bool TryGetTenant(string tenantId, out TenantOptions tenantOptions);
    }

    public interface ITenantConnectionStringResolver
    {
        bool TryGetConnectionString(string tenantId, string name, out string connectionString);
        string GetRequiredConnectionString(string tenantId, string name = "Default");
    }

    public sealed class TenantConfigurationStore : ITenantConfigurationStore
    {
        private readonly IOptionsMonitor<MultiTenantOptions> _options;

        public TenantConfigurationStore(IOptionsMonitor<MultiTenantOptions> options)
        {
            _options = options;
        }

        public bool TryGetTenant(string tenantId, out TenantOptions tenantOptions)
        {
            tenantOptions = default!;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return false;
            }

            return _options.CurrentValue.Tenants.TryGetValue(tenantId, out tenantOptions!);
        }
    }

    public sealed class TenantConnectionStringResolver : ITenantConnectionStringResolver
    {
        private readonly ITenantConfigurationStore _tenantConfigurationStore;
        private readonly IConfiguration _configuration;

        public TenantConnectionStringResolver(
            ITenantConfigurationStore tenantConfigurationStore,
            IConfiguration configuration)
        {
            _tenantConfigurationStore = tenantConfigurationStore;
            _configuration = configuration;
        }

        public bool TryGetConnectionString(string tenantId, string name, out string connectionString)
        {
            connectionString = string.Empty;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return false;
            }

            var normalizedName = string.IsNullOrWhiteSpace(name) ? "Default" : name;
            if (_tenantConfigurationStore.TryGetTenant(tenantId, out var tenantOptions) &&
                tenantOptions.ConnectionStrings.TryGetValue(normalizedName, out var tenantConnectionString) &&
                !string.IsNullOrWhiteSpace(tenantConnectionString))
            {
                connectionString = tenantConnectionString;
                return true;
            }

            var fallbackConnectionString = _configuration.GetConnectionString(normalizedName);
            if (!string.IsNullOrWhiteSpace(fallbackConnectionString))
            {
                connectionString = fallbackConnectionString;
                return true;
            }

            return false;
        }

        public string GetRequiredConnectionString(string tenantId, string name = "Default")
        {
            if (TryGetConnectionString(tenantId, name, out var connectionString))
            {
                return connectionString;
            }

            throw new InvalidOperationException(
                $"Connection string '{name}' was not found for tenant '{tenantId}'.");
        }
    }
}
