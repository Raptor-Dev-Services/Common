using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace Common.HealthChecks
{
    /// <summary>
    /// Registro de health checks.
    /// </summary>
    public static class ServiceCollectionEx
    {
        /// <summary>
        /// Registra los health checks y un check <c>"self"</c> que siempre responde Healthy (sirve de liveness).
        /// No mapea ningun endpoint: eso lo hace quien consume con <c>MapHealthChecks</c>.
        /// </summary>
        /// <param name="services">Coleccion de servicios.</param>
        /// <returns>El constructor de health checks, para agregar mas.</returns>
        public static IHealthChecksBuilder AddCoreHealthChecks(this IServiceCollection services)
        {
            return services.AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy());
        }

        /// <summary>
        /// Agrega un check de PostgreSQL (<c>AddNpgSql</c>) con nombre por defecto <c>"sql"</c>.
        /// </summary>
        /// <remarks>Pese al nombre, solo sirve para PostgreSQL: es identico a <see cref="AddPostgreSqlHealthCheck"/> salvo por el nombre por defecto.</remarks>
        /// <param name="builder">Constructor de health checks.</param>
        /// <param name="connectionString">Cadena de conexion.</param>
        /// <param name="name">Nombre del check.</param>
        /// <param name="failureStatus">Estado que se reporta si falla; si es <c>null</c>, Unhealthy.</param>
        /// <param name="tags">Etiquetas del check, para filtrar endpoints.</param>
        /// <param name="timeout">Tiempo maximo del check.</param>
        /// <returns>El mismo constructor, para encadenar.</returns>
        public static IHealthChecksBuilder AddSqlHealthCheck(
            this IHealthChecksBuilder builder,
            string connectionString,
            string name = "sql",
            HealthStatus? failureStatus = null,
            IEnumerable<string>? tags = null,
            TimeSpan? timeout = null)
        {
            return builder.AddNpgSql(
                connectionString: connectionString,
                name: name,
                failureStatus: failureStatus,
                tags: tags,
                timeout: timeout);
        }

        /// <summary>
        /// Agrega un check de PostgreSQL (<c>AddNpgSql</c>) con nombre por defecto <c>"postgres"</c>.
        /// </summary>
        /// <param name="builder">Constructor de health checks.</param>
        /// <param name="connectionString">Cadena de conexion.</param>
        /// <param name="name">Nombre del check.</param>
        /// <param name="failureStatus">Estado que se reporta si falla; si es <c>null</c>, Unhealthy.</param>
        /// <param name="tags">Etiquetas del check, para filtrar endpoints.</param>
        /// <param name="timeout">Tiempo maximo del check.</param>
        /// <returns>El mismo constructor, para encadenar.</returns>
        public static IHealthChecksBuilder AddPostgreSqlHealthCheck(
            this IHealthChecksBuilder builder,
            string connectionString,
            string name = "postgres",
            HealthStatus? failureStatus = null,
            IEnumerable<string>? tags = null,
            TimeSpan? timeout = null)
        {
            return builder.AddNpgSql(
                connectionString: connectionString,
                name: name,
                failureStatus: failureStatus,
                tags: tags,
                timeout: timeout);
        }

        /// <summary>
        /// Agrega un check de Redis con nombre por defecto <c>"redis"</c>.
        /// </summary>
        /// <param name="builder">Constructor de health checks.</param>
        /// <param name="connectionString">Cadena de conexion de Redis.</param>
        /// <param name="name">Nombre del check.</param>
        /// <param name="failureStatus">Estado que se reporta si falla; si es <c>null</c>, Unhealthy.</param>
        /// <param name="tags">Etiquetas del check, para filtrar endpoints.</param>
        /// <param name="timeout">Tiempo maximo del check.</param>
        /// <returns>El mismo constructor, para encadenar.</returns>
        public static IHealthChecksBuilder AddRedisHealthCheck(
            this IHealthChecksBuilder builder,
            string connectionString,
            string name = "redis",
            HealthStatus? failureStatus = null,
            IEnumerable<string>? tags = null,
            TimeSpan? timeout = null)
        {
            return builder.AddRedis(
                redisConnectionString: connectionString,
                name: name,
                failureStatus: failureStatus,
                tags: tags,
                timeout: timeout);
        }
    }
}
