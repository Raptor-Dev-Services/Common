using Common.Messaging;
using Common.Results;

namespace Common.Abstractions
{
    /// <summary>
    /// Caso de uso que responde con un <see cref="Result{T}"/> (exito o fallo).
    /// </summary>
    /// <typeparam name="TRequest">Tipo de la peticion.</typeparam>
    /// <typeparam name="TResult">Tipo de los datos del exito.</typeparam>
    public interface IResultInteractor<TRequest, TResult> : IRequestHandler<TRequest, Result<TResult>>
        where TRequest : IResultRequest<TResult>
    { }
}

