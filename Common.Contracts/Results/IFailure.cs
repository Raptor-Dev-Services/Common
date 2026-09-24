namespace Common.Results
{
    /// <summary>
    /// Marca un resultado fallido. El pipeline del mediador lo registra como Warning.
    /// </summary>
    public interface IFailure
    {
        /// <summary>
        /// Mensaje que describe el fallo.
        /// </summary>
        string Message { get; }
    }

    /// <summary>
    /// Fallo por datos de entrada invalidos. La libreria no lo implementa ni lo mapea a un
    /// status HTTP: es una marca para que el consumidor decida.
    /// </summary>
    public interface IValidationFailure : IFailure
    { }

    /// <summary>
    /// Fallo porque el recurso pedido no existe. La libreria no lo implementa ni lo mapea a
    /// un status HTTP: es una marca para que el consumidor decida.
    /// </summary>
    public interface INotFoundFailure : IFailure
    { }

    /// <summary>
    /// Fallo por conflicto con el estado actual (duplicado, version desfasada). La libreria
    /// no lo implementa ni lo mapea a un status HTTP: es una marca para que el consumidor decida.
    /// </summary>
    public interface IConflictFailure : IFailure
    { }
}
