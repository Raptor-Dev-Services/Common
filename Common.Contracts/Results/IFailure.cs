namespace Common.Results
{
    public interface IFailure
    {
        string Message { get; }
    }

    public interface IValidationFailure : IFailure
    { }

    public interface INotFoundFailure : IFailure
    { }

    public interface IConflictFailure : IFailure
    { }
}
