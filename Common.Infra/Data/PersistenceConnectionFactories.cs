using System.Data;
using System.Data.Common;
using Common.MultiTenancy;
using Microsoft.Extensions.Configuration;

namespace Common.Data
{
    public interface IOpenDbConnectionFactory
    {
        Task<IDbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default);
    }

    public interface ITenantOpenDbConnectionFactory
    {
        Task<IDbConnection> GetOpenConnectionAsync(string tenantId, CancellationToken cancellationToken = default);
    }

    public abstract class DbConnectionFactory : IOpenDbConnectionFactory
    {
        protected readonly string ConnectionString;

        protected DbConnectionFactory(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Connection string is required.", nameof(connectionString));
            }

            ConnectionString = connectionString;
        }

        public async Task<IDbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = CreateConnection(ConnectionString);
            await OpenConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }

        protected abstract IDbConnection CreateConnection(string connectionString);

        internal static async Task OpenConnectionAsync(IDbConnection connection, CancellationToken cancellationToken)
        {
            if (connection is DbConnection dbConnection)
            {
                await dbConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            connection.Open();
        }
    }

    public abstract class ConfigurationDbConnectionFactory<TConnectionName> : DbConnectionFactory
    {
        protected ConfigurationDbConnectionFactory(IConfiguration configuration)
            : base(configuration.GetConnectionString(typeof(TConnectionName).Name) ??
                   throw new InvalidOperationException($"Connection string '{typeof(TConnectionName).Name}' was not found."))
        {
        }
    }

    public abstract class TenantDbConnectionFactory : ITenantOpenDbConnectionFactory
    {
        private readonly ITenantConnectionStringResolver _tenantConnectionStringResolver;
        private readonly string _connectionName;

        protected TenantDbConnectionFactory(
            ITenantConnectionStringResolver tenantConnectionStringResolver,
            string connectionName = "Default")
        {
            _tenantConnectionStringResolver = tenantConnectionStringResolver;
            _connectionName = connectionName;
        }

        public async Task<IDbConnection> GetOpenConnectionAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            var connectionString = _tenantConnectionStringResolver.GetRequiredConnectionString(tenantId, _connectionName);
            var connection = CreateConnection(connectionString);
            await DbConnectionFactory.OpenConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }

        protected abstract IDbConnection CreateConnection(string connectionString);
    }

    public abstract class CurrentTenantDbConnectionFactory : IOpenDbConnectionFactory
    {
        private readonly ITenantContextAccessor _tenantContextAccessor;
        private readonly ITenantOpenDbConnectionFactory _tenantFactory;

        protected CurrentTenantDbConnectionFactory(
            ITenantContextAccessor tenantContextAccessor,
            ITenantOpenDbConnectionFactory tenantFactory)
        {
            _tenantContextAccessor = tenantContextAccessor;
            _tenantFactory = tenantFactory;
        }

        public Task<IDbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var tenantId = _tenantContextAccessor.GetTenantId();
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                throw new InvalidOperationException("Tenant context is not available in the current request.");
            }

            return _tenantFactory.GetOpenConnectionAsync(tenantId, cancellationToken);
        }
    }
}
