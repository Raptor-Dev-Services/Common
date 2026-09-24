using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Common.Exceptions;
using Common.MultiTenancy;

namespace Common.Web
{
    internal static class ProblemJson
    {
        internal const string ContentType = "application/problem+json";
    }

    /// <summary>
    /// Middlewares HTTP de la libreria.
    /// </summary>
    public static class ApplicationBuilderEx
    {
        /// <summary>
        /// Agrega <see cref="CorrelationIdMiddleware"/>: reutiliza o genera el <c>X-Correlation-Id</c> de
        /// cada peticion y lo pone en la respuesta y en el scope de log.
        /// </summary>
        /// <remarks>
        /// El valor que manda el cliente se sanea antes de usarlo. Ver remarks de
        /// <see cref="CorrelationIdMiddleware"/>.
        /// </remarks>
        /// <param name="app">Constructor de la aplicacion.</param>
        /// <returns>El mismo constructor, para encadenar.</returns>
        public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
        {
            return app.UseMiddleware<CorrelationIdMiddleware>();
        }

        /// <summary>
        /// Agrega <see cref="TenantResolutionMiddleware"/>: resuelve el tenant de cada peticion y lo fija
        /// para el resto del pipeline. Requiere <c>AddMultiTenancy</c>.
        /// </summary>
        /// <remarks>
        /// ADVERTENCIA: con la configuracion por defecto el tenant sale de un header que manda el cliente.
        /// Ver remarks de <see cref="TenantResolutionMiddleware"/>.
        /// </remarks>
        /// <param name="app">Constructor de la aplicacion.</param>
        /// <returns>El mismo constructor, para encadenar.</returns>
        public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
        {
            return app.UseMiddleware<TenantResolutionMiddleware>();
        }

        /// <summary>
        /// Agrega <see cref="ProblemDetailsMiddleware"/>: convierte las excepciones no controladas en
        /// respuestas <c>application/problem+json</c>. Conviene ponerlo al principio del pipeline para
        /// que cubra a los demas middlewares.
        /// </summary>
        /// <param name="app">Constructor de la aplicacion.</param>
        /// <returns>El mismo constructor, para encadenar.</returns>
        public static IApplicationBuilder UseCoreProblemDetails(this IApplicationBuilder app)
        {
            return app.UseMiddleware<ProblemDetailsMiddleware>();
        }
    }

    /// <summary>
    /// Middleware que resuelve el tenant de la peticion con <see cref="ITenantResolver"/> y lo fija en
    /// <see cref="ITenantContextAccessor"/>, en <see cref="HttpContext.Items"/>, en el header de
    /// respuesta, en la etiqueta <c>tenant.id</c> de la actividad y en el scope de log.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Con el resolvedor por defecto el orden es header, query string (si esta habilitado), subdominio
    /// (host de 3 o mas segmentos, no ignorado) y <see cref="MultiTenantOptions.DefaultTenantId"/>.
    /// Respuestas de rechazo, con cuerpo <see cref="ProblemDetails"/>: 400 si no hay tenant y
    /// <see cref="MultiTenantOptions.RequireTenant"/>; 403 si el tenant esta deshabilitado; 403 si no
    /// esta registrado, <see cref="MultiTenantOptions.RejectUnknownTenants"/> esta activo y hay al menos
    /// un tenant configurado. Sin tenant y sin <c>RequireTenant</c>, la peticion sigue sin tenant.
    /// </para>
    /// <para>
    /// ADVERTENCIA: resolver el tenant desde un header o un query string que manda el cliente no
    /// autentica nada: cualquiera puede pedir el tenant que quiera. Detras de autenticacion, el tenant
    /// deberia salir de un claim verificado; si no, hay que comprobar despues que el usuario pertenece
    /// al tenant resuelto.
    /// </para>
    /// </remarks>
    public sealed class TenantResolutionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ITenantResolver _tenantResolver;
        private readonly ITenantContextAccessor _tenantContextAccessor;
        private readonly ITenantConfigurationStore _tenantConfigurationStore;
        private readonly IOptions<MultiTenantOptions> _options;
        private readonly ILogger<TenantResolutionMiddleware> _logger;

