using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Common.Http
{
    public static class HttpClientBuilderEx
    {
        public static IHttpClientBuilder AddCoreResilience(
            this IHttpClientBuilder builder,
            IConfigurationSection? section = null)
        {
            builder.AddStandardResilienceHandler(options =>
            {
                if (section is null)
                {
                    return;
                }

                section.Bind(options);
            });

            return builder;
        }
    }
}
