using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Common.Observability
{
    public static class ServiceCollectionEx
    {
        public const string ApiMeterName = "CMSA.Api";

        public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
        {
            var section = configuration.GetSection("Observability");
            var serviceName = section.GetSection("ServiceName").Value ?? "UnknownService";
            var serviceVersion = section.GetSection("ServiceVersion").Value ?? string.Empty;
            var otlpEndpoint = section.GetSection("OtlpEndpoint").Value ?? string.Empty;
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? string.Empty;

            services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(serviceName, serviceVersion: serviceVersion)
                    .AddAttributes(new KeyValuePair<string, object>[]
                    {
                        new("deployment.environment", environment),
                        new("host.name", Environment.MachineName),
                        new("service.instance.id", Environment.ProcessId.ToString())
                    }))
                .WithTracing(tracing =>
                {
                    tracing
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation();

                    if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    {
                        tracing.AddOtlpExporter(opt => opt.Endpoint = new Uri(otlpEndpoint));
                    }
                })
                .WithMetrics(metrics =>
                {
                    metrics
                        .AddMeter(ApiMeterName)
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation()
                        .AddPrometheusExporter();

                    if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    {
                        metrics.AddOtlpExporter(opt => opt.Endpoint = new Uri(otlpEndpoint));
                    }
                });

            return services;
        }
    }
}

