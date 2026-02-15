using Common.Messaging;
using Common.Results;

namespace Common.Abstractions
{
    public interface IResultRequest<TResult> : IRequest<Result<TResult>>
    { }
}

