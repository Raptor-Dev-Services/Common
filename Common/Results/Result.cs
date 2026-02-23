using Common.Messaging;

namespace Common.Results
{
    public abstract class Result : IResponse
    {
        public static Result OK() => new SuccessResult();

        public static Result Fail(string message) => new FailureResult(message);

        public static Result Fail(Exception ex) => new FailureResult(ex);

        public static Result<T> OK<T>(T data) => new SuccessResult<T>(data);

        public static Result<T> Fail<T>(string message) => new FailureResult<T>(message);

        public static Result<T> Fail<T>(Exception ex) => new FailureResult<T>(ex);
    }

    public abstract class Result<T> : IResponse
    { }
}
