# 05 - Errores

`Common` separa dos clases de error:

- **Fallos esperados** (no existe, no es valido, ya estaba): el handler los **devuelve** como
  respuesta de fallo (`IFailure`). Ver [04 - Casos de uso](04-casos-de-uso-y-mediator.md).
- **Excepciones**: una regla de negocio rota en lo profundo, o algo que no deberia pasar. Se lanzan y
  las atrapa un middleware.

## `BusinessRuleException`

Una regla de negocio incumplida. Su mensaje es para el usuario, asi que escribelo para el usuario:

```csharp
using Common.Exceptions;

throw new BusinessRuleException("No se puede cancelar una venta ya facturada.");
```

## `ErrorList`: juntar varios errores

Para validar varias cosas y reportarlas todas de una vez:

```csharp
using Common.Errors;

var errores = new ErrorList();
if (string.IsNullOrWhiteSpace(cmd.Nombre)) errores.Add("El nombre es obligatorio.");
if (cmd.Precio <= 0) errores.Add("El precio debe ser mayor que cero.");

if (!errores.IsEmpty)
    throw errores.AsException();   // una BusinessRuleException con los mensajes unidos
```

Comprueba siempre `IsEmpty` antes: con la lista vacia, `AsException()` y `ToString()` lanzan
`InvalidOperationException`.

## `UseCoreProblemDetails`: excepciones a respuestas HTTP

```csharp
app.UseCoreProblemDetails();   // lo primero del pipeline
```

Envuelve todo lo que viene despues y convierte las excepciones que se escapen en respuestas
[RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) con `Content-Type: application/problem+json`:

| Excepcion | Status | `detail` | Log |
|---|---|---|---|
| `BusinessRuleException` | 400 | El mensaje de la excepcion | `Warning` |
| Cualquier otra | 500 | `"An unexpected error occurred"` | `Error`, con la excepcion completa |

```json
{
  "title": "Business rule violation",
  "status": 400,
  "detail": "No se puede cancelar una venta ya facturada.",
  "instance": "/api/ventas/42/cancelar",
  "traceId": "9897d3cd1ada80d805422e65d2aecc54",
  "tenantId": "acme"
}
```

- Un 500 **nunca** expone el mensaje ni la traza de la excepcion: el cliente recibe el `traceId` y
  con el se busca el detalle en los logs.
- `tenantId` aparece solo si la peticion tenia tenant resuelto.
- Registralo **primero** en el pipeline. Lo que se registre antes que el no queda protegido.
- Limites conocidos: si la respuesta ya empezo a escribirse (un streaming que falla a la mitad), no
  puede cambiar el status y la excepcion se propaga. Y una peticion que el cliente cancela
  (`OperationCanceledException`) se registra como `Error` y responde 500. `UseEnvelopeExceptionHandling`
  resuelve ambos casos.

## Excepciones comunes y `UseEnvelopeExceptionHandling` (envelope unico)

Para que **todos** los errores salgan en el envelope `{ data, isSuccess, message, utcTimeStamp }`
(camelCase), sin ProblemDetails, `Common.Contracts` trae una jerarquia de excepciones y `Common.Web`
un middleware que las traduce. Todas viven en el namespace `Common.Exceptions` y derivan de
`BusinessRuleException`, asi que su mensaje es para el usuario:

| Excepcion | Status | Cuando |
|---|---|---|
| `BusinessRuleException` | 400 | Peticion o regla invalida |
| `ResourceNotFoundException` | 404 | El recurso no existe |
| `BusinessRuleViolationException` | 409 | Choca con el estado actual (ya existe, ya procesado) |
| `DataIntegrityException` | 422 | Datos guardados que el sistema no puede procesar |
| `OperationCanceledException` con `RequestAborted` cancelado | 499, sin cuerpo | El cliente cerro la conexion: no es error (log `Information`) |
| Cualquier otra | 500 | Mensaje generico `"Ocurrio un error inesperado."`; la excepcion completa solo va al log (`Error`) |

```csharp
// Program.cs
app.UseCorrelationId();               // opcional: pone el CorrelationId en los logs del middleware
app.UseEnvelopeExceptionHandling();   // lo primero tras el correlation id; en vez de UseCoreProblemDetails
```

No requiere registrar servicios. Usa uno u otro (`UseCoreProblemDetails` o este), no los dos. No cambia
nada para quien ya usa `UseCoreProblemDetails`: ahi las subclases nuevas salen como 400.

```csharp
throw new ResourceNotFoundException("No existe el folio 7.");   // 404
// {"data":null,"isSuccess":false,"message":"No existe el folio 7.","utcTimeStamp":"2026-10-27T16:00:00Z"}
```

- Si la respuesta ya empezo a escribirse, el middleware no puede cambiar el status: solo registra.
- Cada log lleva `TraceId` (el de la actividad o `HttpContext.TraceIdentifier`) y `CorrelationId`.
- Una `OperationCanceledException` que **no** viene del cliente (p. ej. un timeout interno) si es 500.

## Dos formatos de error en la misma API

Los fallos esperados salen en el envelope (`ResultViewModel`: `isSuccess`, `message`) y, con
`UseCoreProblemDetails`, las excepciones en ProblemDetails. Son dos formatos, y el cliente tiene que
entender los dos. Si prefieres uno solo, usa `UseEnvelopeExceptionHandling` (arriba).

Si necesitas algo a medida, en ASP.NET Core la forma idiomatica es un `IExceptionHandler`:

```csharp
internal sealed class EnvelopeExceptionHandler(ILogger<EnvelopeExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, message) = exception is BusinessRuleException
            ? (StatusCodes.Status400BadRequest, exception.Message)
            : (StatusCodes.Status500InternalServerError, "Ocurrio un error inesperado.");

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Excepcion no controlada");

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ResultViewModel<object>().Fail(message), ct);
        return true;
    }
}

builder.Services.AddExceptionHandler<EnvelopeExceptionHandler>();
builder.Services.AddProblemDetails();
app.UseExceptionHandler();
```

Las dos opciones son validas. Lo que no conviene es no decidir y acabar con un tercer formato: el de
la pagina de error por defecto de ASP.NET Core.
