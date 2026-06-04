using Common.Messaging;
using Common.Results;

namespace Common.Abstractions
{
    public interface IResultPresenter<TResponse> : INotificationHandler<Result<TResponse>>
    { }
}

