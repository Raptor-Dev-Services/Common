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
  (`OperationCanceledException`) se registra como `Error` y responde 500; si te molesta ese ruido,
  atrapala antes con tu propio middleware.

## Dos formatos de error en la misma API

Los fallos esperados salen en el envelope (`ResultViewModel`: `isSuccess`, `message`) y las
excepciones en ProblemDetails. Son dos formatos, y el cliente tiene que entender los dos.

Si prefieres uno solo, no uses `UseCoreProblemDetails` y escribe tu propio manejador que responda
con el envelope. En ASP.NET Core la forma idiomatica es un `IExceptionHandler`:

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
