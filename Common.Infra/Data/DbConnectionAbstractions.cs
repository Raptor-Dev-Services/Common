using System.Data.Common;
using Common.MultiTenancy;

namespace Common.Data
{
    /// <summary>
    /// Fabrica de conexiones abiertas de un tipo concreto de <see cref="DbConnection"/>.
    /// </summary>
    /// <remarks>La libreria no trae ninguna implementacion de esta interfaz.</remarks>
    /// <typeparam name="TConnection">Tipo de conexion.</typeparam>
    public interface IDbConnectionFactory<TConnection> where TConnection : DbConnection
    {
        /// <summary>
        /// Crea una conexion nueva y la abre. Quien llama es dueno de la conexion y debe liberarla.
        /// </summary>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>La conexion abierta.</returns>
        ValueTask<TConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Fabrica de conexiones abiertas de un tipo concreto de <see cref="DbConnection"/> para un tenant dado.
    /// </summary>
    /// <typeparam name="TConnection">Tipo de conexion.</typeparam>
    public interface ITenantDbConnectionFactory<TConnection> where TConnection : DbConnection
    {
        /// <summary>
        /// Crea una conexion nueva a la base del tenant y la abre. Quien llama es dueno de la conexion y debe liberarla.
        /// </summary>
        /// <param name="tenantId">Id del tenant.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>La conexion abierta.</returns>
        ValueTask<TConnection> CreateOpenConnectionAsync(string tenantId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Base para <see cref="ITenantDbConnectionFactory{TConnection}"/>: resuelve la cadena de conexion
    /// del tenant con <see cref="ITenantConnectionStringResolver"/> y abre la conexion que crea la subclase.
    /// </summary>
    /// <typeparam name="TConnection">Tipo de conexion.</typeparam>
    public abstract class TenantDbConnectionFactoryBase<TConnection> : ITenantDbConnectionFactory<TConnection>
        where TConnection : DbConnection
    {
        private readonly ITenantConnectionStringResolver _connectionStringResolver;
        private readonly string _connectionName;

        /// <summary>
        /// Crea la fabrica.
        /// </summary>
        /// <param name="connectionStringResolver">Resolvedor de cadenas de conexion por tenant.</param>
        /// <param name="connectionName">Nombre de la cadena de conexion. Por defecto <c>"Default"</c>.</param>
        protected TenantDbConnectionFactoryBase(
            ITenantConnectionStringResolver connectionStringResolver,
            string connectionName = "Default")
        {
            _connectionStringResolver = connectionStringResolver;
            _connectionName = connectionName;
        }

        /// <summary>
        /// Resuelve la cadena de conexion del tenant, crea la conexion y la abre.
        /// </summary>
        /// <param name="tenantId">Id del tenant.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>La conexion abierta. Quien llama debe liberarla.</returns>
        /// <exception cref="InvalidOperationException">Si no hay cadena de conexion para el tenant.</exception>
        public async ValueTask<TConnection> CreateOpenConnectionAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            var connectionString = _connectionStringResolver.GetRequiredConnectionString(tenantId, _connectionName);
            var connection = CreateConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }

        /// <summary>
        /// Crea la conexion (sin abrir) para la cadena dada.
        /// </summary>
        /// <param name="connectionString">Cadena de conexion resuelta.</param>
        /// <returns>La conexion sin abrir.</returns>
        protected abstract TConnection CreateConnection(string connectionString);
    }
}
