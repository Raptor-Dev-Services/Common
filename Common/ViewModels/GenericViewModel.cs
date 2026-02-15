namespace Common.ViewModels
{
    [Obsolete("Use ResultViewModel<T> to map Result/ISuccess/IFailure consistently.")]
    public class GenericViewModel<T>
    {
        public GenericViewModel()
        {
            Data = null;
            Message = string.Empty;
            IsSuccess = false;
            UtcTimeStamp = DateTime.UtcNow;
        }

        public object? Data { get; private set; }

        public bool IsSuccess { get; private set; }

        public string? Message { get; private set; }

        public DateTime UtcTimeStamp { get; private set; }

        public GenericViewModel<T> OK(object data)
        {
            Data = data;
            Message = null;
            IsSuccess = true;
            UtcTimeStamp = DateTime.UtcNow;
            return this;
        }

        public GenericViewModel<T> Fail(string message)
        {
            Data = null;
            Message = message;
            IsSuccess = false;
            UtcTimeStamp = DateTime.UtcNow;
            return this;
        }
    }
}

