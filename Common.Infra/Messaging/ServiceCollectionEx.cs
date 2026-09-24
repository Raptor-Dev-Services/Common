using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Messaging
{
    /// <summary>
    /// Registro del mediador.
    /// </summary>
    public static class ServiceCollectionEx
    {
        /// <summary>
        /// Registra el mediador y, opcionalmente, los handlers de los ensamblados dados.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Siempre registra, Scoped, <see cref="IMediator"/> (<see cref="Mediator"/>) e
        /// <see cref="InteractorPipeline{TRequest, TResponse}"/> como <see cref="IPipelineBehavior{TRequest, TResponse}"/>.
        /// Sin ensamblados no registra nada mas.
        /// </para>
        /// <para>
        /// Con ensamblados, recorre sus tipos concretos y registra Scoped cada uno por cada
        /// <see cref="IRequestHandler{TRequest, TResponse}"/> e <see cref="INotificationHandler{TNotification}"/>
        /// que implementa. Solo se registra por esas interfaces: el tipo concreto no queda registrado. No
        /// evita duplicados: llamar dos veces registra el pipeline (y los handlers) dos veces.
        /// </para>
        /// </remarks>
        /// <param name="services">Coleccion de servicios.</param>
        /// <param name="assemblies">Ensamblados donde buscar handlers y presenters.</param>
        /// <returns>La misma coleccion, para encadenar.</returns>
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
