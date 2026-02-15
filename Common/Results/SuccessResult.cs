namespace Common.Results
{
    public sealed class SuccessResult<T> : Result<T>, ISuccess<T>
    {
        public SuccessResult(T data) => Data = data;

        public T Data { get; }

        public static implicit operator SuccessResult<T>(T data) => new(data);
    }
}

