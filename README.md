# GTM-Suite Common

Libreria base reutilizable para WebApi (.NET 10) con logging (Serilog + Seq) y observabilidad (OpenTelemetry). Incluye resultados estandar, errores, excepciones y contratos de mensajeria.

## Paquetes

| Paquete | Version | Proposito tecnico |
|---|---|---|
| Microsoft.Extensions.Configuration | 10.0.2 | Lectura y binding de configuracion (appsettings, env vars, etc.). |
| Microsoft.Extensions.DependencyInjection | 10.0.2 | Registro y resolucion de dependencias. |
| Microsoft.Extensions.Logging | 10.0.2 | Abstracciones de logging. |
| Serilog | 4.3.0 | Logger estructurado. |
| OpenTelemetry.Extensions.Hosting | ver Common.csproj | Bootstrap OTel en host .NET. |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | ver Common.csproj | Exportacion OTLP (Grafana Tempo / OTEL collector). |
| OpenTelemetry.Instrumentation.AspNetCore | ver Common.csproj | Instrumentacion de requests ASP.NET Core. |
| OpenTelemetry.Instrumentation.Http | ver Common.csproj | Instrumentacion de HttpClient. |
| OpenTelemetry.Instrumentation.Runtime | ver Common.csproj | Metricas runtime (.NET GC, CPU, etc.). |
| OpenTelemetry.Instrumentation.Process | ver Common.csproj | Metricas de proceso. |

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
```

## Configuracion esperada (appsettings.json)

```json
{
  "CustomLogging": {
    "Project": "GTM-Suite",
    "SeqUri": "http://localhost:5341",
    "Application": "MiWebApi",
    "Version": "1.0.0"
  },
  "Observability": {
    "ServiceName": "MiWebApi",
    "ServiceVersion": "1.0.0",
    "OtlpEndpoint": "http://localhost:4317"
  }
}
```

## Troubleshooting

| Sintoma | Causa probable | Solucion |
|---|---|---|
| `AddSerilog` no existe | Falta `Serilog.Extensions.Logging` | Verifica el PackageReference en `Common.csproj`. |
| `WriteTo.Seq` no existe | Falta `Serilog.Sinks.Seq` | Instala el paquete correspondiente. |
| No llegan traces a Grafana | Endpoint OTLP incorrecto | Verifica `Observability:OtlpEndpoint` y conectividad. |

## Notas tecnicas

- OpenTelemetry exporta traces y metrics por OTLP al endpoint configurado.
- Serilog envia todo en Verbose y agrega propiedades globales para trazabilidad.
