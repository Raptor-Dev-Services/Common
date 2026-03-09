using System.Data;
using Common.Data;
using Common.MultiTenancy;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Common.PostgreSql
{
    public abstract class ConfigurationNpgsqlConnectionFactory<TConnectionName>
        : ConfigurationDbConnectionFactory<TConnectionName>
    {
        protected ConfigurationNpgsqlConnectionFactory(IConfiguration configuration)
            : base(configuration)
        {
        }

        protected override IDbConnection CreateConnection(string connectionString)
            => new NpgsqlConnection(connectionString);
    }

    public abstract class TenantNpgsqlConnectionFactory : TenantDbConnectionFactory
    {
        protected TenantNpgsqlConnectionFactory(
            ITenantConnectionStringResolver tenantConnectionStringResolver,
            string connectionName = "Default")
            : base(tenantConnectionStringResolver, connectionName)
        {
        }

        protected override IDbConnection CreateConnection(string connectionString)
            => new NpgsqlConnection(connectionString);
    }

    public abstract class CurrentTenantNpgsqlConnectionFactory : CurrentTenantDbConnectionFactory
    {
        protected CurrentTenantNpgsqlConnectionFactory(
            ITenantContextAccessor tenantContextAccessor,
            ITenantOpenDbConnectionFactory tenantFactory)
            : base(tenantContextAccessor, tenantFactory)
        {
        }
    }
}
