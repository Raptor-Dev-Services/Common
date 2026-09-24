using Common.Messaging;
using Common.Results;

namespace Common.Abstractions
{
    /// <summary>
    /// Presenter que recibe el <see cref="Result{T}"/> de un caso de uso cuando el pipeline
    /// del mediador lo publica.
    /// </summary>
    /// <typeparam name="TResponse">Tipo de los datos del exito.</typeparam>
    public interface IResultPresenter<TResponse> : INotificationHandler<Result<TResponse>>
    { }
}

