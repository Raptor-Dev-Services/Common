namespace Common.Exceptions
{
    /// <summary>
    /// Violacion de una regla de negocio: un error esperado cuyo mensaje se puede mostrar
    /// al cliente.
    /// </summary>
    /// <remarks>
    /// El middleware de <c>UseCoreProblemDetails</c> la convierte en una respuesta 400 con
    /// este mensaje como detalle; cualquier otra excepcion sale como 500 con un mensaje
    /// generico. Por eso el mensaje no debe llevar datos internos.
    /// </remarks>
    public class BusinessRuleException : Exception
    {
        /// <summary>
        /// Crea la excepcion sin mensaje.
        /// </summary>
        public BusinessRuleException()
        { }

        /// <summary>
        /// Crea la excepcion con el mensaje que se mostrara al cliente.
        /// </summary>
        /// <param name="message">Descripcion de la regla que se violo.</param>
        public BusinessRuleException(string? message)
            : base(message)
        { }

        /// <summary>
        /// Crea la excepcion con mensaje y la excepcion que la causo.
        /// </summary>
        /// <param name="message">Descripcion de la regla que se violo.</param>
        /// <param name="innerException">Excepcion original.</param>
        public BusinessRuleException(string? message, Exception? innerException)
            : base(message, innerException)
        { }
    }
}

