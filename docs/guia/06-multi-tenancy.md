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
3. El subdominio, si `ResolveFromSubdomain`: el primer segmento de un host de 3 o mas segmentos
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

**Llamalo solo desde un flujo sin tenant** (un job, un consumidor, un `BackgroundService`). Si lo
llamas desde algo que ya tiene tenant, como una peticion HTTP, **esa peticion pierde su tenant** al
volver de `RunAsync` (ver problemas conocidos al final).

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
  **Si el tenant no la tiene, devuelve la cadena global** `ConnectionStrings:Default`, y solo lanza si
  tampoco existe esa (ver problemas conocidos).
- Fabricas de conexion por tenant: [08 - Datos](08-datos-y-migraciones.md#una-base-por-tenant).

## Problemas conocidos

Comprobados en la version actual; estan registrados en el [SRS](../srs.md) como requisitos que no se
cumplen. Mientras se corrigen, esto es lo que hay que saber:

| Problema | Consecuencia | Como evitarlo |
|---|---|---|
| Un tenant sin cadena propia, **o que no esta en el catalogo**, recibe la cadena global `ConnectionStrings:{nombre}` | Un tenant mal configurado lee y escribe en la base compartida, no en la suya | Si usas una base por tenant, **no definas** la cadena global con el mismo nombre: asi un tenant sin cadena falla en vez de caer a otra base. Y manten `RejectUnknownTenants` con catalogo. |
| `RejectUnknownTenants` solo rechaza si hay catalogo (`Tenants` no vacio) | Sin catalogo, cualquier tenant pasa | Define el catalogo, o valida el tenant tu mismo |
| Un host que es una IP (`192.168.1.10`) resuelve el tenant `"192"` con `ResolveFromSubdomain` | Llamadas por IP (health checks internos, pruebas) entran con un tenant inventado | Apaga `ResolveFromSubdomain` si no lo usas, o manten catalogo con `RejectUnknownTenants` para que se rechace |
| `RunAsync` dentro de un flujo que ya tiene tenant le borra el tenant al volver | Despues de la llamada, la peticion sigue sin tenant | Llama `RunAsync` solo desde flujos sin tenant |
