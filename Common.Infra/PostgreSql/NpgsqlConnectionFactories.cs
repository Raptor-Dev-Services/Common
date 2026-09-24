using System.Data;
using Common.Data;
using Common.MultiTenancy;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Common.PostgreSql
{
    /// <summary>
    /// <see cref="ConfigurationDbConnectionFactory{TConnectionName}"/> que crea conexiones <see cref="NpgsqlConnection"/>.
    /// </summary>
    /// <typeparam name="TConnectionName">
    /// Tipo marcador: su nombre simple es la clave en <c>ConnectionStrings</c>.
    /// </typeparam>
    public abstract class ConfigurationNpgsqlConnectionFactory<TConnectionName>
        : ConfigurationDbConnectionFactory<TConnectionName>
    {
        /// <summary>
        /// Crea la fabrica leyendo <c>ConnectionStrings:{nombre del tipo}</c>.
        /// </summary>
        /// <param name="configuration">Configuracion de la aplicacion.</param>
        /// <exception cref="InvalidOperationException">Si la cadena no existe.</exception>
        protected ConfigurationNpgsqlConnectionFactory(IConfiguration configuration)
            : base(configuration)
        {
        }

        /// <summary>
        /// Crea una <see cref="NpgsqlConnection"/> sin abrir.
        /// </summary>
        /// <param name="connectionString">Cadena de conexion.</param>
        /// <returns>La conexion sin abrir.</returns>
        protected override IDbConnection CreateConnection(string connectionString)
            => new NpgsqlConnection(connectionString);
    }

    /// <summary>
    /// <see cref="TenantDbConnectionFactory"/> que crea conexiones <see cref="NpgsqlConnection"/>.
    /// </summary>
    public abstract class TenantNpgsqlConnectionFactory : TenantDbConnectionFactory
    {
        /// <summary>
        /// Crea la fabrica.
        /// </summary>
        /// <param name="tenantConnectionStringResolver">Resolvedor de cadenas de conexion por tenant.</param>
        /// <param name="connectionName">Nombre de la cadena de conexion. Por defecto <c>"Default"</c>.</param>
        protected TenantNpgsqlConnectionFactory(
            ITenantConnectionStringResolver tenantConnectionStringResolver,
            string connectionName = "Default")
            : base(tenantConnectionStringResolver, connectionName)
        {
        }

        /// <summary>
        /// Crea una <see cref="NpgsqlConnection"/> sin abrir.
        /// </summary>
        /// <param name="connectionString">Cadena de conexion.</param>
        /// <returns>La conexion sin abrir.</returns>
        protected override IDbConnection CreateConnection(string connectionString)
            => new NpgsqlConnection(connectionString);
    }

    /// <summary>
    /// <see cref="CurrentTenantDbConnectionFactory"/> para PostgreSQL. No agrega comportamiento: el tipo
    /// de conexion lo decide el <see cref="ITenantOpenDbConnectionFactory"/> que recibe.
    /// </summary>
    public abstract class CurrentTenantNpgsqlConnectionFactory : CurrentTenantDbConnectionFactory
    {
        /// <summary>
        /// Crea la fabrica.
        /// </summary>
        /// <param name="tenantContextAccessor">Accesor del tenant actual.</param>
        /// <param name="tenantFactory">Fabrica que abre la conexion de un tenant dado.</param>
        protected CurrentTenantNpgsqlConnectionFactory(
            ITenantContextAccessor tenantContextAccessor,
            ITenantOpenDbConnectionFactory tenantFactory)
            : base(tenantContextAccessor, tenantFactory)
        {
        }
    }
}
