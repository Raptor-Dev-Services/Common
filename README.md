# GTM-Suite Common

Libreria base reutilizable para WebApi (.NET 10) con logging (Serilog + Seq) y observabilidad (OpenTelemetry). Incluye resultados estandar, errores, excepciones y contratos de mensajeria.

## Paquetes

| Paquete | Version | Proposito tecnico |
|---|---|---|
| Microsoft.Extensions.Configuration | 10.0.2 | Lectura y binding de configuracion (appsettings, env vars, etc.). |
| Microsoft.Extensions.DependencyInjection | 10.0.2 | Registro y resolucion de dependencias. |
| Microsoft.Extensions.Logging | 10.0.2 | Abstracciones de logging. |
| Microsoft.Extensions.Http.Resilience | 10.2.0 | Resiliencia para llamadas HTTP salientes. |
| Serilog | 4.3.0 | Logger estructurado. |
| AspNetCore.HealthChecks.NpgSql | 9.0.0 | Health checks para PostgreSQL. |
| AspNetCore.HealthChecks.Redis | 9.0.0 | Health checks para Redis. |
| OpenTelemetry.Extensions.Hosting | ver Common.csproj | Bootstrap OTel en host .NET. |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | ver Common.csproj | Exportacion OTLP (Grafana Tempo / OTEL collector). |
| OpenTelemetry.Exporter.Prometheus.AspNetCore | ver Common.csproj | Exportacion de metricas para scraping Prometheus. |
| OpenTelemetry.Instrumentation.AspNetCore | ver Common.csproj | Instrumentacion de requests ASP.NET Core. |
| OpenTelemetry.Instrumentation.Http | ver Common.csproj | Instrumentacion de HttpClient. |
| OpenTelemetry.Instrumentation.Runtime | ver Common.csproj | Metricas runtime (.NET GC, CPU, etc.). |

## Arquitectura modular (v2.0.0)

Desde **v2.0.0**, `Common` se reparte en **5 sub-librerias + una facade**. Los **namespaces NO cambian**
(`Common.Results`, `Common.Messaging`, ...): solo cambia el ensamblado que los contiene, para que cada
capa de un monolito modular referencie unicamente lo que necesita. Quien hoy referencia `Common.csproj`
(la facade) sigue compilando sin tocar un solo `using`.

| Proyecto (assembly) | Namespaces que contiene | Depende de |
|---|---|---|
| `Common.Contracts` | `Common.Results`, `Common.Errors`, `Common.Exceptions`, `Common.Messaging` (solo `INotification`/`IResponse`) | — |
| `Common.Messaging` | `Common.Messaging` (contratos mediator), `Common.Abstractions` | Contracts |
| `Common.MultiTenancy` | `Common.MultiTenancy` | ASP.NET + Serilog |
| `Common.Infra` | `Common.Logging`, `Common.Observability`, `Common.HealthChecks`, `Common.Http`, `Common.Options`, `Common.Messaging` (impl `Mediator`/pipeline/`AddMediator`), `Common.Data`, `Common.PostgreSql` | Contracts + Messaging + MultiTenancy + NuGets |
| `Common.Web` | `Common.ViewModels`, `Common.Web` | Contracts + MultiTenancy + ASP.NET |
| `Common` (facade) | re-exporta los 5 anteriores | los 5 |

### Referencias por capa

| Capa | Referencia |
|---|---|
| Domain | `Common.Contracts` |
| Application | `Common.Contracts` + `Common.Messaging` (+ `Common.MultiTenancy` si lee tenant) |
| Infrastructure | `Common.Contracts` + `Common.Messaging` + `Common.MultiTenancy` + `Common.Infra` |
| Presentation | `Common.Contracts` + `Common.Messaging` + `Common.Web` |
| Host | `Common.Infra` + `Common.Web` |

### Modulos (responsabilidad)

| Modulo | Namespace | Responsabilidad |
|---|---|---|
| Logging | Common.Logging | Registro de Serilog (Console/Debug/Seq). |
| Observability | Common.Observability | OpenTelemetry (traces + metrics) con OTLP. |
| Results | Common.Results | Resultado estandar (Result/Success/Failure). |
| Errors | Common.Errors | ErrorList utilitaria. |
| Exceptions | Common.Exceptions | Excepciones de dominio. |
| Messaging | Common.Messaging | Contratos IRequest/IResponse/IMediator/pipe + impl Mediator. |
| Abstractions | Common.Abstractions | Interfaces base de interactores/presenters. |
| ViewModels | Common.ViewModels | ViewModels genericos. |
| MultiTenancy | Common.MultiTenancy | Resolucion de tenant, contexto actual y configuracion por tenant. |
| Data | Common.Data | Abstracciones de conexiones DB para implementaciones por proyecto. |
| PostgreSql | Common.PostgreSql | Factorias Npgsql, health checks y migraciones por scripts al arranque. |

## Compatibilidad

| Componente | Version |
|---|---|
| Target Framework | `net10.0` |
| SDK recomendado | .NET SDK 10.x |

## Uso en otros proyectos

### 1) Referenciar el proyecto

Opcion simple (facade, trae todo) — tipica para un Host/WebApi:

