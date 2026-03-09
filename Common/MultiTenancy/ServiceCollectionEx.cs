using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Common.MultiTenancy
{
    public static class ServiceCollectionEx
    {
        public static IServiceCollection AddMultiTenancy(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<MultiTenantOptions>? configure = null)
        {
            var section = configuration.GetSection(MultiTenantOptions.SectionName);
            services.AddOptions<MultiTenantOptions>()
                .Bind(section)
                .PostConfigure(options => configure?.Invoke(options))
                .Validate(options => !options.RequireTenant || !string.IsNullOrWhiteSpace(options.DefaultTenantId) || options.ResolveFromHeader || options.ResolveFromQueryString || options.ResolveFromSubdomain,
                    "RequireTenant is enabled but no tenant resolution strategy is configured.")
                .ValidateOnStart();

            services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
            services.AddSingleton<ITenantResolver, DefaultTenantResolver>();
            services.AddSingleton<ITenantConfigurationStore, TenantConfigurationStore>();
            services.AddSingleton<ITenantConnectionStringResolver, TenantConnectionStringResolver>();
            services.AddSingleton<ITenantExecutionContextRunner, TenantExecutionContextRunner>();

            return services;
        }
    }
}