        /// <summary>
        /// Crea el middleware.
        /// </summary>
        /// <param name="next">Siguiente middleware.</param>
        /// <param name="tenantResolver">Resolvedor del tenant.</param>
        /// <param name="tenantContextAccessor">Accesor donde se fija el tenant.</param>
        /// <param name="tenantConfigurationStore">Configuracion de los tenants conocidos.</param>
        /// <param name="options">Opciones de multi-tenancy.</param>
        /// <param name="logger">Logger en el que se abre el scope con <c>TenantId</c>.</param>
        public TenantResolutionMiddleware(
            RequestDelegate next,
            ITenantResolver tenantResolver,
            ITenantContextAccessor tenantContextAccessor,
            ITenantConfigurationStore tenantConfigurationStore,
            IOptions<MultiTenantOptions> options,
            ILogger<TenantResolutionMiddleware> logger)
        {
            _next = next;
            _tenantResolver = tenantResolver;
            _tenantContextAccessor = tenantContextAccessor;
            _tenantConfigurationStore = tenantConfigurationStore;
            _options = options;
            _logger = logger;
        }

        /// <summary>
        /// Resuelve y valida el tenant, o corta la peticion con 400/403. Al terminar deja el accesor sin tenant.
        /// </summary>
        /// <param name="context">Contexto HTTP de la peticion.</param>
        /// <returns>Tarea que termina cuando termina el resto del pipeline.</returns>
        public async Task Invoke(HttpContext context)
        {
            var tenantId = await _tenantResolver.ResolveTenantIdAsync(context, context.RequestAborted).ConfigureAwait(false);
            var options = _options.Value;

            if (string.IsNullOrWhiteSpace(tenantId) && options.RequireTenant)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Tenant not resolved",
                    Detail = "Could not resolve tenant identifier for this request.",
                    Instance = context.Request.Path
                }, options: null, contentType: ProblemJson.ContentType).ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            if (_tenantConfigurationStore.TryGetTenant(tenantId, out var tenantOptions))
            {
                if (!tenantOptions.IsEnabled)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new ProblemDetails
                    {
                        Status = StatusCodes.Status403Forbidden,
                        Title = "Tenant disabled",
                        Detail = $"Tenant '{tenantId}' is disabled.",
                        Instance = context.Request.Path
                    }, options: null, contentType: ProblemJson.ContentType).ConfigureAwait(false);
                    return;
                }
            }
            else if (options.RejectUnknownTenants && options.Tenants.Count > 0)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Unknown tenant",
                    Detail = $"Tenant '{tenantId}' is not registered.",
                    Instance = context.Request.Path
                }, options: null, contentType: ProblemJson.ContentType).ConfigureAwait(false);
                return;
            }

            var tenantContext = new TenantContext(tenantId);
            _tenantContextAccessor.Current = tenantContext;
            context.Items[nameof(TenantContext)] = tenantContext;
            context.Response.Headers[options.TenantResponseHeaderName] = tenantId;
            Activity.Current?.SetTag("tenant.id", tenantId);

            try
            {
                using (_logger.BeginScope(new Dictionary<string, object?> { ["TenantId"] = tenantId }))
                {
                    await _next(context).ConfigureAwait(false);
                }
            }
            finally
            {
                _tenantContextAccessor.Current = null;
            }
        }
    }

    /// <summary>
    /// Middleware que asigna un id de correlacion a cada peticion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Si la peticion trae el header <see cref="HeaderName"/>, lo reutiliza despues de sanearlo con
    /// <see cref="Sanitize"/>; si no trae o no queda nada valido, genera un GUID de 32 caracteres
    /// hexadecimales. El id se escribe en el header de respuesta, en
    /// <see cref="HttpContext.Items"/> (clave <see cref="HeaderName"/>), en la etiqueta
    /// <c>correlation_id</c> de la actividad y en el scope de log como <c>CorrelationId</c>.
    /// </para>
    /// <para>
    /// El header es entrada del cliente: se conservan solo letras, digitos y <c>- _ . :</c>, hasta
    /// <see cref="MaxLength"/> caracteres. Asi un cliente no puede inyectar saltos de linea en los logs
    /// (log forging) ni inflarlos.
    /// </para>
    /// </remarks>
    public sealed class CorrelationIdMiddleware
    {
        /// <summary>
        /// Nombre del header: <c>"X-Correlation-Id"</c>.
        /// </summary>
        public const string HeaderName = "X-Correlation-Id";

        /// <summary>Longitud maxima que se conserva del id que manda el cliente.</summary>
        public const int MaxLength = 128;
        private readonly RequestDelegate _next;
        private readonly ILogger<CorrelationIdMiddleware> _logger;

        /// <summary>
        /// Crea el middleware.
        /// </summary>
        /// <param name="next">Siguiente middleware.</param>
        /// <param name="logger">Logger en el que se abre el scope con <c>CorrelationId</c>.</param>
        public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        /// <summary>
        /// Asigna el id de correlacion y ejecuta el resto del pipeline dentro de su scope de log.
        /// </summary>
        /// <param name="context">Contexto HTTP de la peticion.</param>
        /// <returns>Tarea que termina cuando termina el resto del pipeline.</returns>
        public async Task Invoke(HttpContext context)
        {
            // El header es entrada del cliente y termina en los logs y en la respuesta. Sin sanear, un
            // cliente podia meter saltos de linea (log forging) o inflar cada evento. Se conservan solo
            // letras, digitos y - _ . : hasta MaxLength; si no queda nada, se genera uno nuevo.
            var correlationId = Sanitize(context.Request.Headers[HeaderName].FirstOrDefault());
            if (correlationId.Length == 0)
            {
                correlationId = Guid.NewGuid().ToString("N");
            }

            context.Response.Headers[HeaderName] = correlationId;
            context.Items[HeaderName] = correlationId;
            Activity.Current?.SetTag("correlation_id", correlationId);

            using (_logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId }))
            {
                await _next(context).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Deja solo letras, digitos y <c>- _ . :</c> del valor recibido, recortado a <see cref="MaxLength"/>.
        /// </summary>
        /// <param name="value">Valor del header, tal como llego.</param>
        /// <returns>El valor saneado; cadena vacia si no queda nada.</returns>
        public static string Sanitize(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var buffer = new System.Text.StringBuilder(Math.Min(value.Length, MaxLength));
            foreach (var c in value)
            {
                if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':')
                {
                    buffer.Append(c);
                    if (buffer.Length == MaxLength)
                    {
                        break;
                    }
                }
            }

            return buffer.ToString();
        }
    }

    /// <summary>
    /// Middleware que convierte las excepciones no controladas en respuestas
    /// <c>application/problem+json</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="BusinessRuleException"/> produce 400 con su mensaje como detalle (se registra como
    /// Warning). Cualquier otra excepcion produce 500 con el detalle generico "An unexpected error
    /// occurred" (se registra como Error). El cuerpo incluye <c>traceId</c> (el de la actividad actual o
    /// el identificador de la peticion) y <c>tenantId</c> si se resolvio tenant.
    /// </remarks>
    public sealed class ProblemDetailsMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ProblemDetailsMiddleware> _logger;

        /// <summary>
        /// Crea el middleware.
        /// </summary>
        /// <param name="next">Siguiente middleware.</param>
        /// <param name="logger">Logger donde se registran las excepciones.</param>
        public ProblemDetailsMiddleware(RequestDelegate next, ILogger<ProblemDetailsMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        /// <summary>
        /// Ejecuta el resto del pipeline y, si lanza, escribe el ProblemDetails correspondiente.
        /// </summary>
        /// <param name="context">Contexto HTTP de la peticion.</param>
        /// <returns>Tarea que termina cuando se escribe la respuesta.</returns>
        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context).ConfigureAwait(false);
            }
            catch (BusinessRuleException ex)
            {
                _logger.LogWarning(ex, "Business rule violation: {Message}", ex.Message);
                await WriteProblemDetails(context, StatusCodes.Status400BadRequest, "Business rule violation", ex.Message)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception");
                await WriteProblemDetails(context, StatusCodes.Status500InternalServerError, "Unhandled error", "An unexpected error occurred")
                    .ConfigureAwait(false);
            }
        }

        private static Task WriteProblemDetails(HttpContext context, int statusCode, string title, string? detail)
        {
            var problem = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
                Instance = context.Request.Path
            };

            problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
            if (context.Items.TryGetValue(nameof(TenantContext), out var tenantContext) &&
                tenantContext is TenantContext value)
            {
                problem.Extensions["tenantId"] = value.TenantId;
            }

            context.Response.StatusCode = statusCode;
            // WriteAsJsonAsync sin contentType pisa el Content-Type con application/json: se pasa explicito.
            return context.Response.WriteAsJsonAsync(problem, options: null, contentType: ProblemJson.ContentType);
        }
    }
}
