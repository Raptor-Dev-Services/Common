namespace Common.Results
{
    /// <summary>
    /// Resultado fallido de una operacion sin datos de retorno.
    /// </summary>
    public sealed class FailureResult : Result, IFailure
    {
        /// <summary>
        /// Crea el fallo a partir de un mensaje. Lo envuelve en un <see cref="System.Exception"/> generico.
        /// </summary>
        /// <param name="message">Mensaje del fallo.</param>
        public FailureResult(string message) => Exception = new Exception(message);

        /// <summary>
        /// Crea el fallo a partir de una excepcion.
        /// </summary>
        /// <param name="ex">Excepcion que describe el fallo.</param>
        public FailureResult(Exception ex) => Exception = ex;

        /// <summary>
        /// Excepcion que describe el fallo. Si se creo con un mensaje, es un <see cref="System.Exception"/> generico.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// Mensaje del fallo: el <see cref="System.Exception.Message"/> de <see cref="Exception"/>.
        /// </summary>
        public string Message => Exception.Message;
    }

    /// <summary>
    /// Resultado fallido de una operacion que, de haber salido bien, habria devuelto un <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Tipo de dato del resultado exitoso.</typeparam>
    public sealed class FailureResult<T> : Result<T>, IFailure
    {
        /// <summary>
        /// Crea el fallo a partir de un mensaje. Lo envuelve en un <see cref="System.Exception"/> generico.
        /// </summary>
        /// <param name="message">Mensaje del fallo.</param>
        public FailureResult(string message) => Exception = new Exception(message);

        /// <summary>
        /// Crea el fallo a partir de una excepcion.
        /// </summary>
        /// <param name="ex">Excepcion que describe el fallo.</param>
        public FailureResult(Exception ex) => Exception = ex;

        /// <summary>
        /// Excepcion que describe el fallo. Si se creo con un mensaje, es un <see cref="System.Exception"/> generico.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// Mensaje del fallo: el <see cref="System.Exception.Message"/> de <see cref="Exception"/>.
        /// </summary>
        public string Message => Exception.Message;
    }
}
