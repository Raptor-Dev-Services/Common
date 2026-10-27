namespace Common.Exceptions
{
    /// <summary>
    /// El recurso pedido no existe (o no es visible para quien lo pide).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>UseEnvelopeExceptionHandling</c> la responde con <b>404 Not Found</b> y su mensaje en el
    /// envelope. El mensaje se muestra al cliente: no debe llevar datos internos.
    /// </para>
    /// <para>
    /// Hereda de <see cref="BusinessRuleException"/> para que quien solo use <c>UseCoreProblemDetails</c>
    /// la siga viendo como un error esperado (400) y no como un 500.
    /// </para>
    /// </remarks>
    public class ResourceNotFoundException : BusinessRuleException
    {
        /// <summary>Crea la excepcion sin mensaje.</summary>
        public ResourceNotFoundException()
        { }

        /// <summary>Crea la excepcion con el mensaje que se mostrara al cliente.</summary>
        /// <param name="message">Que recurso no se encontro.</param>
        public ResourceNotFoundException(string? message)
            : base(message)
        { }

        /// <summary>Crea la excepcion con mensaje y la excepcion que la causo.</summary>
        /// <param name="message">Que recurso no se encontro.</param>
        /// <param name="innerException">Excepcion original.</param>
        public ResourceNotFoundException(string? message, Exception? innerException)
            : base(message, innerException)
        { }
    }
}
