using Serilog;
using Serilog.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Common.Logging
{
    public static class ServiceCollectionEx
    {
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


