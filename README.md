# GTM-Suite Common

Libreria base reutilizable para WebApi (.NET 10) con logging (Serilog + Seq) y observabilidad (OpenTelemetry). Incluye resultados estandar, errores, excepciones y contratos de mensajeria.

## Documentacion

| Documento | Para que |
|---|---|
| [`docs/srs.md`](docs/srs.md) | Requisitos: que tiene que hacer la libreria y como se verifica cada cosa. |
| [`docs/adr/`](docs/adr/README.md) | Decisiones de arquitectura: por que es como es y que se descarto. |
| [`REFACTORING-PLAN.md`](REFACTORING-PLAN.md) | Como se ejecuto la division en sub-librerias (v2.0.0). |
| [`.claude/rules/stable-dependencies.md`](.claude/rules/stable-dependencies.md) | La regla de dependencias estables y su puerta. |

## Licencia

MIT. Ver [`LICENSE`](LICENSE). Aplica igual al submodulo y a los paquetes de nuget.org.

## Distribucion

El mismo codigo llega por dos canales ([ADR-0008](docs/adr/0008-consumo-como-submodulo-fijado-a-commit.md)):

| Canal | Para quien | Como |
|---|---|---|
| Submodulo de git | Productos de Raptor Dev Services | `git submodule add https://github.com/Raptor-Dev-Services/Common`, fijado a un tag. |
| nuget.org | Cualquiera | Paquetes `Raptor.Common.*`, publicados desde el espejo [`Raptor057/ApiCommon`](https://github.com/Raptor057/ApiCommon). |

En nuget.org hay un paquete por ensamblado con el mismo nombre y el prefijo `Raptor.`
(`Raptor.Common.Contracts`, `Raptor.Common.Infra`...), y `Raptor.Common` es la facade. Cada version
del paquete corresponde al tag de este repo con el mismo numero.

```bash
dotnet add package Raptor.Common            # todo
dotnet add package Raptor.Common.Contracts  # solo lo que usa la capa Domain
```

## Dependencias

Todas en version estable ([ADR-0007](docs/adr/0007-solo-dependencias-estables.md)). La fuente de verdad
es cada `.csproj`; esta tabla dice para que esta cada una.

| Paquete | Ensamblado | Proposito |
|---|---|---|
| Serilog | MultiTenancy, Infra | Logger estructurado. |
| Serilog.Extensions.Logging | Infra | Puente de Serilog a `ILogger`. |
| Serilog.Sinks.Console / Debug / Seq | Infra | Destinos de los logs. |
| OpenTelemetry.Extensions.Hosting | Infra | Arranque de OpenTelemetry en el host. |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | Infra | Exportacion OTLP de trazas y metricas. |
| OpenTelemetry.Instrumentation.AspNetCore / Http / Runtime | Infra | Instrumentacion de requests, `HttpClient` y runtime. |
| Microsoft.Extensions.Http.Resilience | Infra | Resiliencia de llamadas HTTP salientes. |
| AspNetCore.HealthChecks.NpgSql / Redis | Infra | Health checks de PostgreSQL y Redis. |
| Dapper | Infra | Envoltorio de consultas con medicion de tiempo. |
| Npgsql | Infra | Conexiones y migraciones de PostgreSQL. |
| Microsoft.AspNetCore.App (framework) | MultiTenancy, Infra, Web | `HttpContext`, middlewares, opciones. |

`Common.Contracts` y `Common.Messaging` no tienen dependencias externas.

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
builder.Services.AddObservability(builder.Configuration, meterName: "MiWebApi");
builder.Services.AddMediator(typeof(Program).Assembly);
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
    "Version": "1.0.0",
    "IncludeSqlText": false
  },
  "Observability": {
    "ServiceName": "MiWebApi",
    "ServiceVersion": "1.0.0",
    "OtlpEndpoint": "http://localhost:4317",
    "MetricsOtlpEndpoint": "http://localhost:9090/api/v1/otlp/v1/metrics"
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
| No aparecen metricas en Prometheus | La API ya no publica `/metrics`: empuja por OTLP | Configura `Observability:MetricsOtlpEndpoint` al receptor OTLP de Prometheus y arrancalo con `--web.enable-otlp-receiver`. |
| Logs sin tenant | Middleware multi-tenant no registrado | Asegura `app.UseTenantResolution()` antes de procesar endpoints. |
| Llamadas HTTP salientes sin tenant | Falta propagacion en `HttpClient` | Usa `.AddTenantPropagation()` al registrar clientes HTTP. |
| Jobs/consumers sin tenant en logs | No se setea contexto fuera de HTTP | Ejecuta procesos con `ITenantExecutionContextRunner`. |

## Notas tecnicas

- OpenTelemetry exporta traces y metrics por OTLP al endpoint configurado.
- Las metricas pueden salir a un destino distinto del de las trazas con
  `Observability:MetricsOtlpEndpoint` (HTTP, para el receptor OTLP de Prometheus). Ya no se usa
  `AddPrometheusExporter`: ese paquete nunca publico una version estable.
- Serilog usa `CustomLogging:LogEventLevel` (por defecto Verbose); en `Development` fuerza al menos `Debug`.
- El middleware de tenant agrega `tenant.id` al `Activity` actual y `TenantId` al scope de logs por request.
- `RejectUnknownTenants=true` rechaza tenants no registrados cuando existe catalogo de tenants en configuracion.
- `InteractorPipeline` registra cada peticion y respuesta con los campos sensibles tapados (`***`) por
  nombre de propiedad: password, token, secret, apikey y compania
  ([ADR-0006](docs/adr/0006-enmascarar-datos-sensibles-por-lista-negra.md)). La lista
  `SensitiveDataMasker.Terminos` es publica pero de solo lectura: un termino nuevo se agrega aqui, en
  `Common`. Los diccionarios se tapan por clave, y los parametros SQL que registra
  `DapperSqlDbConnectionBase` pasan por el mismo enmascarado (tambien con `DynamicParameters`).
- Sin `CustomLogging:LogEventLevel` el nivel es `Verbose`: fijalo siempre en produccion.
