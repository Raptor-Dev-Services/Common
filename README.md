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
| AspNetCore.HealthChecks.SqlServer | 9.0.0 | Health checks para SQL Server. |
| AspNetCore.HealthChecks.Redis | 9.0.0 | Health checks para Redis. |
| OpenTelemetry.Extensions.Hosting | ver Common.csproj | Bootstrap OTel en host .NET. |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | ver Common.csproj | Exportacion OTLP (Grafana Tempo / OTEL collector). |
| OpenTelemetry.Exporter.Prometheus.AspNetCore | ver Common.csproj | Exportacion de metricas para scraping Prometheus. |
| OpenTelemetry.Instrumentation.AspNetCore | ver Common.csproj | Instrumentacion de requests ASP.NET Core. |
| OpenTelemetry.Instrumentation.Http | ver Common.csproj | Instrumentacion de HttpClient. |
| OpenTelemetry.Instrumentation.Runtime | ver Common.csproj | Metricas runtime (.NET GC, CPU, etc.). |

## Modulos

| Modulo | Namespace | Responsabilidad |
|---|---|---|
| Logging | Common.Logging | Registro de Serilog (Console/Debug/Seq). |
| Observability | Common.Observability | OpenTelemetry (traces + metrics) con OTLP. |
| Results | Common.Results | Resultado estandar (Result/Success/Failure). |
| Errors | Common.Errors | ErrorList utilitaria. |
| Exceptions | Common.Exceptions | Excepciones de dominio. |
| Messaging | Common.Messaging | Contratos IRequest/IResponse/IMediator/pipe. |
| Abstractions | Common.Abstractions | Interfaces base de interactores/presenters. |
| ViewModels | Common.ViewModels | ViewModels genericos. |
| MultiTenancy | Common.MultiTenancy | Resolucion de tenant, contexto actual y configuracion por tenant. |
| Data | Common.Data | Abstracciones de conexiones DB para implementaciones por proyecto. |

## Compatibilidad

| Componente | Version |
|---|---|
| Target Framework | `net10.0` |
| SDK recomendado | .NET SDK 10.x |

## Uso en otros proyectos

### 1) Referenciar el proyecto

En el proyecto WebApi:

```xml
<ItemGroup>
  <ProjectReference Include="..\\Common\\Common.csproj" />
</ItemGroup>
```

### 2) Registrar servicios principales

```csharp
builder.Services.AddLoggingServices(builder.Configuration);
builder.Services.AddObservability(builder.Configuration);
builder.Services.AddMultiTenancy(builder.Configuration);
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
    "TenantHeaderName": "X-Tenant-Id",
    "ResolveFromHeader": true,
    "ResolveFromSubdomain": true,
    "DefaultTenantId": "default",
    "Tenants": {
      "tenant-a": {
        "ConnectionStrings": {
          "Default": "Server=...;Database=TenantA;..."
        },
        "Settings": {
          "Region": "MX"
        }
      },
      "tenant-b": {
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

- `ITenantConnectionStringResolver` para resolver la cadena por tenant.
- `ITenantDbConnectionFactory<TConnection>` para abrir conexiones tipadas.
- `TenantDbConnectionFactoryBase<TConnection>` como base reutilizable.

Ejemplo para SQL Server en tu proyecto API:

```csharp
using Common.Data;
using Common.MultiTenancy;
using Microsoft.Data.SqlClient;

public sealed class SqlTenantConnectionFactory
    : TenantDbConnectionFactoryBase<SqlConnection>
{
    public SqlTenantConnectionFactory(ITenantConnectionStringResolver resolver)
        : base(resolver, "Default")
    {
    }

    protected override SqlConnection CreateConnection(string connectionString)
        => new(connectionString);
}
```

## Troubleshooting

| Sintoma | Causa probable | Solucion |
|---|---|---|
| `AddSerilog` no existe | Falta `Serilog.Extensions.Logging` | Verifica el PackageReference en `Common.csproj`. |
| `WriteTo.Seq` no existe | Falta `Serilog.Sinks.Seq` | Instala el paquete correspondiente. |
| No llegan traces a Grafana | Endpoint OTLP incorrecto | Verifica `Observability:OtlpEndpoint` y conectividad. |
| No aparecen metricas en Prometheus | Falta endpoint/scrape de metricas | Habilita endpoint Prometheus en la API y configuralo en Prometheus. |
| Logs sin tenant | Middleware multi-tenant no registrado | Asegura `app.UseTenantResolution()` antes de procesar endpoints. |

## Notas tecnicas

- OpenTelemetry exporta traces y metrics por OTLP al endpoint configurado.
- OpenTelemetry tambien expone metricas para Prometheus (`AddPrometheusExporter`).
- Serilog usa `CustomLogging:LogEventLevel` (por defecto Verbose); en `Development` fuerza al menos `Debug`.
- El middleware de tenant agrega `tenant.id` al `Activity` actual y `TenantId` al scope de logs por request.
