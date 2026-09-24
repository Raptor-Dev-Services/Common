using Common.Messaging;

namespace Common.Results
{
    /// <summary>
    /// Resultado de una operacion sin datos de retorno: exito (<see cref="SuccessResult"/>) o
    /// fallo (<see cref="FailureResult"/>). Tambien expone las fabricas de <see cref="Result{T}"/>.
    /// </summary>
    /// <remarks>
    /// No tiene propiedad de exito: se distingue por tipo, con <c>is ISuccess</c> o
    /// <c>is IFailure</c>. Es un <see cref="IResponse"/>, asi que el pipeline del mediador lo publica.
    /// </remarks>
    public abstract class Result : IResponse
    {
        /// <summary>
        /// Crea un resultado exitoso sin datos.
        /// </summary>
        /// <returns>Un <see cref="SuccessResult"/>.</returns>
        public static Result OK() => new SuccessResult();

        /// <summary>
        /// Crea un resultado fallido a partir de un mensaje.
        /// </summary>
        /// <param name="message">Mensaje del fallo.</param>
        /// <returns>Un <see cref="FailureResult"/>.</returns>
        public static Result Fail(string message) => new FailureResult(message);

        /// <summary>
        /// Crea un resultado fallido a partir de una excepcion.
        /// </summary>
        /// <param name="ex">Excepcion que describe el fallo.</param>
        /// <returns>Un <see cref="FailureResult"/>.</returns>
        public static Result Fail(Exception ex) => new FailureResult(ex);

        /// <summary>
        /// Crea un resultado exitoso con datos.
        /// </summary>
        /// <typeparam name="T">Tipo de los datos.</typeparam>
        /// <param name="data">Datos a devolver.</param>
        /// <returns>Un <see cref="SuccessResult{T}"/>.</returns>
        public static Result<T> OK<T>(T data) => new SuccessResult<T>(data);

        /// <summary>
        /// Crea un resultado fallido tipado a partir de un mensaje.
        /// </summary>
        /// <typeparam name="T">Tipo de dato que habria tenido el exito.</typeparam>
        /// <param name="message">Mensaje del fallo.</param>
        /// <returns>Un <see cref="FailureResult{T}"/>.</returns>
        public static Result<T> Fail<T>(string message) => new FailureResult<T>(message);

        /// <summary>
        /// Crea un resultado fallido tipado a partir de una excepcion.
        /// </summary>
        /// <typeparam name="T">Tipo de dato que habria tenido el exito.</typeparam>
        /// <param name="ex">Excepcion que describe el fallo.</param>
        /// <returns>Un <see cref="FailureResult{T}"/>.</returns>
        public static Result<T> Fail<T>(Exception ex) => new FailureResult<T>(ex);
    }

    /// <summary>
    /// Resultado de una operacion que devuelve un <typeparamref name="T"/>: exito
    /// (<see cref="SuccessResult{T}"/>) o fallo (<see cref="FailureResult{T}"/>).
    /// </summary>
    /// <remarks>
    /// No hereda de <see cref="Result"/> ni tiene miembros propios: se distingue por tipo, con
    /// <c>is ISuccess&lt;T&gt;</c> o <c>is IFailure</c>. Las fabricas estan en <see cref="Result"/>.
    /// </remarks>
    /// <typeparam name="T">Tipo de los datos del exito.</typeparam>
    public abstract class Result<T> : IResponse
    { }
}
