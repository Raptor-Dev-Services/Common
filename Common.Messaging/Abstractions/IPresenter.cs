using Common.Messaging;

namespace Common.Abstractions
{
    public interface IPresenter<TResult> : INotificationHandler<TResult>
        where TResult : IResponse
    { }
}

