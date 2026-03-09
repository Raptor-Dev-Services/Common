using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace Common.HealthChecks
{
    public static class ServiceCollectionEx
    {
        public static IHealthChecksBuilder AddCoreHealthChecks(this IServiceCollection services)
        {
            return services.AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy());
        }

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
