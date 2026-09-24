using System.Text.Json;
using Xunit;
using Common.Exceptions;
using Common.Messaging;
using Common.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Common.Tests;

/// <summary>
/// Una excepcion lanzada por un handler tiene que salir de <c>Send</c> tal cual.
///
/// El mediator invoca al handler por reflexion, y <c>MethodInfo.Invoke</c> envuelve en
/// <c>TargetInvocationException</c> lo que se lance antes del primer <c>await</c>. Una
/// <c>BusinessRuleException</c> de un handler sincrono llegaba disfrazada: el pipeline la registraba
/// como Critical y <c>UseCoreProblemDetails</c>, que no la reconocia, respondia 500 en vez de 400.
/// Se encontro consumiendo el paquete publicado desde una API de prueba.
/// </summary>
public sealed class MediatorExceptionTests
{
    private sealed record Peticion(bool Sincrono) : IRequest<Respuesta>;
    private sealed record Respuesta : IResponse;

    private sealed class HandlerQueLanza : IRequestHandler<Peticion, Respuesta>
    {
        public Task<Respuesta> Handle(Peticion request, CancellationToken cancellationToken) =>
            request.Sincrono ? LanzaSincrono() : LanzaAsync();

        private static Task<Respuesta> LanzaSincrono() => throw new BusinessRuleException("regla rota");

        private static async Task<Respuesta> LanzaAsync()
        {
            await Task.Yield();
            throw new BusinessRuleException("regla rota");
        }
    }

    private static IMediator Mediador()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMediator();
        services.AddScoped<IRequestHandler<Peticion, Respuesta>, HandlerQueLanza>();
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IMediator>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task La_excepcion_del_handler_sale_sin_envolver(bool sincrono)
    {
        // La variante async ya funcionaba; queda como control de que las dos se comportan igual.
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Mediador().Send(new Peticion(sincrono)));
        Assert.Equal("regla rota", ex.Message);
    }

    private sealed record OtraPeticion : IRequest<Respuesta>;

    private sealed class HandlerQueResponde : IRequestHandler<OtraPeticion, Respuesta>
    {
        public Task<Respuesta> Handle(OtraPeticion request, CancellationToken cancellationToken) =>
            Task.FromResult(new Respuesta());
    }

    private sealed class BehaviorQueLanza : IPipelineBehavior<OtraPeticion, Respuesta>
    {
        public Task<Respuesta> Handle(OtraPeticion request, RequestHandlerDelegate<Respuesta> next, CancellationToken cancellationToken) =>
            throw new BusinessRuleException("regla del behavior");
    }

    [Fact]
    public async Task La_excepcion_de_un_behavior_tambien_sale_sin_envolver()
    {
        // El behavior se invoca por reflexion igual que el handler: mismo problema, otro punto.
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMediator();
        services.AddScoped<IRequestHandler<OtraPeticion, Respuesta>, HandlerQueResponde>();
        services.AddScoped<IPipelineBehavior<OtraPeticion, Respuesta>, BehaviorQueLanza>();
        var mediator = services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IMediator>();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => mediator.Send(new OtraPeticion()));
        Assert.Equal("regla del behavior", ex.Message);
    }

    [Fact]
    public async Task Una_regla_rota_sincrona_responde_400_y_no_500()
    {
        // El caso de punta a punta tal como se vio: handler sincrono -> ProblemDetails.
        var mediator = Mediador();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ProblemDetailsMiddleware(
            async _ => await mediator.Send(new Peticion(Sincrono: true)),
            NullLogger<ProblemDetailsMiddleware>.Instance);

        await middleware.Invoke(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }
}

/// <summary>
/// Las respuestas de error tienen que declararse <c>application/problem+json</c> (RFC 9457).
/// <c>WriteAsJsonAsync</c> sin <c>contentType</c> pisaba el tipo con <c>application/json</c>, aunque el
/// middleware lo hubiera fijado antes.
/// </summary>
public sealed class ProblemDetailsContentTypeTests
{
    [Fact]
    public async Task Una_excepcion_responde_con_content_type_de_problem_details()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ProblemDetailsMiddleware(
            _ => throw new InvalidOperationException("detalle interno"),
            NullLogger<ProblemDetailsMiddleware>.Instance);

        await middleware.Invoke(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);

        // Y el detalle interno no llega al cliente.
        context.Response.Body.Position = 0;
        var cuerpo = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.DoesNotContain("detalle interno", cuerpo.RootElement.GetRawText());
    }
}

/// <summary>
/// El id de correlacion que manda el cliente acaba en los logs y en la respuesta. Tiene que sanearse:
/// sin eso, un cliente mete saltos de linea en los logs (log forging) o los infla.
/// </summary>
public sealed class CorrelationIdTests
{
    private static async Task<string> IdResultante(string? header)
    {
        var context = new DefaultHttpContext();
        if (header is not null)
            context.Request.Headers[CorrelationIdMiddleware.HeaderName] = header;

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);
        await middleware.Invoke(context);

        return context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
    }

    [Fact]
    public async Task Un_id_valido_se_conserva()
    {
        Assert.Equal("pedido-42_a.b:c", await IdResultante("pedido-42_a.b:c"));
    }

    [Fact]
    public async Task Los_saltos_de_linea_no_llegan_al_log()
    {
        var id = await IdResultante("abc\r\n[FTL] evento falso");

        Assert.DoesNotContain('\r', id);
        Assert.DoesNotContain('\n', id);
        Assert.DoesNotContain(' ', id);
        Assert.Equal("abcFTLeventofalso", id);
    }

    [Fact]
    public async Task Un_id_enorme_se_recorta()
    {
        var id = await IdResultante(new string('a', 5000));

        Assert.Equal(CorrelationIdMiddleware.MaxLength, id.Length);
    }

    [Fact]
    public async Task Si_no_queda_nada_valido_se_genera_uno()
    {
        var id = await IdResultante("\r\n<>{}");

        Assert.Equal(32, id.Length);
        Assert.True(Guid.TryParseExact(id, "N", out _));
    }
}
