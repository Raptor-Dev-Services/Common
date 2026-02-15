using Serilog;
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
                    var application = section.GetSection("Application").Value ?? string.Empty;
                    var version = section.GetSection("Version").Value ?? string.Empty;

                    var loggerConfig = new LoggerConfiguration()
                        .MinimumLevel.Verbose()
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
