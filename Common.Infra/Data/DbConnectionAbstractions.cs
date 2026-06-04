using System.Data.Common;
using Common.MultiTenancy;

namespace Common.Data
{
    public interface IDbConnectionFactory<TConnection> where TConnection : DbConnection
    {
        ValueTask<TConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
    }

    public interface ITenantDbConnectionFactory<TConnection> where TConnection : DbConnection
    {
        ValueTask<TConnection> CreateOpenConnectionAsync(string tenantId, CancellationToken cancellationToken = default);
    }

    public abstract class TenantDbConnectionFactoryBase<TConnection> : ITenantDbConnectionFactory<TConnection>
        where TConnection : DbConnection
    {
        private readonly ITenantConnectionStringResolver _connectionStringResolver;
        private readonly string _connectionName;

        protected TenantDbConnectionFactoryBase(
            ITenantConnectionStringResolver connectionStringResolver,
            string connectionName = "Default")
        {
            _connectionStringResolver = connectionStringResolver;
            _connectionName = connectionName;
        }

        public async ValueTask<TConnection> CreateOpenConnectionAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            var connectionString = _connectionStringResolver.GetRequiredConnectionString(tenantId, _connectionName);
            var connection = CreateConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }

        protected abstract TConnection CreateConnection(string connectionString);
    }
}
