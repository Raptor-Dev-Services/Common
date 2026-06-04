using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Messaging
{
    public sealed class Mediator : IMediator
    {
        private readonly IServiceProvider _serviceProvider;

        public Mediator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

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

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            if (notification is null) throw new ArgumentNullException(nameof(notification));

            var handlers = _serviceProvider.GetServices<INotificationHandler<TNotification>>();
            var tasks = handlers.Select(h => h.Handle(notification, cancellationToken));
            return Task.WhenAll(tasks);
        }

        private static Task<TResponse> InvokeRequestHandler<TResponse>(MethodInfo handleMethod, object handler, object request, CancellationToken cancellationToken)
        {
            var responseTask = handleMethod.Invoke(handler, [request, cancellationToken]) as Task<TResponse>;
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
            var responseTask = handleMethod.Invoke(behavior, [request, next, cancellationToken]) as Task<TResponse>;
            if (responseTask is null)
            {
                throw new InvalidOperationException($"Behavior {behavior.GetType().FullName} did not return Task<{typeof(TResponse).Name}>.");
            }

            return responseTask;
        }
    }
}
