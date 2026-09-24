using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Messaging
{
    /// <summary>
    /// Implementacion de <see cref="IMediator"/> que resuelve handlers y behaviors del contenedor de
    /// dependencias por reflexion.
    /// </summary>
    public sealed class Mediator : IMediator
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// Crea el mediador.
        /// </summary>
        /// <param name="serviceProvider">Proveedor del que se resuelven handlers y behaviors (el del scope actual).</param>
        public Mediator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Envia la peticion al <see cref="IRequestHandler{TRequest, TResponse}"/> registrado para su tipo
        /// en tiempo de ejecucion, envuelto por todos los <see cref="IPipelineBehavior{TRequest, TResponse}"/> registrados.
        /// </summary>
        /// <remarks>
        /// El primer behavior registrado es el mas externo: se ejecuta primero y ve la respuesta al final.
        /// Una excepcion del handler o de un behavior sale tal cual se lanzo, sea el metodo async o no:
        /// la invocacion por reflexion no la envuelve en <see cref="System.Reflection.TargetInvocationException"/>.
        /// </remarks>
        /// <typeparam name="TResponse">Tipo de la respuesta.</typeparam>
        /// <param name="request">Peticion a enviar.</param>
        /// <param name="cancellationToken">Token que se pasa a behaviors y handler.</param>
        /// <returns>La respuesta del handler (tras los behaviors).</returns>
        /// <exception cref="ArgumentNullException">Si <paramref name="request"/> es <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">
        /// Si no hay handler registrado para la peticion, o si un handler o behavior no devuelve <c>Task&lt;TResponse&gt;</c>.
        /// </exception>
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));

            var requestType = request.GetType();
            var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
            var handler = _serviceProvider.GetService(handlerType);

            if (handler is null)
            {
                throw new InvalidOperationException($"No handler registered for request {requestType.FullName}.");
            }

            var requestHandleMethod = handlerType.GetMethod("Handle")
                ?? throw new InvalidOperationException($"Handle method not found for handler {handlerType.FullName}.");

            RequestHandlerDelegate<TResponse> handlerDelegate = () => InvokeRequestHandler<TResponse>(requestHandleMethod, handler, request, cancellationToken);

            var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
            var behaviors = _serviceProvider.GetServices(behaviorType).Cast<object>().Reverse().ToList();
            var behaviorHandleMethod = behaviorType.GetMethod("Handle")
                ?? throw new InvalidOperationException($"Handle method not found for behavior {behaviorType.FullName}.");

            foreach (var behavior in behaviors)
            {
                var next = handlerDelegate;
                handlerDelegate = () => InvokePipelineBehavior<TResponse>(behaviorHandleMethod, behavior, request, next, cancellationToken);
            }

            return handlerDelegate();
        }

        /// <summary>
        /// Entrega la notificacion a todos los <see cref="INotificationHandler{TNotification}"/> registrados
        /// para <typeparamref name="TNotification"/> y espera a que terminen (<see cref="Task.WhenAll(IEnumerable{Task})"/>).
        /// </summary>
        /// <remarks>
        /// Los handlers corren en paralelo, sin orden garantizado. Se buscan por el tipo estatico
        /// <typeparamref name="TNotification"/>, no por el tipo en tiempo de ejecucion. Sin handlers, no hace
        /// nada. Si alguno falla, la tarea devuelta falla.
        /// </remarks>
        /// <typeparam name="TNotification">Tipo de la notificacion.</typeparam>
        /// <param name="notification">Notificacion a publicar.</param>
        /// <param name="cancellationToken">Token que se pasa a cada handler.</param>
        /// <returns>Tarea que termina cuando todos los handlers terminan.</returns>
        /// <exception cref="ArgumentNullException">Si <paramref name="notification"/> es <c>null</c>.</exception>
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            if (notification is null) throw new ArgumentNullException(nameof(notification));

            var handlers = _serviceProvider.GetServices<INotificationHandler<TNotification>>();
            var tasks = handlers.Select(h => h.Handle(notification, cancellationToken));
            return Task.WhenAll(tasks);
        }

        // Sin esto, MethodInfo.Invoke envuelve en TargetInvocationException lo que el handler lance
        // antes de su primer await: una BusinessRuleException de un handler sincrono llegaba
        // disfrazada, el pipeline la registraba como Critical y UseCoreProblemDetails respondia 500
        // en vez de 400. Con DoNotWrapExceptions sale tal cual, igual que desde un handler async.
        private const BindingFlags NoEnvolver = BindingFlags.DoNotWrapExceptions;

        private static Task<TResponse> InvokeRequestHandler<TResponse>(MethodInfo handleMethod, object handler, object request, CancellationToken cancellationToken)
        {
            var responseTask = handleMethod.Invoke(handler, NoEnvolver, null, [request, cancellationToken], null) as Task<TResponse>;
            if (responseTask is null)
            {
                throw new InvalidOperationException($"Handler {handler.GetType().FullName} did not return Task<{typeof(TResponse).Name}>.");
            }

            return responseTask;
        }

        private static Task<TResponse> InvokePipelineBehavior<TResponse>(
            MethodInfo handleMethod,
            object behavior,
            object request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            var responseTask = handleMethod.Invoke(behavior, NoEnvolver, null, [request, next, cancellationToken], null) as Task<TResponse>;
            if (responseTask is null)
            {
                throw new InvalidOperationException($"Behavior {behavior.GetType().FullName} did not return Task<{typeof(TResponse).Name}>.");
            }

            return responseTask;
        }
    }
}
