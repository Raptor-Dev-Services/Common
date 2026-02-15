using Common.Messaging;
using Common.Results;

namespace Common.Abstractions
{
    public interface IResultInteractor<TRequest, TResult> : IRequestHandler<TRequest, Result<TResult>>
        where TRequest : IResultRequest<TResult>
    { }
}

