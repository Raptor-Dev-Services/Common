namespace Common.Abstractions
{
    /// <summary>
    /// Base para casos de uso que responden con <see cref="Results.Result{T}"/>. Solo aporta
    /// atajos para construir el exito y el fallo.
    /// </summary>
    /// <typeparam name="TRequest">Tipo de la peticion.</typeparam>
    /// <typeparam name="TResult">Tipo de los datos del exito.</typeparam>
    public abstract class ResultInteractorBase<TRequest, TResult> : IResultInteractor<TRequest, TResult>
        where TRequest : IResultRequest<TResult>
    {
        /// <summary>
        /// Crea un resultado exitoso con datos.
        /// </summary>
        /// <param name="data">Datos a devolver.</param>
        /// <returns>Un <see cref="Results.SuccessResult{T}"/>.</returns>
        protected Results.Result<TResult> OK(TResult data) => Results.Result.OK(data);

        /// <summary>
        /// Crea un resultado fallido a partir de un mensaje.
        /// </summary>
        /// <param name="message">Mensaje del fallo.</param>
        /// <returns>Un <see cref="Results.FailureResult{T}"/>.</returns>
        protected Results.Result<TResult> Fail(string message) => Results.Result.Fail<TResult>(message);

        /// <summary>
        /// Crea un resultado fallido a partir de una excepcion.
        /// </summary>
        /// <param name="ex">Excepcion que describe el fallo.</param>
        /// <returns>Un <see cref="Results.FailureResult{T}"/>.</returns>
        protected Results.Result<TResult> Fail(Exception ex) => Results.Result.Fail<TResult>(ex);

        /// <summary>
        /// Ejecuta el caso de uso.
        /// </summary>
        /// <param name="request">Peticion a atender.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>El resultado: exito con datos o fallo.</returns>
        public abstract Task<Results.Result<TResult>> Handle(TRequest request, CancellationToken cancellationToken);
    }
}