```xml
<ItemGroup>
  <ProjectReference Include="..\\Common\\Common.csproj" />
</ItemGroup>
```

Opcion monolito modular (referencia por capa) — ver "Referencias por capa" arriba. Ej. capa Host:

```xml
<ItemGroup>
  <ProjectReference Include="..\\Common.Infra\\Common.Infra.csproj" />
  <ProjectReference Include="..\\Common.Web\\Common.Web.csproj" />
</ItemGroup>
```

### 2) Registrar servicios principales

```csharp
builder.Services.AddLoggingServices(builder.Configuration);
builder.Services.AddObservability(builder.Configuration);
builder.Services.AddMultiTenancy(builder.Configuration);
builder.Services.AddHttpClient("core").AddCoreResilience().AddTenantPropagation();
```

### 3) Registrar middlewares base

```csharp
app.UseTenantResolution();
app.UseCorrelationId();
app.UseCoreProblemDetails();
```

## Configuracion esperada (appsettings.json)

```json
{
  "CustomLogging": {
    "Project": "GTM-Suite",
    "SeqUri": "http://localhost:5341",
    "LogEventLevel": "Information",
    "Application": "MiWebApi",
    "Version": "1.0.0"
  },
  "Observability": {
    "ServiceName": "MiWebApi",
    "ServiceVersion": "1.0.0",
    "OtlpEndpoint": "http://localhost:4317"
  },
  "MultiTenancy": {
    "RequireTenant": true,
    "RejectUnknownTenants": true,
    "TenantHeaderName": "X-Tenant-Id",
    "ResolveFromHeader": true,
    "ResolveFromSubdomain": true,
    "DefaultTenantId": "default",
    "Tenants": {
      "tenant-a": {
        "IsEnabled": true,
        "ConnectionStrings": {
          "Default": "Server=...;Database=TenantA;..."
        },
        "Settings": {
          "Region": "MX"
        }
      },
      "tenant-b": {
        "IsEnabled": true,
        "ConnectionStrings": {
          "Default": "Server=...;Database=TenantB;..."
        },
        "Settings": {
          "Region": "US"
        }
      }
    }
  }
}
```

## Conexion a base de datos por tenant

`Common` define contratos para que cada proyecto implemente su proveedor:

- `DbConnectionFactory` como base simple para conexiones por cadena fija.
- `ConfigurationDbConnectionFactory<TConnectionName>` para resolver `ConnectionStrings:{typeof(TConnectionName).Name}`.
- `TenantDbConnectionFactory` para resolver conexión por tenant.
- `CurrentTenantDbConnectionFactory` para usar el tenant actual del request.
- `ITenantConnectionStringResolver` para obtener cadenas por tenant.
- `IDapperSqlDbConnection` + `DapperSqlDbConnectionBase` para operaciones Dapper con logging y medición de tiempo.

Ejemplo con el mismo estilo de `Persistence/Connections`:

```csharp
using Common.PostgreSql;
using Microsoft.Extensions.Configuration;

public sealed class MainDbConnection
{
}

public sealed class ConfigurationMainDbConnectionFactory
    : ConfigurationNpgsqlConnectionFactory<MainDbConnection>
{
    public ConfigurationMainDbConnectionFactory(IConfiguration configuration)
        : base(configuration)
    {
    }
}
```

Ejemplo para jobs/background:

```csharp
await tenantExecutionContextRunner.RunAsync("tenant-a", async ct =>
{
    // Todo lo que se ejecute aqui conserva TenantId en contexto/logs/traces.
    await service.RunAsync(ct);
}, cancellationToken);
```

## Troubleshooting

| Sintoma | Causa probable | Solucion |
|---|---|---|
| `AddSerilog` no existe | Falta `Serilog.Extensions.Logging` | Verifica el PackageReference en `Common.csproj`. |
| `WriteTo.Seq` no existe | Falta `Serilog.Sinks.Seq` | Instala el paquete correspondiente. |
| No llegan traces a Grafana | Endpoint OTLP incorrecto | Verifica `Observability:OtlpEndpoint` y conectividad. |
| No aparecen metricas en Prometheus | Falta endpoint/scrape de metricas | Habilita endpoint Prometheus en la API y configuralo en Prometheus. |
| Logs sin tenant | Middleware multi-tenant no registrado | Asegura `app.UseTenantResolution()` antes de procesar endpoints. |
| Llamadas HTTP salientes sin tenant | Falta propagacion en `HttpClient` | Usa `.AddTenantPropagation()` al registrar clientes HTTP. |
| Jobs/consumers sin tenant en logs | No se setea contexto fuera de HTTP | Ejecuta procesos con `ITenantExecutionContextRunner`. |

## Notas tecnicas

- OpenTelemetry exporta traces y metrics por OTLP al endpoint configurado.
- OpenTelemetry tambien expone metricas para Prometheus (`AddPrometheusExporter`).
- Serilog usa `CustomLogging:LogEventLevel` (por defecto Verbose); en `Development` fuerza al menos `Debug`.
- El middleware de tenant agrega `tenant.id` al `Activity` actual y `TenantId` al scope de logs por request.
- `RejectUnknownTenants=true` rechaza tenants no registrados cuando existe catalogo de tenants en configuracion.
