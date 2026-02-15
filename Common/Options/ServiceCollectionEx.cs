using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Common.Options
{
    public static class ServiceCollectionEx
    {
        public static OptionsBuilder<TOptions> AddValidatedOptions<TOptions>(
            this IServiceCollection services,
            IConfigurationSection section)
            where TOptions : class
        {
            return services.AddOptions<TOptions>()
                .Bind(section)
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }
    }
}
