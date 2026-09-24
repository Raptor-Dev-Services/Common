using Common.Messaging;

namespace Common.Abstractions
{
    /// <summary>
    /// Caso de uso: un <see cref="IRequestHandler{TRequest, TResponse}"/> cuya respuesta es un
    /// <see cref="IResponse"/>, y por eso pasa por el pipeline del mediador (log y publicacion
    /// a los presenters).
    /// </summary>
    /// <typeparam name="TRequest">Tipo de la peticion.</typeparam>
    /// <typeparam name="TResponse">Tipo de la respuesta.</typeparam>
    public interface IInteractor<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : IResponse
    { }
}

