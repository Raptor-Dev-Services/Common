# 07 - Logging y observabilidad

Tres piezas independientes, todas en `Common.Infra` (la correlacion en `Common.Web`):

| Pieza | Registro | Que hace |
|---|---|---|
| Logging | `AddLoggingServices` | Serilog con enriquecimiento, a consola y a Seq |
| Trazas y metricas | `AddObservability` | OpenTelemetry por OTLP |
| Correlacion | `UseCorrelationId` | Un id por peticion en logs, traza y respuesta |

Puedes usar cualquiera sin las otras.

## Logging

```csharp
builder.Services.AddLoggingServices(builder.Configuration);
```

```json
{
  "CustomLogging": {
    "Project": "MiProducto",
    "Application": "MiProducto.Api",
    "Version": "1.4.0",
    "LogEventLevel": "Information",
    "SeqUri": "http://localhost:5341",
    "IncludeSqlText": false
  }
}
```

- Reemplaza los proveedores de logging por Serilog. Escribe a **consola** y a **debug** siempre, y a
  **Seq** solo si `SeqUri` tiene valor.
- Cada evento lleva `Project`, `Application`, `Version`, `Environment`, `MachineName`, y `TenantId`
  cuando la ejecucion tiene tenant. Lo que agregues con `BeginScope` tambien viaja.
- **Nivel:** sale de `LogEventLevel` (`Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`).
  **Sin ese valor es `Verbose`**: fijalo siempre en produccion. En `Development` baja al menos a `Debug`.
- El entorno se lee de la variable `ASPNETCORE_ENVIRONMENT`.

Si ya tienes tu propia configuracion de Serilog (por ejemplo con `UseSerilog` y
`ReadFrom.Configuration`), no llames a `AddLoggingServices`: todo lo demas de `Common` escribe por
`ILogger` y funciona con cualquier proveedor. Para no perder el `TenantId` en tus logs, agrega el
enriquecedor: `.Enrich.With<TenantLogEventEnricher>()`.

## Enmascarado de secretos

El pipeline del mediator registra cada peticion y cada respuesta. Antes de hacerlo, **tapa con `***`**
toda propiedad cuyo nombre contenga un termino sensible:

`password`, `contrasena`, `token`, `secret`, `apikey`, `api_key`, `authorization`, `cardnumber`,
`card_number`, `cvv`, `cvc`, `privatekey`, `private_key`, `connectionstring`, `otp`, `totp`.

```
Peticion:  { Email = "ana@x.com", Password = "Secreta.2026" }
En el log: { "Email": "ana@x.com", "Password": "***", "$type": "LoginRequest" }
```

- La coincidencia es **parcial** y sin mayusculas: `NewPassword`, `PasswordHash`, `RefreshToken` caen.
- Lo que describe al secreto sin contenerlo no se tapa: `AccessTokenExpiresAt`, `TokenType`.
- Los diccionarios se tapan por clave, igual que los objetos por propiedad.
- Tambien se enmascaran los **parametros SQL** que registra el envoltorio Dapper ([guia 08](08-datos-y-migraciones.md)).

La coincidencia parcial tambien produce falsos positivos: `otp` tapa `Footprint`, `token` tapa
`TokenizerVersion`. Se prefirio tapar de mas a filtrar un secreto.

**El limite:** es una lista negra ([ADR-0006](../adr/0006-enmascarar-datos-sensibles-por-lista-negra.md)).
Un campo sensible con un nombre fuera de la lista (`Curp`, `NumeroDeSeguro`) **se registra en claro**.
Nombra tus campos sensibles con un termino de la lista, o no los pongas en peticiones ni respuestas.

Puedes usar el enmascarado en tus propios logs:

```csharp
logger.LogInformation("Recibido {@Payload}", SensitiveDataMasker.Enmascarar(payload));
```

## Correlacion

```csharp
app.UseCorrelationId();
```

- Si la peticion trae `X-Correlation-Id`, lo reutiliza; si no, genera uno.
- El valor que llega del cliente **se sanea**: solo letras, digitos y `- _ . :`, y como maximo 128
  caracteres. Asi un cliente no puede meter saltos de linea en tus logs ni inflarlos.
- Lo devuelve en la respuesta (`X-Correlation-Id`), lo pone en el scope de log (`CorrelationId`) y en
  la traza (`correlation_id`).

Con el id que el cliente ve en la respuesta, se encuentran en Seq todos los eventos de esa peticion.

## Trazas y metricas

```csharp
builder.Services.AddObservability(builder.Configuration, meterName: "MiProducto.Api");
```

```json
{
  "Observability": {
    "ServiceName": "MiProducto.Api",
    "ServiceVersion": "1.4.0",
    "OtlpEndpoint": "http://localhost:4317",
    "MetricsOtlpEndpoint": "http://localhost:9090/api/v1/otlp/v1/metrics"
  }
}
```

- **Trazas:** de ASP.NET Core y `HttpClient`, exportadas por OTLP a `OtlpEndpoint`.
- **Metricas:** de ASP.NET Core, `HttpClient`, el runtime de .NET y el meter `meterName` (el tuyo, con
  tus metricas propias). Van a `MetricsOtlpEndpoint` si esta, **por HTTP/protobuf** (lo que espera el
  receptor OTLP de Prometheus); si no, a `OtlpEndpoint`.
- El recurso lleva `service.name`, `service.version`, `deployment.environment`, `host.name` y
  `service.instance.id`.
- **Sin endpoints no exporta nada**, y la aplicacion arranca igual.

Metricas propias con el mismo `meterName`:

```csharp
private static readonly Meter Meter = new("MiProducto.Api");
private static readonly Counter<long> Ventas = Meter.CreateCounter<long>("ventas_registradas");

Ventas.Add(1, new KeyValuePair<string, object?>("canal", "mostrador"));
```

**No hay endpoint `/metrics`.** Las metricas se empujan por OTLP
([ADR-0005](../adr/0005-metricas-y-trazas-solo-por-otlp.md)). Para Prometheus, arrancalo con
`--web.enable-otlp-receiver` y apunta `MetricsOtlpEndpoint` a `http://prometheus:9090/api/v1/otlp/v1/metrics`.

## Problemas frecuentes

| Sintoma | Causa | Solucion |
|---|---|---|
| Los logs no llegan a Seq | `SeqUri` vacio o Seq apagado | Revisa `CustomLogging:SeqUri` |
| Demasiados logs en produccion | Falta `LogEventLevel` (cae a `Verbose`) | Fijalo a `Information` o `Warning` |
| No llegan trazas | `OtlpEndpoint` vacio o inalcanzable | Revisa la URL y que el colector escuche en gRPC (4317) |
| No llegan metricas a Prometheus | Prometheus sin receptor OTLP | `--web.enable-otlp-receiver` y `MetricsOtlpEndpoint` |
| Logs sin `TenantId` | La ejecucion no tiene tenant | [Guia 06](06-multi-tenancy.md): middleware o `ITenantExecutionContextRunner` |
