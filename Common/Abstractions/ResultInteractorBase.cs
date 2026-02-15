namespace Common.Abstractions
{
    public abstract class ResultInteractorBase<TRequest, TResult> : IResultInteractor<TRequest, TResult>
        where TRequest : IResultRequest<TResult>
    {
        protected Results.Result<TResult> OK(TResult data) => Results.Result.OK(data);

        protected Results.Result<TResult> Fail(string message) => Results.Result.Fail<TResult>(message);

        protected Results.Result<TResult> Fail(Exception ex) => Results.Result.Fail<TResult>(ex);

        public abstract Task<Results.Result<TResult>> Handle(TRequest request, CancellationToken cancellationToken);
    }
}
