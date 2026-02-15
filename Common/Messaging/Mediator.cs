using System.Linq;
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

            RequestHandlerDelegate<TResponse> handlerDelegate =
                () => ((dynamic)handler).Handle((dynamic)request, cancellationToken);

            var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
            var behaviors = _serviceProvider.GetServices(behaviorType).Cast<dynamic>().Reverse().ToList();

            foreach (var behavior in behaviors)
            {
                var next = handlerDelegate;
                handlerDelegate = () => behavior.Handle((dynamic)request, next, cancellationToken);
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
    }
}
