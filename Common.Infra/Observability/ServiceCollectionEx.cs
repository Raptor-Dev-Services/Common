using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Common.Observability
{
    public static class ServiceCollectionEx
    {
        public static IServiceCollection AddObservability(
            this IServiceCollection services,
            IConfiguration configuration,
            string meterName)
        {
            var section = configuration.GetSection("Observability");
            var serviceName = section.GetSection("ServiceName").Value ?? "UnknownService";
            var serviceVersion = section.GetSection("ServiceVersion").Value ?? string.Empty;
            var otlpEndpoint = section.GetSection("OtlpEndpoint").Value ?? string.Empty;
            var metricsOtlpEndpoint = section.GetSection("MetricsOtlpEndpoint").Value ?? string.Empty;
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
                        .AddMeter(meterName)
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation();

                    // Las metricas pueden salir a un destino distinto del de las trazas: el
                    // receptor OTLP de Prometheus habla HTTP, no gRPC. Si hay endpoint propio
                    // de metricas manda ese; si no, se usa el general.
                    var metricsEndpoint = !string.IsNullOrWhiteSpace(metricsOtlpEndpoint)
                        ? metricsOtlpEndpoint
                        : otlpEndpoint;

                    if (!string.IsNullOrWhiteSpace(metricsEndpoint))
                    {
                        metrics.AddOtlpExporter(opt =>
                        {
                            opt.Endpoint = new Uri(metricsEndpoint);

                            if (!string.IsNullOrWhiteSpace(metricsOtlpEndpoint))
                            {
                                opt.Protocol = OtlpExportProtocol.HttpProtobuf;
                            }
                        });
                    }
                });

            return services;
        }
    }
}

