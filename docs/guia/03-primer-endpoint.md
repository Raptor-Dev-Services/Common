# 03 - Tu primer endpoint

Una API minima con un caso de uso completo: de la peticion HTTP a la respuesta JSON, pasando por el
mediator y su pipeline. Todo este codigo esta probado contra los paquetes publicados.

Parte de un proyecto web vacio (`dotnet new web`) con `Common` ya instalado por
[submodulo](01-instalar-como-submodulo.md) o por [NuGet](02-instalar-desde-nuget.md).

## El flujo

```
HTTP GET /api/saludos/Ana
  -> SaludosController           arma la peticion y la manda al mediator
  -> IMediator.Send              busca el handler y lo envuelve en el pipeline
  -> InteractorPipeline          registra la peticion (con secretos tapados)
  -> ObtenerSaludoHandler        hace el trabajo y devuelve una respuesta tipada
  -> InteractorPipeline          registra la respuesta y la PUBLICA
  -> ObtenerSaludoPresenter      recibe la respuesta y llena el ResultViewModel
  -> SaludosController           elige el status HTTP y devuelve el ResultViewModel
```

El handler no sabe nada de HTTP y el controller no sabe nada del negocio: los une el presenter.

## Program.cs

```csharp
using Common.Logging;
using Common.Messaging;
using Common.Observability;
using Common.ViewModels;
using Common.Web;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLoggingServices(builder.Configuration);
builder.Services.AddObservability(builder.Configuration, meterName: "Demo.Api");
builder.Services.AddMediator(typeof(Program).Assembly);        // registra handlers y presenters
builder.Services.TryAddScoped(typeof(ResultViewModel<>));      // un envelope por peticion
builder.Services.AddControllers();

var app = builder.Build();

app.UseCoreProblemDetails();   // primero: atrapa lo que se escape de todo lo demas
app.UseCorrelationId();

app.MapControllers();
app.Run();
```

## El caso de uso

```csharp
using Common.Messaging;
using Common.Results;
using Common.ViewModels;
using Microsoft.AspNetCore.Mvc;

// 1. La peticion: que se pide, y que tipo de respuesta devuelve.
public sealed record ObtenerSaludoRequest(string Nombre) : IRequest<ObtenerSaludoResponse>;

// 2. Las respuestas posibles. Una base abstracta, y un tipo por cada resultado.
public abstract record ObtenerSaludoResponse : IResponse;
public sealed record ObtenerSaludoOk(SaludoDto Data) : ObtenerSaludoResponse, ISuccess<SaludoDto>;
public sealed record ObtenerSaludoNoEncontrado(string Message) : ObtenerSaludoResponse, INotFoundFailure;

public sealed record SaludoDto(string Texto);

// 3. El handler: la logica. Devuelve una respuesta, no lanza para los casos esperados.
internal sealed class ObtenerSaludoHandler : IRequestHandler<ObtenerSaludoRequest, ObtenerSaludoResponse>
{
    public Task<ObtenerSaludoResponse> Handle(ObtenerSaludoRequest request, CancellationToken cancellationToken)
    {
        ObtenerSaludoResponse response = request.Nombre == "nadie"
            ? new ObtenerSaludoNoEncontrado("No hay a quien saludar.")
            : new ObtenerSaludoOk(new SaludoDto($"Hola, {request.Nombre}"));
        return Task.FromResult(response);
    }
}

// 4. El presenter: pasa la respuesta al envelope que va a devolver el controller.
internal sealed class ObtenerSaludoPresenter(ResultViewModel<SaludosController> viewModel)
    : INotificationHandler<ObtenerSaludoResponse>
{
    public Task Handle(ObtenerSaludoResponse response, CancellationToken cancellationToken)
    {
        switch (response)
        {
            case IFailure failure: viewModel.Set(failure); break;
            case ISuccess<SaludoDto> success: viewModel.Set(success); break;
        }
        return Task.CompletedTask;
    }
}

// 5. El controller: manda la peticion y traduce el tipo de respuesta a status HTTP.
[ApiController]
[Route("api/saludos")]
public sealed class SaludosController(IMediator mediator, ResultViewModel<SaludosController> viewModel) : ControllerBase
{
    [HttpGet("{nombre}")]
    public async Task<IActionResult> Get(string nombre, CancellationToken cancellationToken)
    {
        var response = await mediator.Send(new ObtenerSaludoRequest(nombre), cancellationToken);
        return response switch
        {
            INotFoundFailure => NotFound(viewModel),
            IFailure => BadRequest(viewModel),
            _ => Ok(viewModel),
        };
    }
}
```

## Lo que responde

```
GET /api/saludos/Ana
200  {"data":{"texto":"Hola, Ana"},"isSuccess":true,"message":null,"utcTimeStamp":"..."}

GET /api/saludos/nadie
404  {"data":null,"isSuccess":false,"message":"No hay a quien saludar.","utcTimeStamp":"..."}
```

Y cada respuesta lleva la cabecera `X-Correlation-Id`, para que quien reporte un fallo pueda citarla.

## appsettings.json minimo

```json
{
  "CustomLogging": {
    "Application": "Demo.Api",
    "Version": "1.0.0",
    "LogEventLevel": "Information"
  },
  "Observability": {
    "ServiceName": "Demo.Api"
  }
}
```

Fija siempre `LogEventLevel`: sin el, el nivel es `Verbose` y se registra todo.

## Siguiente paso

- Para proyectos con muchos casos de uso, una base comun para presenters y controllers te ahorra
  repetir el `switch`: esta en [04 - Casos de uso y mediator](04-casos-de-uso-y-mediator.md).
- Como se convierten las excepciones en respuestas: [05 - Errores](05-errores.md).
