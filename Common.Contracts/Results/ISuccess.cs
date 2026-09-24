namespace Common.Results
{
    /// <summary>
    /// Marca un resultado exitoso sin datos.
    /// </summary>
    public interface ISuccess
    { }

    /// <summary>
    /// Marca un resultado exitoso que lleva datos.
    /// </summary>
    /// <typeparam name="T">Tipo de los datos.</typeparam>
    public interface ISuccess<T> : ISuccess
    {
        /// <summary>
        /// Datos que devolvio la operacion.
        /// </summary>
        T Data { get; }
    }
}
