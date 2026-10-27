using System.Text.Json;
using Common.Exceptions;
using Common.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Common.Tests;

public sealed class EnvelopeExceptionMiddlewareTests
{
    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<(JsonElement Body, ListLogger<EnvelopeExceptionMiddleware> Log)> Run(
        DefaultHttpContext context, RequestDelegate next)
    {
        var log = new ListLogger<EnvelopeExceptionMiddleware>();
        await new EnvelopeExceptionMiddleware(next, log).Invoke(context);
        context.Response.Body.Position = 0;
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (text.Length == 0 ? default : JsonDocument.Parse(text).RootElement, log);
    }

    public static IEnumerable<object[]> Casos() => new[]
    {
        new object[] { new ResourceNotFoundException("No existe el folio 7."), 404 },
        new object[] { new DataIntegrityException("Falta el catalogo X."), 422 },
        new object[] { new BusinessRuleViolationException("Ya fue procesado."), 409 },
        new object[] { new BusinessRuleException("Peticion invalida."), 400 },
    };

    [Theory]
    [MemberData(nameof(Casos))]
    public async Task Cada_excepcion_de_negocio_da_su_status_y_el_envelope(Exception ex, int status)
    {
        var context = NewContext();
        var (body, log) = await Run(context, _ => throw ex);

        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        Assert.False(body.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(ex.Message, body.GetProperty("message").GetString());
        Assert.True(body.TryGetProperty("utcTimeStamp", out _));
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Excepcion_desconocida_da_500_generico_sin_filtrar_el_detalle_y_loguea_error()
    {
        var context = NewContext();
        context.TraceIdentifier = "traza-123";
        context.Items[CorrelationIdMiddleware.HeaderName] = "corr-9";
        var (body, log) = await Run(context, _ => throw new InvalidOperationException("cadena de conexion secreta"));

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal(EnvelopeExceptionMiddleware.GenericErrorMessage, body.GetProperty("message").GetString());
        Assert.DoesNotContain("secreta", body.GetRawText());
        Assert.DoesNotContain("InvalidOperation", body.GetRawText());
        var error = Assert.Single(log.Entries, e => e.Level == LogLevel.Error);
        Assert.IsType<InvalidOperationException>(error.Exception);
        Assert.Contains("corr-9", error.Message);
    }

    [Fact]
    public async Task Peticion_cancelada_por_el_cliente_no_es_500_ni_error()
    {
        using var cts = new CancellationTokenSource();
        var context = NewContext();
        context.RequestAborted = cts.Token;
        var (body, log) = await Run(context, _ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(default, body);
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Information);
    }

    [Fact]
    public async Task Cancelacion_que_no_viene_del_cliente_si_es_500()
    {
        var context = NewContext();
        var (_, log) = await Run(context, _ => throw new OperationCanceledException());

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error);
    }

    private sealed class StartedFeature : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => true;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }

    [Fact]
    public async Task Si_la_respuesta_ya_empezo_no_escribe_ni_cambia_el_status_y_solo_loguea()
    {
        var context = NewContext();
        var feature = new StartedFeature();
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(feature);
        var (_, log) = await Run(context, _ => throw new InvalidOperationException("a medias"));

        Assert.Equal(200, feature.StatusCode);
        Assert.Equal(0, feature.Body.Length);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Sin_excepcion_deja_pasar_la_respuesta()
    {
        var context = NewContext();
        var (_, log) = await Run(context, c => { c.Response.StatusCode = 204; return Task.CompletedTask; });

        Assert.Equal(204, context.Response.StatusCode);
        Assert.Empty(log.Entries);
    }
}
