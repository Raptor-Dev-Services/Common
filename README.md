# Raptor Common

Libreria base para WebApi en .NET 10. Resuelve lo que toda API repite para que tu codigo sea solo
negocio: casos de uso con un mediator y su pipeline, resultados y errores estandar, multi-tenancy,
logging estructurado con secretos enmascarados, trazas y metricas por OpenTelemetry, resiliencia HTTP,
health checks, fabricas de conexion y migraciones SQL al arranque.

[![NuGet](https://img.shields.io/nuget/v/Raptor.Common.svg)](https://www.nuget.org/packages/Raptor.Common)
Licencia MIT.

## Instalacion

Dos caminos, con el mismo codigo. Elige uno y **no los mezcles** en una misma solucion.

**Paquete NuGet**, lo mas simple:

```bash
dotnet add package Raptor.Common --version 2.1.2
```

**Submodulo de git**, si quieres el codigo dentro de tu repo para leerlo y depurarlo:

```bash
git submodule add https://github.com/Raptor-Dev-Services/Common Common
git -C Common checkout v2.1.2
```

```xml
<ProjectReference Include="Common/Common/Common.csproj" />
```

El submodulo pide un paso mas si tu repo usa `Directory.Build.props` o gestion central de paquetes:
esta en la [guia de instalacion como submodulo](https://github.com/Raptor-Dev-Services/Common/blob/main/docs/guia/01-instalar-como-submodulo.md). La de
[NuGet](https://github.com/Raptor-Dev-Services/Common/blob/main/docs/guia/02-instalar-desde-nuget.md) explica como instalar solo lo que cada capa necesita.
Las diferencias entre los dos caminos estan [aqui](https://github.com/Raptor-Dev-Services/Common/blob/main/docs/guia/README.md#submodulo-o-nuget-es-lo-mismo).

## En 30 segundos

```csharp
using Common.Logging;
using Common.Messaging;
using Common.Observability;
using Common.ViewModels;
using Common.Web;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLoggingServices(builder.Configuration);
builder.Services.AddObservability(builder.Configuration, meterName: "MiApi");
builder.Services.AddMediator(typeof(Program).Assembly);
builder.Services.TryAddScoped(typeof(ResultViewModel<>));
builder.Services.AddControllers();

var app = builder.Build();
app.UseCoreProblemDetails();
app.UseCorrelationId();
app.MapControllers();
app.Run();
```

Un caso de uso completo, de la peticion HTTP al JSON de respuesta, esta en
[tu primer endpoint](https://github.com/Raptor-Dev-Services/Common/blob/main/docs/guia/03-primer-endpoint.md).

## Que trae

| Ensamblado | Paquete NuGet | Namespaces | Para que |
|---|---|---|---|
| `Common.Contracts` | `Raptor.Common.Contracts` | `Common.Results`, `Common.Errors`, `Common.Exceptions`, `Common.Messaging` | Resultados, fallos tipados, `BusinessRuleException`. Sin dependencias. |
| `Common.Messaging` | `Raptor.Common.Messaging` | `Common.Messaging`, `Common.Abstractions` | Contratos del mediator: peticiones, handlers, notificaciones. |
| `Common.MultiTenancy` | `Raptor.Common.MultiTenancy` | `Common.MultiTenancy` | Resolucion y contexto de tenant, catalogo por configuracion. |
| `Common.Infra` | `Raptor.Common.Infra` | `Common.Logging`, `Common.Observability`, `Common.Data`, `Common.PostgreSql`, `Common.Http`, `Common.HealthChecks`, `Common.Options`, `Common.Messaging` | Implementacion del mediator, logging, OpenTelemetry, datos, migraciones, HTTP, salud, opciones. |
| `Common.Web` | `Raptor.Common.Web` | `Common.Web`, `Common.ViewModels` | Middlewares (errores, correlacion, tenant) y el envelope `ResultViewModel`. |
| `Common` | `Raptor.Common` | (ninguno: facade) | Trae los cinco. |

Los namespaces no dependen del ensamblado: `Common.Messaging` se reparte entre tres, y un `using`
funciona igual con la facade que con las sub-librerias
([ADR-0001](https://github.com/Raptor-Dev-Services/Common/blob/main/docs/adr/0001-dividir-en-sub-librerias-con-facade.md)).

### Referencia por capa

| Capa | Ensamblados |
|---|---|
| Domain | Contracts |
| Application | Contracts + Messaging (+ MultiTenancy si lee el tenant) |
| Infrastructure | Contracts + Messaging + MultiTenancy + Infra |
| Presentation | Contracts + Messaging + Web |
| Host | Infra + Web |

## Documentacion

| | |
|---|---|
| [Guia de uso](https://github.com/Raptor-Dev-Services/Common/blob/main/docs/guia/README.md) | Instalacion, primer endpoint y una guia por capacidad. |
| [SRS](https://github.com/Raptor-Dev-Services/Common/blob/main/docs/srs.md) | Que tiene que hacer la libreria y como se verifica cada requisito. |
| [ADR](https://github.com/Raptor-Dev-Services/Common/blob/main/docs/adr/README.md) | Por que es como es y que se descarto. |
| [CHANGELOG](https://github.com/Raptor-Dev-Services/Common/blob/main/CHANGELOG.md) | Que cambio en cada version y como migrar. |
| [CONTRIBUTING](https://github.com/Raptor-Dev-Services/Common/blob/main/CONTRIBUTING.md) | Como hacer un cambio y publicar una version. |

La API publica trae comentarios XML: con el paquete o el submodulo, el IDE muestra la
documentacion de cada tipo y metodo.

## Configuracion

Todas las claves que lee la libreria. Ninguna seccion es obligatoria: cada capacidad lee solo la suya.

```json
{
  "CustomLogging": {
    "Project": "MiProducto",
    "Application": "MiApi",
    "Version": "1.0.0",
    "LogEventLevel": "Information",
    "SeqUri": "http://localhost:5341",
    "IncludeSqlText": false
  },
  "Observability": {
    "ServiceName": "MiApi",
    "ServiceVersion": "1.0.0",
    "OtlpEndpoint": "http://localhost:4317",
    "MetricsOtlpEndpoint": "http://localhost:9090/api/v1/otlp/v1/metrics"
  },
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
      "tenant-a": {
        "IsEnabled": true,
        "ConnectionStrings": { "Default": "Host=...;Database=tenant_a;..." },
        "Settings": { "Region": "MX" }
      }
    }
  }
}
```

Que hace cada una: [logging y observabilidad](https://github.com/Raptor-Dev-Services/Common/blob/main/docs/guia/07-logging-y-observabilidad.md) y
[multi-tenancy](https://github.com/Raptor-Dev-Services/Common/blob/main/docs/guia/06-multi-tenancy.md). **Fija siempre `LogEventLevel`:** sin el, el nivel
es `Verbose` y se registra todo.

## Compatibilidad

| | |
|---|---|
| Framework | `net10.0` |
| SDK | .NET SDK 10.x |
| Base de datos (piezas concretas) | PostgreSQL. Las abstracciones de `Common.Data` sirven para cualquier motor. |
| Dependencias | Solo versiones estables ([ADR-0007](https://github.com/Raptor-Dev-Services/Common/blob/main/docs/adr/0007-solo-dependencias-estables.md)) |

## Licencia

MIT. Ver [`LICENSE`](https://github.com/Raptor-Dev-Services/Common/blob/main/LICENSE). Aplica igual al submodulo y a los paquetes de nuget.org.
