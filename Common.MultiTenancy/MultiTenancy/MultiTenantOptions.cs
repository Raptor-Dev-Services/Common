namespace Common.MultiTenancy
{
    /// <summary>
    /// Configuracion de multi-tenancy. Se lee de la seccion <see cref="SectionName"/> con
    /// <c>AddMultiTenancy</c>.
    /// </summary>
    public sealed class MultiTenantOptions
    {
        /// <summary>
        /// Nombre de la seccion de configuracion: <c>"MultiTenancy"</c>.
        /// </summary>
        public const string SectionName = "MultiTenancy";

        /// <summary>
        /// Si es <c>true</c>, una peticion sin tenant resuelto recibe 400. Por defecto <c>false</c>:
        /// la peticion sigue sin tenant.
        /// </summary>
        public bool RequireTenant { get; set; }

        /// <summary>
        /// Si es <c>true</c> (por defecto), un tenant que no esta en <see cref="Tenants"/> recibe 403.
        /// Solo aplica cuando <see cref="Tenants"/> tiene al menos una entrada: con la lista vacia
        /// se acepta cualquier tenant.
        /// </summary>
        public bool RejectUnknownTenants { get; set; } = true;

        /// <summary>
        /// Tenant que se usa cuando ninguna estrategia lo resuelve. Por defecto <c>null</c>.
        /// </summary>
        public string? DefaultTenantId { get; set; }

        /// <summary>
        /// Header de la peticion del que se lee el tenant, y que se escribe al propagarlo en
        /// llamadas HTTP salientes. Por defecto <c>"X-Tenant-Id"</c>.
        /// </summary>
        public string TenantHeaderName { get; set; } = "X-Tenant-Id";

        /// <summary>
        /// Clave del query string de la que se lee el tenant. Por defecto <c>"tenant"</c>.
        /// </summary>
        public string TenantQueryStringKey { get; set; } = "tenant";

        /// <summary>
        /// Resolver el tenant desde <see cref="TenantHeaderName"/>. Por defecto <c>true</c>.
        /// </summary>
        public bool ResolveFromHeader { get; set; } = true;

        /// <summary>
        /// Resolver el tenant desde <see cref="TenantQueryStringKey"/>. Por defecto <c>false</c>.
        /// </summary>
        public bool ResolveFromQueryString { get; set; }

        /// <summary>
        /// Resolver el tenant desde el primer segmento del host cuando el host es un nombre (no una
        /// direccion IP), tiene 3 o mas segmentos y ese segmento no esta en <see cref="IgnoredSubdomains"/>.
        /// Por defecto <c>true</c>.
        /// </summary>
        public bool ResolveFromSubdomain { get; set; } = true;

        /// <summary>
        /// Header de la respuesta en el que se devuelve el tenant resuelto. Por defecto <c>"X-Tenant-Id"</c>.
        /// </summary>
        public string TenantResponseHeaderName { get; set; } = "X-Tenant-Id";

        /// <summary>
        /// Subdominios que no se toman como tenant (sin distinguir mayusculas). Por defecto
        /// <c>www</c> y <c>api</c>.
        /// </summary>
        public HashSet<string> IgnoredSubdomains { get; set; } = new(StringComparer.OrdinalIgnoreCase) { "www", "api" };

        /// <summary>
        /// Tenants conocidos, por id (sin distinguir mayusculas).
        /// </summary>
        public Dictionary<string, TenantOptions> Tenants { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Configuracion de un tenant concreto.
    /// </summary>
    public sealed class TenantOptions
    {
        /// <summary>
        /// Si es <c>false</c>, las peticiones de este tenant reciben 403. Por defecto <c>true</c>.
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Ajustes libres del tenant, por nombre (sin distinguir mayusculas). La libreria no los lee.
        /// </summary>
        public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Cadenas de conexion propias del tenant, por nombre (sin distinguir mayusculas). Si falta
        /// una, pedirla falla: no se usa la de <c>ConnectionStrings</c> global.
        /// </summary>
        public Dictionary<string, string> ConnectionStrings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
