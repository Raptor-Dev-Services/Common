using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Common.Observability
{
    /// <summary>
    /// Registro de OpenTelemetry (trazas y metricas).
    /// </summary>
    public static class ServiceCollectionEx
    {
        /// <summary>
        /// Registra trazas y metricas de OpenTelemetry con exportacion OTLP, configuradas desde la seccion
        /// <c>Observability</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Recurso: <c>ServiceName</c> (por defecto <c>"UnknownService"</c>), <c>ServiceVersion</c>, y los
        /// atributos deployment.environment (variable <c>ASPNETCORE_ENVIRONMENT</c>), host.name y
        /// service.instance.id (id del proceso).
        /// </para>
        /// <para>
        /// Trazas: ASP.NET Core y HttpClient, exportadas a <c>OtlpEndpoint</c> con el protocolo por defecto
        /// del exportador. Metricas: el medidor <paramref name="meterName"/>, ASP.NET Core, HttpClient y
        /// runtime. Si hay <c>MetricsOtlpEndpoint</c>, las metricas van ahi por HTTP/protobuf; si no, a
        /// <c>OtlpEndpoint</c> con el protocolo por defecto.
        /// </para>
        /// <para>
        /// Sin endpoints no se exporta nada y no falla: la instrumentacion sigue activa.
        /// </para>
        /// </remarks>
        /// <param name="services">Coleccion de servicios.</param>
        /// <param name="configuration">Configuracion de la que se lee <c>Observability</c>.</param>
        /// <param name="meterName">Nombre del <see cref="System.Diagnostics.Metrics.Meter"/> propio de la aplicacion.</param>
        /// <returns>La misma coleccion, para encadenar.</returns>
        /// <exception cref="UriFormatException">Al construir los exportadores, si un endpoint configurado no es una URI valida.</exception>
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

