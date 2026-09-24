# 09 - HTTP, salud y opciones

Tres utilidades pequenas de `Common.Infra`.

## Resiliencia de `HttpClient`

```csharp
using Common.Http;

builder.Services.AddHttpClient("pagos", c => c.BaseAddress = new Uri("https://pagos.interno"))
    .AddCoreResilience();
```

Aplica el manejador estandar de resiliencia de .NET (`Microsoft.Extensions.Http.Resilience`):
reintentos con espera exponencial, circuit breaker y timeouts por intento y totales, con sus valores
por defecto.

Para ajustarlos desde configuracion, pasa la seccion:

```csharp
builder.Services.AddHttpClient("pagos")
    .AddCoreResilience(builder.Configuration.GetSection("Resiliencia:Pagos"));
```

```json
{
  "Resiliencia": {
    "Pagos": {
      "Retry": { "MaxRetryAttempts": 2 },
      "TotalRequestTimeout": { "Timeout": "00:00:10" }
    }
  }
}
```

Se combina con la propagacion del tenant ([guia 06](06-multi-tenancy.md#propagar-el-tenant-a-otros-servicios)):
`.AddCoreResilience().AddTenantPropagation()`.

**Ojo con los reintentos en escrituras:** un `POST` que se reintenta puede ejecutarse dos veces si el
primero llego pero la respuesta se perdio. Reintenta solo operaciones idempotentes, o manda una
clave de idempotencia que el otro lado respete.

## Health checks

```csharp
using Common.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

builder.Services.AddCoreHealthChecks()                         // "self": siempre sano
    .AddPostgreSqlHealthCheck(connectionString, tags: ["ready"])
    .AddRedisHealthCheck(redisConnectionString, tags: ["ready"]);

var app = builder.Build();

// Vivo: el proceso responde. No toca dependencias.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = r => r.Name == "self" });

// Listo: puede atender trafico. Revisa las dependencias.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") });
```

Por que dos endpoints: el orquestador reinicia un contenedor cuyo `live` falla, y le deja de mandar
trafico si falla `ready`. Si `live` revisara la base, una base lenta haria reiniciar aplicaciones
sanas.

Cada check acepta `name`, `failureStatus`, `tags` y `timeout`. `AddSqlHealthCheck` es un alias de
`AddPostgreSqlHealthCheck` con nombre por defecto `sql`.

La respuesta por defecto es texto plano (`Healthy` / `Unhealthy`), sin detalles: es lo correcto para
un endpoint anonimo. Si quieres JSON, pasa tu propio `ResponseWriter` en `HealthCheckOptions`, y no
incluyas los mensajes de excepcion: suelen llevar nombres de host y cadenas de conexion.

## Opciones validadas al arrancar

```csharp
using System.ComponentModel.DataAnnotations;
using Common.Options;

public sealed class PagosOptions
{
    [Required, Url]
    public string BaseUrl { get; set; } = string.Empty;

    [Range(1, 60)]
    public int TimeoutSegundos { get; set; } = 10;
}

builder.Services.AddValidatedOptions<PagosOptions>(builder.Configuration.GetSection("Pagos"));
```

Enlaza la seccion, valida las data annotations y **falla al arrancar** si algo no cumple, en lugar de
fallar con el primer usuario que toque esa parte. Se lee como cualquier opcion:
`IOptions<PagosOptions>`.

`AddValidatedOptions` devuelve el `OptionsBuilder`, asi que puedes encadenar validaciones propias:

```csharp
builder.Services.AddValidatedOptions<PagosOptions>(builder.Configuration.GetSection("Pagos"))
    .Validate(o => o.BaseUrl.StartsWith("https://"), "Pagos:BaseUrl tiene que ser https.");
```
