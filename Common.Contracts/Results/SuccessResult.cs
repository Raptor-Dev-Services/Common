namespace Common.Results
{
    /// <summary>
    /// Resultado exitoso de una operacion sin datos de retorno.
    /// </summary>
    public sealed class SuccessResult : Result, ISuccess
    { }

    /// <summary>
    /// Resultado exitoso que lleva un <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Tipo de los datos.</typeparam>
    public sealed class SuccessResult<T> : Result<T>, ISuccess<T>
    {
        /// <summary>
        /// Crea el resultado con sus datos.
        /// </summary>
        /// <param name="data">Datos a devolver. No se valida que no sea nulo.</param>
        public SuccessResult(T data) => Data = data;

        /// <summary>
        /// Datos que devolvio la operacion.
        /// </summary>
        public T Data { get; }

        /// <summary>
        /// Convierte un valor en un <see cref="SuccessResult{T}"/> que lo contiene.
        /// </summary>
        /// <param name="data">Datos a envolver.</param>
        public static implicit operator SuccessResult<T>(T data) => new(data);
    }
}
