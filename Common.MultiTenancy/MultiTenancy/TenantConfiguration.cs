using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Common.MultiTenancy
{
    /// <summary>
    /// Consulta la configuracion de los tenants conocidos.
    /// </summary>
    public interface ITenantConfigurationStore
    {
        /// <summary>
        /// Busca la configuracion de un tenant.
        /// </summary>
        /// <param name="tenantId">Id del tenant.</param>
        /// <param name="tenantOptions">La configuracion encontrada; sin valor util si devuelve <c>false</c>.</param>
        /// <returns><c>true</c> si el tenant esta configurado.</returns>
        bool TryGetTenant(string tenantId, out TenantOptions tenantOptions);
    }

    /// <summary>
    /// Resuelve la cadena de conexion que le toca a un tenant.
    /// </summary>
    public interface ITenantConnectionStringResolver
    {
        /// <summary>
        /// Busca la cadena de conexion <paramref name="name"/> del tenant.
        /// </summary>
        /// <param name="tenantId">Id del tenant.</param>
        /// <param name="name">Nombre de la cadena de conexion.</param>
        /// <param name="connectionString">La cadena encontrada, o vacia si devuelve <c>false</c>.</param>
        /// <returns><c>true</c> si se encontro una cadena.</returns>
        bool TryGetConnectionString(string tenantId, string name, out string connectionString);

        /// <summary>
        /// Devuelve la cadena de conexion <paramref name="name"/> del tenant, o lanza si no existe.
        /// </summary>
        /// <param name="tenantId">Id del tenant.</param>
        /// <param name="name">Nombre de la cadena de conexion. Por defecto <c>"Default"</c>.</param>
        /// <returns>La cadena de conexion.</returns>
        /// <exception cref="InvalidOperationException">Si no se encuentra la cadena.</exception>
        string GetRequiredConnectionString(string tenantId, string name = "Default");
    }

    /// <summary>
    /// <see cref="ITenantConfigurationStore"/> que lee <see cref="MultiTenantOptions.Tenants"/>.
    /// Usa <see cref="IOptionsMonitor{TOptions}"/>, asi que ve los cambios de configuracion en caliente.
    /// </summary>
    public sealed class TenantConfigurationStore : ITenantConfigurationStore
    {
        private readonly IOptionsMonitor<MultiTenantOptions> _options;

        /// <summary>
        /// Crea el almacen.
        /// </summary>
        /// <param name="options">Opciones de multi-tenancy.</param>
        public TenantConfigurationStore(IOptionsMonitor<MultiTenantOptions> options)
        {
            _options = options;
        }

        /// <summary>
        /// Busca el tenant en <see cref="MultiTenantOptions.Tenants"/> (sin distinguir mayusculas).
        /// Un id vacio o solo con espacios devuelve <c>false</c>.
        /// </summary>
        /// <param name="tenantId">Id del tenant.</param>
        /// <param name="tenantOptions">La configuracion encontrada; <c>null</c> si devuelve <c>false</c>.</param>
        /// <returns><c>true</c> si el tenant esta configurado.</returns>
        public bool TryGetTenant(string tenantId, out TenantOptions tenantOptions)
        {
            tenantOptions = default!;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return false;
            }

            return _options.CurrentValue.Tenants.TryGetValue(tenantId, out tenantOptions!);
        }
    }

    /// <summary>
    /// <see cref="ITenantConnectionStringResolver"/> que devuelve solo la cadena configurada para el
    /// propio tenant en <c>MultiTenancy:Tenants:{id}:ConnectionStrings</c>.
    /// </summary>
    /// <remarks>
    /// Nunca cae a <c>ConnectionStrings</c> global. Hasta la v2.1.2 lo hacia, y un tenant sin cadena
    /// propia, o fuera del catalogo, recibia la base compartida o la de otro: trabajaba sobre datos
    /// ajenos sin ningun error. Ahora eso falla.
    /// </remarks>
    public sealed class TenantConnectionStringResolver : ITenantConnectionStringResolver
    {
        private readonly ITenantConfigurationStore _tenantConfigurationStore;

        /// <summary>
        /// Crea el resolvedor.
        /// </summary>
        /// <param name="tenantConfigurationStore">Almacen de configuracion de tenants.</param>
        /// <param name="configuration">
        /// Ya no se usa: se conserva para no romper a quien construye el resolvedor a mano. Antes se
        /// leia de aqui la cadena global de respaldo, que se quito.
        /// </param>
        public TenantConnectionStringResolver(
            ITenantConfigurationStore tenantConfigurationStore,
            IConfiguration configuration)
        {
            _tenantConfigurationStore = tenantConfigurationStore;
            _ = configuration;
        }

        /// <summary>
        /// Busca la cadena <paramref name="name"/> en <see cref="TenantOptions.ConnectionStrings"/> del
        /// tenant.
        /// </summary>
        /// <remarks>
        /// Un <paramref name="name"/> vacio se trata como <c>"Default"</c>. Devuelve <c>false</c> si el
        /// tenant esta vacio, no esta en el catalogo o no tiene esa cadena: nunca usa la global.
        /// </remarks>
        /// <param name="tenantId">Id del tenant.</param>
        /// <param name="name">Nombre de la cadena de conexion.</param>
        /// <param name="connectionString">La cadena encontrada, o vacia si devuelve <c>false</c>.</param>
        /// <returns><c>true</c> si se encontro una cadena.</returns>
        public bool TryGetConnectionString(string tenantId, string name, out string connectionString)
        {
            connectionString = string.Empty;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return false;
            }

            var normalizedName = string.IsNullOrWhiteSpace(name) ? "Default" : name;
            if (_tenantConfigurationStore.TryGetTenant(tenantId, out var tenantOptions) &&
                tenantOptions.ConnectionStrings.TryGetValue(normalizedName, out var tenantConnectionString) &&
                !string.IsNullOrWhiteSpace(tenantConnectionString))
            {
                connectionString = tenantConnectionString;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Igual que <see cref="TryGetConnectionString"/>, pero lanza si no encuentra la cadena.
        /// </summary>
        /// <param name="tenantId">Id del tenant.</param>
        /// <param name="name">Nombre de la cadena de conexion. Por defecto <c>"Default"</c>.</param>
        /// <returns>La cadena de conexion.</returns>
        /// <exception cref="InvalidOperationException">
        /// Si el tenant no tiene esa cadena, no esta en el catalogo, o <paramref name="tenantId"/> esta vacio.
        /// </exception>
        public string GetRequiredConnectionString(string tenantId, string name = "Default")
        {
            if (TryGetConnectionString(tenantId, name, out var connectionString))
            {
                return connectionString;
            }

            throw new InvalidOperationException(
                $"Connection string '{name}' was not found for tenant '{tenantId}'.");
        }
    }
}
