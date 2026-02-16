using Serilog;
using Serilog.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Logging
{
    public static class ServiceCollectionEx
    {
        public static IServiceCollection AddLoggingServices(this IServiceCollection services, IConfiguration configuration)
        {
            return services
                .AddLogging(logging =>
                {
                    var section = configuration.GetSection("CustomLogging");
                    var seqUri = section.GetSection("SeqUri").Value;
                    var application =
                        section.GetSection("Application").Value ??
                        section.GetSection("Project").Value ??
                        string.Empty;
                    var version =
                        section.GetSection("Version").Value ??
                        section.GetSection("ServiceVersion").Value ??
                        string.Empty;
                    var logEventLevelRaw = section.GetSection("LogEventLevel").Value ?? "Information";
                    if (!Enum.TryParse<LogEventLevel>(logEventLevelRaw, true, out var minimumLevel))
                    {
                        minimumLevel = LogEventLevel.Information;
                    }

                    var loggerConfig = new LoggerConfiguration()
                        .MinimumLevel.Is(minimumLevel)
                        .Enrich.FromLogContext()
                        .Enrich.WithProperty("Project", section.GetSection("Project").Value)
                        .Enrich.WithProperty("MachineName", Environment.MachineName)
                        .Enrich.WithProperty("Environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? string.Empty)
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
