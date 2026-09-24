using Common.Messaging;
using Common.Results;

namespace Common.Abstractions
{
    /// <summary>
    /// Peticion cuya respuesta es un <see cref="Result{T}"/>.
    /// </summary>
    /// <typeparam name="TResult">Tipo de los datos del exito.</typeparam>
    public interface IResultRequest<TResult> : IRequest<Result<TResult>>
    { }
}

