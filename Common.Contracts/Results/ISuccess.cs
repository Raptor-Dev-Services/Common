namespace Common.Results
{
    public interface ISuccess
    { }

    public interface ISuccess<T> : ISuccess
    {
        T Data { get; }
    }
}
