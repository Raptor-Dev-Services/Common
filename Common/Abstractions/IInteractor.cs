using Common.Messaging;

namespace Common.Abstractions
{
    public interface IInteractor<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : IResponse
    { }
}

