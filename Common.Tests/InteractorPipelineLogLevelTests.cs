using Common.Exceptions;
using Common.Messaging;
using Common.Results;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Common.Tests;

/// <summary>
/// Nivel de log con que <see cref="InteractorPipeline{TRequest, TResponse}"/> registra lo que lanza un
/// handler. Antes todo era Critical (y las de negocio Error): alertas falsas por cada cancelacion o 404.
/// </summary>
public sealed class InteractorPipelineLogLevelTests
{
    private sealed record Peticion : IRequest<Respuesta>;
    private sealed record Respuesta : IResponse;

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, exception));
    }

    private sealed class MediadorNulo : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
    }

    private static async Task<(Exception Lanzada, ListLogger<InteractorPipeline<Peticion, Respuesta>> Log)> Ejecutar(
        Exception ex, CancellationToken token)
    {
        var log = new ListLogger<InteractorPipeline<Peticion, Respuesta>>();
        var pipeline = new InteractorPipeline<Peticion, Respuesta>(new MediadorNulo(), log);
        var lanzada = await Assert.ThrowsAnyAsync<Exception>(() =>
            pipeline.Handle(new Peticion(), () => throw ex, token));
        return (lanzada, log);
    }

    private static IEnumerable<(LogLevel Level, Exception? Exception)> ConExcepcion(ListLogger<InteractorPipeline<Peticion, Respuesta>> log) =>
        log.Entries.Where(e => e.Exception is not null);

    [Fact]
    public async Task Cancelacion_de_la_peticion_es_Information_y_se_relanza()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ex = new OperationCanceledException(cts.Token);

        var (lanzada, log) = await Ejecutar(ex, cts.Token);

        Assert.Same(ex, lanzada);
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Contains(ConExcepcion(log), e => e.Level == LogLevel.Information);
    }

    [Fact]
    public async Task Cancelacion_con_token_sin_cancelar_es_un_fallo_Error()
    {
        var ex = new OperationCanceledException();

        var (lanzada, log) = await Ejecutar(ex, CancellationToken.None);

        Assert.Same(ex, lanzada);
        Assert.Contains(ConExcepcion(log), e => e.Level == LogLevel.Error);
    }

    public static IEnumerable<object[]> DeNegocio() => new[]
    {
        new object[] { new BusinessRuleException("Peticion invalida.") },
        new object[] { new ResourceNotFoundException("No existe.") },
        new object[] { new DataIntegrityException("Datos rotos.") },
        new object[] { new BusinessRuleViolationException("Ya existe.") },
    };

    [Theory]
    [MemberData(nameof(DeNegocio))]
    public async Task Excepcion_de_negocio_es_Warning_y_se_relanza(Exception ex)
    {
        var (lanzada, log) = await Ejecutar(ex, CancellationToken.None);

        Assert.Same(ex, lanzada);
        Assert.Contains(ConExcepcion(log), e => e.Level == LogLevel.Warning);
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Otra_excepcion_es_Error_nunca_Critical_y_se_relanza()
    {
        var ex = new InvalidOperationException("boom");

        var (lanzada, log) = await Ejecutar(ex, CancellationToken.None);

        Assert.Same(ex, lanzada);
        Assert.Contains(ConExcepcion(log), e => e.Level == LogLevel.Error);
        Assert.DoesNotContain(log.Entries, e => e.Level == LogLevel.Critical);
    }
}

public sealed class ProblemDetailsCancelacionTests
{
    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogLevel> Levels { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Levels.Add(logLevel);
    }

    [Fact]
    public async Task Cancelacion_del_cliente_es_Information_sin_Error()
    {
        using var cts = new CancellationTokenSource();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestAborted = cts.Token };
        cts.Cancel();
        var log = new ListLogger<Common.Web.ProblemDetailsMiddleware>();

        await new Common.Web.ProblemDetailsMiddleware(_ => throw new OperationCanceledException(cts.Token), log).Invoke(context);

        Assert.DoesNotContain(log.Levels, l => l >= LogLevel.Warning);
        Assert.Contains(LogLevel.Information, log.Levels);
    }
}
