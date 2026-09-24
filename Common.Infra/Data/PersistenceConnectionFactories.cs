using System.Data;
using System.Data.Common;
using Common.MultiTenancy;
using Microsoft.Extensions.Configuration;

namespace Common.Data
{
    /// <summary>
    /// Fabrica de conexiones abiertas. Es la que usan <see cref="DapperSqlDbConnectionBase"/> y las
    /// migraciones de esquema.
    /// </summary>
    public interface IOpenDbConnectionFactory
    {
        /// <summary>
        /// Crea una conexion nueva y la abre. Quien llama es dueno de la conexion y debe liberarla.
        /// </summary>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>La conexion abierta.</returns>
        Task<IDbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Fabrica de conexiones abiertas a la base de un tenant dado.
    /// </summary>
    public interface ITenantOpenDbConnectionFactory
    {
        /// <summary>
        /// Crea una conexion nueva a la base del tenant y la abre. Quien llama debe liberarla.
        /// </summary>
        /// <param name="tenantId">Id del tenant.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>La conexion abierta.</returns>
        Task<IDbConnection> GetOpenConnectionAsync(string tenantId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Base de <see cref="IOpenDbConnectionFactory"/> con una cadena de conexion fija. La subclase solo
    /// crea la conexion; la base la abre (async si es <see cref="DbConnection"/>, sincrona si no).
    /// </summary>
    public abstract class DbConnectionFactory : IOpenDbConnectionFactory
    {
        /// <summary>
        /// Cadena de conexion con la que se crean las conexiones.
        /// </summary>
        protected readonly string ConnectionString;

        /// <summary>
        /// Crea la fabrica.
        /// </summary>
        /// <param name="connectionString">Cadena de conexion.</param>
        /// <exception cref="ArgumentException">Si la cadena es nula, vacia o solo espacios.</exception>
        protected DbConnectionFactory(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Connection string is required.", nameof(connectionString));
            }

            ConnectionString = connectionString;
        }

        /// <summary>
        /// Crea una conexion con <see cref="ConnectionString"/> y la abre.
        /// </summary>
        /// <param name="cancellationToken">Token de cancelacion (solo se respeta si la conexion es <see cref="DbConnection"/>).</param>
        /// <returns>La conexion abierta. Quien llama debe liberarla.</returns>
        public async Task<IDbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = CreateConnection(ConnectionString);
            await OpenConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }

        /// <summary>
        /// Crea la conexion (sin abrir) para la cadena dada.
        /// </summary>
        /// <param name="connectionString">Cadena de conexion.</param>
        /// <returns>La conexion sin abrir.</returns>
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

    /// <summary>
    /// <see cref="DbConnectionFactory"/> que toma la cadena de <c>ConnectionStrings</c> con el nombre
    /// del tipo <typeparamref name="TConnectionName"/>.
    /// </summary>
    /// <typeparam name="TConnectionName">
    /// Tipo marcador: su nombre simple (<c>typeof(T).Name</c>) es la clave en <c>ConnectionStrings</c>.
    /// </typeparam>
    public abstract class ConfigurationDbConnectionFactory<TConnectionName> : DbConnectionFactory
    {
        /// <summary>
        /// Crea la fabrica leyendo <c>ConnectionStrings:{nombre del tipo}</c>.
        /// </summary>
        /// <param name="configuration">Configuracion de la aplicacion.</param>
        /// <exception cref="InvalidOperationException">Si la cadena no existe.</exception>
        /// <exception cref="ArgumentException">Si la cadena existe pero esta vacia o solo tiene espacios.</exception>
        protected ConfigurationDbConnectionFactory(IConfiguration configuration)
            : base(configuration.GetConnectionString(typeof(TConnectionName).Name) ??
                   throw new InvalidOperationException($"Connection string '{typeof(TConnectionName).Name}' was not found."))
        {
        }
    }

    /// <summary>
    /// Base de <see cref="ITenantOpenDbConnectionFactory"/>: resuelve la cadena del tenant con
    /// <see cref="ITenantConnectionStringResolver"/> y abre la conexion que crea la subclase.
    /// </summary>
    public abstract class TenantDbConnectionFactory : ITenantOpenDbConnectionFactory
    {
        private readonly ITenantConnectionStringResolver _tenantConnectionStringResolver;
        private readonly string _connectionName;

        /// <summary>
        /// Crea la fabrica.
        /// </summary>
        /// <param name="tenantConnectionStringResolver">Resolvedor de cadenas de conexion por tenant.</param>
        /// <param name="connectionName">Nombre de la cadena de conexion. Por defecto <c>"Default"</c>.</param>
        protected TenantDbConnectionFactory(
            ITenantConnectionStringResolver tenantConnectionStringResolver,
            string connectionName = "Default")
        {
            _tenantConnectionStringResolver = tenantConnectionStringResolver;
            _connectionName = connectionName;
        }

        /// <summary>
        /// Resuelve la cadena del tenant, crea la conexion y la abre.
        /// </summary>
        /// <param name="tenantId">Id del tenant.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>La conexion abierta. Quien llama debe liberarla.</returns>
        /// <exception cref="InvalidOperationException">Si no hay cadena de conexion para el tenant.</exception>
        public async Task<IDbConnection> GetOpenConnectionAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            var connectionString = _tenantConnectionStringResolver.GetRequiredConnectionString(tenantId, _connectionName);
            var connection = CreateConnection(connectionString);
            await DbConnectionFactory.OpenConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }

        /// <summary>
        /// Crea la conexion (sin abrir) para la cadena dada.
        /// </summary>
        /// <param name="connectionString">Cadena de conexion.</param>
        /// <returns>La conexion sin abrir.</returns>
        protected abstract IDbConnection CreateConnection(string connectionString);
    }

    /// <summary>
    /// <see cref="IOpenDbConnectionFactory"/> que abre la conexion del tenant del contexto actual
    /// (<see cref="ITenantContextAccessor"/>), delegando en un <see cref="ITenantOpenDbConnectionFactory"/>.
    /// </summary>
    /// <remarks>
    /// Solo funciona donde hay tenant fijado: en una peticion que paso por la resolucion de tenant o
    /// dentro de <see cref="ITenantExecutionContextRunner"/>. Fuera de eso lanza.
    /// </remarks>
    public abstract class CurrentTenantDbConnectionFactory : IOpenDbConnectionFactory
    {
        private readonly ITenantContextAccessor _tenantContextAccessor;
        private readonly ITenantOpenDbConnectionFactory _tenantFactory;

        /// <summary>
        /// Crea la fabrica.
        /// </summary>
        /// <param name="tenantContextAccessor">Accesor del tenant actual.</param>
        /// <param name="tenantFactory">Fabrica que abre la conexion de un tenant dado.</param>
        protected CurrentTenantDbConnectionFactory(
            ITenantContextAccessor tenantContextAccessor,
            ITenantOpenDbConnectionFactory tenantFactory)
        {
            _tenantContextAccessor = tenantContextAccessor;
            _tenantFactory = tenantFactory;
        }

        /// <summary>
        /// Abre una conexion a la base del tenant actual.
        /// </summary>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>La conexion abierta. Quien llama debe liberarla.</returns>
        /// <exception cref="InvalidOperationException">Si no hay tenant en el contexto actual, o no hay cadena de conexion para el.</exception>
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
