using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Common.MultiTenancy
{
    /// <summary>
    /// Registro de los servicios de multi-tenancy.
    /// </summary>
    public static class ServiceCollectionEx
    {
        /// <summary>
        /// Registra <see cref="MultiTenantOptions"/> y los servicios de tenant, todos Singleton:
        /// <see cref="ITenantContextAccessor"/>, <see cref="ITenantResolver"/> (<see cref="DefaultTenantResolver"/>),
        /// <see cref="ITenantConfigurationStore"/>, <see cref="ITenantConnectionStringResolver"/> e
        /// <see cref="ITenantExecutionContextRunner"/>.
        /// </summary>
        /// <remarks>
        /// Las opciones se enlazan a la seccion <see cref="MultiTenantOptions.SectionName"/> y
        /// despues se aplica <paramref name="configure"/>. Se validan al arrancar: falla si
        /// <see cref="MultiTenantOptions.RequireTenant"/> esta activo sin ninguna estrategia de
        /// resolucion ni tenant por defecto. No registra el middleware: eso es <c>UseTenantResolution</c>.
        /// </remarks>
        /// <param name="services">Coleccion de servicios.</param>
        /// <param name="configuration">Configuracion de la que se lee la seccion.</param>
        /// <param name="configure">Ajuste opcional que se aplica despues de leer la configuracion.</param>
        /// <returns>La misma coleccion, para encadenar.</returns>
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
