using System.Diagnostics;
using System.Text.Json;
using Common.Exceptions;
using Common.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Common.Web
{
    /// <summary>
    /// Middleware que convierte las excepciones en respuestas con el envelope de
    /// <see cref="ResultViewModel{T}"/> (<c>{ data, isSuccess, message, utcTimeStamp }</c>, camelCase).
    /// Se agrega con <see cref="ApplicationBuilderEx.UseEnvelopeExceptionHandling"/>.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Excepcion</term><description>Status / log</description></listheader>
    /// <item><term><see cref="ResourceNotFoundException"/></term><description>404, Warning</description></item>
    /// <item><term><see cref="BusinessRuleViolationException"/></term><description>409, Warning</description></item>
    /// <item><term><see cref="DataIntegrityException"/></term><description>422, Warning</description></item>
    /// <item><term><see cref="BusinessRuleException"/> (y otras subclases)</term><description>400, Warning</description></item>
    /// <item><term><see cref="OperationCanceledException"/> con <c>RequestAborted</c> cancelado</term><description>499 sin cuerpo, Information (no es un error)</description></item>
    /// <item><term>Cualquier otra</term><description>500 con mensaje generico, Error con la excepcion completa</description></item>
    /// </list>
    /// En los status de negocio el <c>message</c> es el de la excepcion. En el 500 es siempre
    /// <see cref="GenericErrorMessage"/>: ni el mensaje interno ni la traza salen al cliente. Si la
    /// respuesta ya empezo a escribirse no se toca (ya no se puede cambiar el status): solo se registra.
    /// Todos los logs llevan <c>TraceId</c> y <c>CorrelationId</c> (de <see cref="CorrelationIdMiddleware"/>
    /// si esta en el pipeline). Registralo antes que lo que quieras proteger.
    /// </remarks>
    public sealed class EnvelopeExceptionMiddleware
    {
        /// <summary>Mensaje que recibe el cliente ante una excepcion inesperada (500).</summary>
        public const string GenericErrorMessage = "Ocurrio un error inesperado.";

        /// <summary>Status no estandar que usa nginx para "el cliente cerro la conexion".</summary>
        public const int ClientClosedRequestStatusCode = 499;

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly RequestDelegate _next;
        private readonly ILogger<EnvelopeExceptionMiddleware> _logger;

        /// <summary>Crea el middleware.</summary>
        /// <param name="next">Siguiente middleware.</param>
        /// <param name="logger">Logger donde se registran las excepciones.</param>
        public EnvelopeExceptionMiddleware(RequestDelegate next, ILogger<EnvelopeExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        /// <summary>Ejecuta el resto del pipeline y, si lanza, responde con el envelope.</summary>
        /// <param name="context">Contexto HTTP de la peticion.</param>
        /// <returns>Tarea que termina cuando se escribe la respuesta.</returns>
        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Peticion cancelada por el cliente. TraceId={TraceId} CorrelationId={CorrelationId}",
                    TraceId(context), CorrelationId(context));
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = ClientClosedRequestStatusCode;
                }
            }
            catch (Exception ex)
            {
                var (status, message) = Map(ex);
                var traceId = TraceId(context);
                var correlationId = CorrelationId(context);

                if (status == StatusCodes.Status500InternalServerError)
                {
                    _logger.LogError(ex, "Excepcion no controlada. TraceId={TraceId} CorrelationId={CorrelationId}", traceId, correlationId);
                }
                else
                {
                    _logger.LogWarning(ex, "Excepcion de negocio {Status}: {Message}. TraceId={TraceId} CorrelationId={CorrelationId}", status, ex.Message, traceId, correlationId);
                }

                if (context.Response.HasStarted)
                {
                    _logger.LogWarning(
                        "La respuesta ya empezo; no se escribe el envelope de error. TraceId={TraceId} CorrelationId={CorrelationId}",
                        traceId, correlationId);
                    return;
                }

                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json; charset=utf-8";
                var body = new ResultViewModel<object>().Fail(message, status);
                await context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions), context.RequestAborted).ConfigureAwait(false);
            }
        }

        private static (int Status, string Message) Map(Exception ex) => ex switch
        {
            ResourceNotFoundException => (StatusCodes.Status404NotFound, ex.Message),
            DataIntegrityException => (StatusCodes.Status422UnprocessableEntity, ex.Message),
            BusinessRuleViolationException => (StatusCodes.Status409Conflict, ex.Message),
            BusinessRuleException => (StatusCodes.Status400BadRequest, ex.Message),
            _ => (StatusCodes.Status500InternalServerError, GenericErrorMessage),
        };

        private static string TraceId(HttpContext context) =>
            Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        private static string? CorrelationId(HttpContext context) =>
            context.Items.TryGetValue(CorrelationIdMiddleware.HeaderName, out var value) ? value as string : null;
    }
}
