using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Common.Exceptions;

namespace Common.Web
{
    public static class ApplicationBuilderEx
    {
        public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
        {
            return app.UseMiddleware<CorrelationIdMiddleware>();
        }

        public static IApplicationBuilder UseCoreProblemDetails(this IApplicationBuilder app)
        {
            return app.UseMiddleware<ProblemDetailsMiddleware>();
        }
    }

    public sealed class CorrelationIdMiddleware
    {
        public const string HeaderName = "X-Correlation-Id";
        private readonly RequestDelegate _next;
        private readonly ILogger<CorrelationIdMiddleware> _logger;

        public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(correlationId))
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
    }

    public sealed class ProblemDetailsMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ProblemDetailsMiddleware> _logger;

        public ProblemDetailsMiddleware(RequestDelegate next, ILogger<ProblemDetailsMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

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

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/problem+json";
            return context.Response.WriteAsJsonAsync(problem);
        }
    }
}
