# 06 - Multi-tenancy

Para aplicaciones donde una misma instancia atiende a varios clientes aislados (tenants). Todo esta en
`Common.MultiTenancy`, salvo el middleware (`Common.Web`) y la propagacion HTTP (`Common.Infra`).

Si tu aplicacion no es multi-tenant, sal de esta guia: nada de lo demas depende de ella.

## Registro

```csharp
using Common.MultiTenancy;
using Common.Web;

builder.Services.AddMultiTenancy(builder.Configuration);

var app = builder.Build();
app.UseCoreProblemDetails();
app.UseCorrelationId();
app.UseTenantResolution();     // antes de tus endpoints
app.MapControllers();
```

`AddMultiTenancy` registra, todos como singleton:

| Servicio | Para que |
|---|---|
| `ITenantContextAccessor` | Leer (o fijar) el tenant de la ejecucion actual |
| `ITenantResolver` | Sacar el tenant de un request |
| `ITenantConfigurationStore` | Consultar el catalogo de tenants de la configuracion |
| `ITenantConnectionStringResolver` | Cadena de conexion de un tenant |
| `ITenantExecutionContextRunner` | Ejecutar trabajo fuera de HTTP con un tenant fijado |

## Configuracion

```json
{
  "MultiTenancy": {
    "RequireTenant": true,
    "RejectUnknownTenants": true,
    "TenantHeaderName": "X-Tenant-Id",
    "ResolveFromHeader": true,
    "ResolveFromQueryString": false,
    "TenantQueryStringKey": "tenant",
    "ResolveFromSubdomain": true,
    "IgnoredSubdomains": [ "www", "api" ],
    "DefaultTenantId": null,
    "TenantResponseHeaderName": "X-Tenant-Id",
    "Tenants": {
      "acme": {
        "IsEnabled": true,
        "ConnectionStrings": { "Default": "Host=...;Database=acme;..." },
        "Settings": { "Region": "MX" }
      }
    }
  }
}
```

Valores por defecto: `RequireTenant=false`, `RejectUnknownTenants=true`, header `X-Tenant-Id`,
header y subdominio activos, query string apagado. `AddMultiTenancy` falla al arrancar si
`RequireTenant` esta activo y no hay ninguna forma de resolverlo.

## Como se resuelve el tenant

En este orden, se queda con el primero que no este vacio:

1. El header `TenantHeaderName`, si `ResolveFromHeader`.
2. El query string `TenantQueryStringKey`, si `ResolveFromQueryString`.
3. El subdominio, si `ResolveFromSubdomain`: el primer segmento de un host (un nombre, no una IP) de 3 o mas segmentos
   (`acme.midominio.com` -> `acme`) que no este en `IgnoredSubdomains`.
4. `DefaultTenantId`.

Y luego `UseTenantResolution` decide:

| Situacion | Respuesta |
|---|---|
| No se resolvio tenant y `RequireTenant` | **400** "Tenant not resolved" |
| No se resolvio tenant y no es obligatorio | Sigue sin tenant |
| El tenant esta en el catalogo con `IsEnabled: false` | **403** "Tenant disabled" |
| No esta en el catalogo, hay catalogo y `RejectUnknownTenants` | **403** "Unknown tenant" |
| Cualquier otro caso | Sigue, con el tenant fijado |

Con el tenant fijado: `ITenantContextAccessor` lo devuelve, la respuesta lleva el header
`TenantResponseHeaderName`, la traza se etiqueta con `tenant.id` y todo log de la peticion lleva
`TenantId`. Al terminar la peticion el contexto se limpia.

## Seguridad: el header no autentica a nadie

Resolver el tenant desde un header, un query string o un subdominio que manda el cliente **no
comprueba que el cliente pertenezca a ese tenant**. Cualquiera puede mandar `X-Tenant-Id: otro`.

`UseTenantResolution` sirve tal cual cuando el tenant es publico (una landing por subdominio) o cuando
quien llama es de confianza (otro servicio interno). Si tus usuarios se autentican, el tenant tiene
que salir de un **claim verificado del token**, no del request. En ese caso fija el contexto tu mismo,
despues de `UseAuthentication`:

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    var accessor = context.RequestServices.GetRequiredService<ITenantContextAccessor>();
    var tenantId = context.User.FindFirst("tenant_id")?.Value;
    if (!string.IsNullOrWhiteSpace(tenantId))
        accessor.Current = new TenantContext(tenantId);
    try { await next(); }
    finally { accessor.Current = null; }
});
```

Todo lo demas (logs, trazas, conexiones por tenant, propagacion) funciona igual, porque lee de
`ITenantContextAccessor`.

## Leer el tenant

```csharp
public sealed class MiHandler(ITenantContextAccessor tenant) : IRequestHandler<...>
{
    public Task<...> Handle(..., CancellationToken ct)
    {
        var tenantId = tenant.GetTenantId();   // null si la ejecucion no tiene tenant
        ...
    }
}
```

`TenantContextAccessor` guarda el contexto en un `AsyncLocal`: fluye con `await` dentro de la misma
peticion y no se filtra a otras.

## Fuera de HTTP: jobs y consumidores

Un job o un consumidor de cola no pasa por el middleware. Para que su trabajo tenga tenant:

```csharp
public sealed class CierreDiario(ITenantExecutionContextRunner runner, IServicioDeCierre servicio)
{
    public Task EjecutarAsync(string tenantId, CancellationToken ct) =>
        runner.RunAsync(tenantId, async token => await servicio.CerrarAsync(token), ct);
}
```

Dentro de `RunAsync`, `ITenantContextAccessor`, los logs (`TenantId`) y la traza (`tenant.id`) ven el
tenant como si fuera una peticion. Hay una sobrecarga `RunAsync<T>` que devuelve valor.

Se puede llamar desde un flujo que ya tiene tenant (por ejemplo, una peticion que hace algo a nombre
de otro tenant): dentro de `RunAsync` se ve el tenant pedido, y al volver el flujo conserva el suyo.

## Propagar el tenant a otros servicios

```csharp
builder.Services.AddHttpClient("inventario", c => c.BaseAddress = new Uri("https://inventario.interno"))
    .AddTenantPropagation();
```

Cada llamada de ese `HttpClient` lleva el tenant actual en el header `TenantHeaderName`, reemplazando
cualquier valor previo. Si no hay tenant, no manda nada.

## Datos por tenant

- `ITenantConfigurationStore.TryGetTenant(id, out var opciones)`: la entrada del catalogo (`IsEnabled`,
  `Settings`, `ConnectionStrings`).
- `ITenantConnectionStringResolver.GetRequiredConnectionString(id, "Default")`: la cadena del tenant.
  **Solo la del propio tenant**: si no la tiene, o no esta en el catalogo, lanza. Nunca usa
  `ConnectionStrings` global, para que un tenant mal configurado no acabe en la base de otro.
- Fabricas de conexion por tenant: [08 - Datos](08-datos-y-migraciones.md#una-base-por-tenant).

## A tener en cuenta

- `RejectUnknownTenants` solo rechaza si hay catalogo (`Tenants` no vacio). Sin catalogo, cualquier
  tenant pasa: define el catalogo, o valida el tenant tu mismo.
- Si tus tenants comparten una sola base (con una columna de tenant), no uses el resolvedor de
  cadenas por tenant: usa una fabrica fija ([guia 08](08-datos-y-migraciones.md#una-base-cadena-en-configuracion))
  y filtra por tenant en tus consultas.
