using Microsoft.Extensions.DependencyInjection;

namespace Common.PostgreSql
{
    /// <summary>
    /// Registro de las migraciones de esquema en PostgreSQL.
    /// </summary>
    public static class ServiceCollectionEx
    {
        /// <summary>
        /// Registra <see cref="SchemaMigrationHostedService"/>, que aplica los scripts <c>.sql</c> pendientes
        /// al arrancar el host (ver sus remarks).
        /// </summary>
        /// <remarks>
        /// <paramref name="configure"/> se ejecuta en el momento de la llamada, no al resolver las opciones.
        /// Requiere un <see cref="Common.Data.IOpenDbConnectionFactory"/> registrado.
        /// </remarks>
        /// <param name="services">Coleccion de servicios.</param>
        /// <param name="configure">Ajuste opcional de las opciones, por ejemplo otra carpeta de scripts.</param>
        /// <returns>La misma coleccion, para encadenar.</returns>
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
