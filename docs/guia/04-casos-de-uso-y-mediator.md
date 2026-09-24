# 04 - Casos de uso y mediator

`Common` trae su propio mediator ([ADR-0003](../adr/0003-mediator-propio-en-lugar-de-mediatr.md)).
Los nombres imitan a MediatR a proposito: si vienes de ahi, `IRequest`, `IRequestHandler`,
`INotificationHandler`, `IPipelineBehavior` e `IMediator` hacen lo que esperas.

Ensamblados: los contratos estan en `Common.Messaging` (y `IResponse` en `Common.Contracts`); la
implementacion (`Mediator`, `InteractorPipeline`, `AddMediator`) en `Common.Infra`. Tu capa de
aplicacion solo necesita los contratos.

## Las piezas

| Pieza | Que es | Namespace |
|---|---|---|
| `IRequest<TResponse>` | Una peticion y el tipo de lo que devuelve | `Common.Messaging` |
| `IRequestHandler<TRequest, TResponse>` | Quien la atiende. Uno por peticion | `Common.Messaging` |
| `IResponse` | Marca de "esto es una respuesta de caso de uso" | `Common.Messaging` |
| `INotificationHandler<T>` | Quien recibe algo publicado. Varios por tipo | `Common.Messaging` |
| `IPipelineBehavior<TRequest, TResponse>` | Algo que envuelve a todos los handlers | `Common.Messaging` |
| `IMediator` | `Send` (una peticion, un handler) y `Publish` (una notificacion, N handlers) | `Common.Messaging` |
| `ISuccess<T>`, `IFailure` y derivados | Clasifican la respuesta | `Common.Results` |

## Dos estilos de respuesta

### Estilo 1: respuestas tipadas (recomendado)

Cada caso de uso declara sus resultados posibles como tipos:

```csharp
public abstract record ObtenerSaludoResponse : IResponse;
public sealed record ObtenerSaludoOk(SaludoDto Data) : ObtenerSaludoResponse, ISuccess<SaludoDto>;
public sealed record ObtenerSaludoNoEncontrado(string Message) : ObtenerSaludoResponse, INotFoundFailure;
public sealed record ObtenerSaludoInvalido(string Message) : ObtenerSaludoResponse, IValidationFailure;
```

La firma del handler dice todo lo que puede pasar, y el controller elige el status por el **tipo** de
fallo, no por el texto del mensaje. Las interfaces de fallo que trae `Common`:

| Interfaz | Uso tipico | Status sugerido |
|---|---|---|
| `IValidationFailure` | La entrada no es valida | 400 |
| `INotFoundFailure` | Lo pedido no existe (o no es visible para quien pregunta) | 404 |
| `IConflictFailure` | Choca con el estado actual (duplicado, version vieja) | 409 |
| `IFailure` a secas | Cualquier otro fallo de negocio | 422 o 400 |

`Common` no decide el status: lo decide tu controller. Asi cada API puede tener su tabla.

### Estilo 2: `Result<T>`

Para casos simples, sin tipos propios por resultado:

```csharp
using Common.Abstractions;
using Common.Results;

public sealed record SumarRequest(int A, int B) : IResultRequest<int>;

internal sealed class SumarInteractor : ResultInteractorBase<SumarRequest, int>
{
    public override Task<Result<int>> Handle(SumarRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(request.A < 0 || request.B < 0
            ? Fail("Solo numeros positivos.")
            : OK(request.A + request.B));
}

internal sealed class SumarPresenter(ResultViewModel<CalculosController> viewModel) : IResultPresenter<int>
{
    public Task Handle(Result<int> result, CancellationToken cancellationToken)
    {
        switch (result)
        {
            case IFailure failure: viewModel.Set(failure); break;
            case ISuccess<int> success: viewModel.Set(success); break;
        }
        return Task.CompletedTask;
    }
}
```

`Result.OK(...)` devuelve un `SuccessResult<T>` (que es `ISuccess<T>`) y `Result.Fail(...)` un
`FailureResult<T>` (que es `IFailure`). La contra: todos los fallos son del mismo tipo, asi que el
controller no puede distinguir un 404 de un 409. Por eso el estilo 1 es el recomendado.

