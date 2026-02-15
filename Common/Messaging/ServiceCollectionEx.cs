using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Messaging
{
    public static class ServiceCollectionEx
    {
        public static IServiceCollection AddMediator(this IServiceCollection services, params Assembly[] assemblies)
        {
            services.AddScoped<IMediator, Mediator>();
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(InteractorPipeline<,>));

            if (assemblies is null || assemblies.Length == 0)
            {
                return services;
            }

            foreach (var type in assemblies.SelectMany(a => a.DefinedTypes))
            {
                if (type.IsAbstract || type.IsInterface)
                {
                    continue;
                }

                foreach (var iface in type.ImplementedInterfaces)
                {
                    if (!iface.IsGenericType)
                    {
                        continue;
                    }

                    var def = iface.GetGenericTypeDefinition();
                    if (def == typeof(IRequestHandler<,>) || def == typeof(INotificationHandler<>))
                    {
                        services.AddScoped(iface, type);
                    }
                }
            }

            return services;
        }
    }
}
