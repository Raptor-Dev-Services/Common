using Common.Messaging;

namespace Common.Results
{
    public abstract class Result : IResponse
    {
        public static Result<T> OK<T>(T data) => new SuccessResult<T>(data);

        public static Result<T> Fail<T>(string message) => new FailureResult<T>(message);

        public static Result<T> Fail<T>(Exception ex) => new FailureResult<T>(ex);
    }

    public abstract class Result<T> : IResponse
    { }
}

