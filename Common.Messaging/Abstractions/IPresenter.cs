using Common.Messaging;

namespace Common.Abstractions
{
    /// <summary>
    /// Presenter: recibe la respuesta de un caso de uso cuando el pipeline del mediador la
    /// publica, y la traduce al formato de salida.
    /// </summary>
    /// <typeparam name="TResult">Tipo de la respuesta que recibe.</typeparam>
    public interface IPresenter<TResult> : INotificationHandler<TResult>
        where TResult : IResponse
    { }
}

