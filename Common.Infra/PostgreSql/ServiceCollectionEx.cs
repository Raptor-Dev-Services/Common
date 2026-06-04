using Microsoft.Extensions.DependencyInjection;

namespace Common.PostgreSql
{
    public static class ServiceCollectionEx
    {
        public static IServiceCollection AddSchemaMigrations(
            this IServiceCollection services,
            Action<SchemaMigrationOptions>? configure = null)
        {
            var options = new SchemaMigrationOptions();
            configure?.Invoke(options);

            services.Configure<SchemaMigrationOptions>(settings =>
            {
                settings.ScriptsRelativePath = options.ScriptsRelativePath;
            });

            services.AddHostedService<SchemaMigrationHostedService>();
            return services;
        }
    }
}