**Ojo con los presenters de `Result<T>`:** se publican por tipo, asi que un `IResultPresenter<int>`
recibe **todas** las respuestas `Result<int>` de la aplicacion. Si dos casos de uso devuelven
`Result<int>`, sus presenters se disparan para ambos. Con el estilo 1 no pasa: cada caso de uso
tiene su propio tipo de respuesta.

## El pipeline

Cada `Send` pasa por los `IPipelineBehavior` registrados. `AddMediator` registra uno,
`InteractorPipeline`, que:

1. Registra la peticion a nivel `Information`, con los campos sensibles tapados
   ([guia 07](07-logging-y-observabilidad.md#enmascarado-de-secretos)).
2. Ejecuta el handler.
3. Si la respuesta es un `IFailure`, la registra como `Warning`; si no, la registra enmascarada.
4. **Publica la respuesta** con `IMediator.Publish`: asi llega a los presenters.
5. Si el handler lanza, registra la excepcion (`Error` si es `BusinessRuleException`, `Critical` si
   es otra) y la relanza sin cambiarla.

El paso 4 es el que conecta al handler con el presenter. Sin `InteractorPipeline` registrado, los
presenters nunca se enteran.

Puedes agregar tus propios behaviors (validacion, transacciones, metricas):

```csharp
internal sealed class MedirTiempo<TRequest, TResponse>(ILogger<MedirTiempo<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var inicio = Stopwatch.GetTimestamp();
        try { return await next(); }
        finally { logger.LogDebug("{Request} en {Ms} ms", typeof(TRequest).Name, Stopwatch.GetElapsedTime(inicio).TotalMilliseconds); }
    }
}

services.AddScoped(typeof(IPipelineBehavior<,>), typeof(MedirTiempo<,>));
```

**Orden:** el primer behavior registrado es el mas externo. `AddMediator` registra
`InteractorPipeline`, asi que si registras el tuyo despues, el tuyo queda por dentro.

## Registrar handlers y presenters

**Por escaneo**, lo mas corto:

```csharp
services.AddMediator(typeof(Program).Assembly, typeof(OtroModulo).Assembly);
```

Registra como `Scoped` toda clase concreta de esos ensamblados que implemente `IRequestHandler<,>` o
`INotificationHandler<>`, mas el mediator y el `InteractorPipeline`. Las clases pueden ser `internal`.

**A mano**, para monolitos modulares donde cada modulo registra lo suyo:

```csharp
// En el Host, una sola vez: mediator y pipeline, sin escanear nada.
services.AddMediator();

// En cada modulo:
services.AddScoped<IRequestHandler<ObtenerSaludoRequest, ObtenerSaludoResponse>, ObtenerSaludoHandler>();
services.AddScoped<INotificationHandler<ObtenerSaludoResponse>, ObtenerSaludoPresenter>();
```

Es mas verboso, pero cada modulo declara exactamente lo que expone, y un handler que nadie registro
falla al arrancar con `ValidateOnBuild`, no con el primer usuario.

**Llama `AddMediator` una sola vez.** No evita duplicados: dos llamadas registran dos veces el
`InteractorPipeline`, y cada peticion se registra y se publica dos veces (los presenters corren
doble). Si varios modulos necesitan escanear, pasa todos los ensamblados en la misma llamada.

## Una base para no repetir presenters y controllers

Con muchos casos de uso, el `switch` del presenter y el del controller se repiten. `Common` no trae
estas bases a proposito (cada API decide su tabla de status), pero son cortas:

```csharp
public abstract class ResultPresenter<TController, TResponse, TData>(ResultViewModel<TController> viewModel)
    : INotificationHandler<TResponse>
    where TResponse : IResponse
{
    public Task Handle(TResponse response, CancellationToken cancellationToken)
    {
        switch (response)
        {
            case IFailure failure: viewModel.Set(failure); break;
            case ISuccess<TData> success: viewModel.Set(success, Shape); break;
            default:
                // Un presenter que no sabe que hacer dejaria el envelope vacio y el cliente
                // recibiria 200 con isSuccess=false y sin mensaje. Mejor que truene.
                throw new InvalidOperationException($"{GetType().Name} no sabe presentar {response.GetType().Name}.");
        }
        return Task.CompletedTask;
    }

    // Sobreescribe para dar forma a los datos (ocultar campos, aplanar).
    protected virtual object Shape(TData data) => data!;
}

internal sealed class ObtenerSaludoPresenter(ResultViewModel<SaludosController> vm)
    : ResultPresenter<SaludosController, ObtenerSaludoResponse, SaludoDto>(vm);
```

```csharp
[ApiController]
public abstract class BaseApiController(IMediator mediator) : ControllerBase
{
    protected Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct) =>
        mediator.Send(request, ct);

    protected IActionResult MapResult<T>(IResponse response, ResultViewModel<T> viewModel) => response switch
    {
        IValidationFailure => BadRequest(viewModel),
        INotFoundFailure => NotFound(viewModel),
        IConflictFailure => Conflict(viewModel),
        IFailure => UnprocessableEntity(viewModel),
        _ => Ok(viewModel),
    };
}

public sealed class SaludosController(IMediator mediator, ResultViewModel<SaludosController> viewModel)
    : BaseApiController(mediator)
{
    [HttpGet("{nombre}")]
    public async Task<IActionResult> Get(string nombre, CancellationToken ct) =>
        MapResult(await SendAsync(new ObtenerSaludoRequest(nombre), ct), viewModel);
}
```

## El envelope: `ResultViewModel<T>`

```json
{ "data": { ... }, "isSuccess": true, "message": null, "utcTimeStamp": "2026-09-24T17:13:18Z" }
```

- Registralo como `Scoped` con `services.TryAddScoped(typeof(ResultViewModel<>))`: el presenter y el
  controller de la **misma peticion** tienen que recibir la misma instancia.
- `T` es solo una etiqueta (normalmente el controller) para que dos controllers no compartan envelope.
- `Set(IFailure)` y `Set(ISuccess<TData>, shape?)` son lo normal desde un presenter. `OK(...)` y
  `Fail(...)` existen para llenarlo a mano.
- El parametro `statusCode` de `OK` y `Fail` **no hace nada**: el status lo decide quien devuelve el
  envelope. Se conserva por compatibilidad.

`GenericViewModel<T>` es el mismo envelope sin integracion con `Result`; sirve cuando no hay mediator
de por medio.

## Notificaciones propias

`Publish` no es solo para presenters. Cualquier `INotification` sirve para avisar a otros modulos:

```csharp
public sealed record UsuarioRegistrado(Guid UsuarioId) : INotification;

await mediator.Publish(new UsuarioRegistrado(id), ct);
```

Todos los `INotificationHandler<UsuarioRegistrado>` registrados lo reciben **en paralelo**
(`Task.WhenAll`), sin orden entre ellos y en la misma peticion. Si uno lanza, `Publish` lanza. No es
una cola: si necesitas reintentos o que sobreviva a un reinicio, usa una cola de verdad.

**Ojo con el paralelismo:** los handlers comparten el scope de la peticion. Si dos usan el mismo
servicio no seguro entre hilos (un `DbContext` de EF Core es el caso tipico), pueden chocar. Si tus
handlers de una notificacion tocan la base, que no compartan el contexto, o que la notificacion
tenga un solo handler.

## Excepciones dentro de un handler

Los casos esperados (no existe, no es valido) se **devuelven** como respuesta de fallo. Una excepcion
es para lo que no deberia pasar, o para una regla de negocio que se rompe en lo profundo:

```csharp
throw new BusinessRuleException("La membresia ya vencio.");
```

La excepcion sale de `Send` tal cual la lanzaste, sin envolver, sea el handler `async` o no. Como se
convierte en respuesta HTTP esta en [05 - Errores](05-errores.md).

## Problemas frecuentes

| Sintoma | Causa |
|---|---|
| `InvalidOperationException: No handler registered for request X` | El handler no esta registrado: falta en `AddMediator(ensamblado)` o en el registro a mano. |
| El controller responde con `data: null` y `message: ""` | El presenter no se registro, o `ResultViewModel<>` no es `Scoped` y el presenter lleno otra instancia. |
| El presenter no se ejecuta | Falta `AddMediator()` (registra `InteractorPipeline`, que es quien publica). |
| Un presenter de `Result<int>` se dispara en casos de uso ajenos | Ver "Ojo con los presenters de `Result<T>`" arriba. |
