using Serilog;
using Serilog.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Common.Logging
{
    /// <summary>
    /// Registro del logging con Serilog.
    /// </summary>
    public static class ServiceCollectionEx
    {
        /// <summary>
        /// Reemplaza los proveedores de log por Serilog, configurado desde la seccion <c>CustomLogging</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Nivel minimo: <c>CustomLogging:LogEventLevel</c> (nombre de nivel de Serilog, sin distinguir
        /// mayusculas). Si falta o no es valido, Verbose. Si la variable de entorno
        /// <c>ASPNETCORE_ENVIRONMENT</c> es Development, el nivel baja al menos a Debug.
        /// </para>
        /// <para>
        /// Escribe a consola y a la salida de depuracion; a Seq solo si <c>CustomLogging:SeqUri</c> tiene valor.
        /// Enriquece con el contexto de log, el tenant actual (<see cref="TenantLogEventEnricher"/>) y las
        /// propiedades Project, Application y Version (de <c>CustomLogging</c>), MachineName y Environment.
        /// </para>
        /// <para>
        /// El filtro de nivel de Microsoft.Extensions.Logging (seccion <c>Logging</c>) se sigue aplicando
        /// antes de llegar a Serilog.
        /// </para>
        /// </remarks>
        /// <param name="services">Coleccion de servicios.</param>
        /// <param name="configuration">Configuracion de la que se lee <c>CustomLogging</c>.</param>
        /// <returns>La misma coleccion, para encadenar.</returns>
        public static IServiceCollection AddLoggingServices(this IServiceCollection services, IConfiguration configuration)
        {
            return services
                .AddLogging(logging =>
                {
                    logging.ClearProviders();
                    var section = configuration.GetSection("CustomLogging");
                    var seqUri = section.GetSection("SeqUri").Value;
                    var levelText = section.GetSection("LogEventLevel").Value;
                    var minimumLevel = LogEventLevel.Verbose;
                    if (!string.IsNullOrWhiteSpace(levelText) &&
                        Enum.TryParse(levelText, ignoreCase: true, out LogEventLevel parsedLevel))
                    {
                        minimumLevel = parsedLevel;
                    }
                    var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                    if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase) &&
                        minimumLevel > LogEventLevel.Debug)
                    {
                        minimumLevel = LogEventLevel.Debug;
                    }
                    var application = section.GetSection("Application").Value ?? string.Empty;
                    var version = section.GetSection("Version").Value ?? string.Empty;

                    var loggerConfig = new LoggerConfiguration()
                        .MinimumLevel.Is(minimumLevel)
                        .Enrich.FromLogContext()
                        .Enrich.With<TenantLogEventEnricher>()
                        .Enrich.WithProperty("Project", section.GetSection("Project").Value)
                        .Enrich.WithProperty("MachineName", Environment.MachineName)
                        .Enrich.WithProperty("Environment", environment ?? string.Empty)
                        .Enrich.WithProperty("Application", application)
                        .Enrich.WithProperty("Version", version)
                        .WriteTo.Console()
                        .WriteTo.Debug();

                    if (!string.IsNullOrWhiteSpace(seqUri))
                    {
                        loggerConfig = loggerConfig.WriteTo.Seq(seqUri);
                    }

                    var serilogLogger = loggerConfig.CreateLogger();
                    logging.AddSerilog(logger: serilogLogger, dispose: true);
                });
        }
    }
}


