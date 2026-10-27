namespace Common.Exceptions
{
    /// <summary>
    /// Los datos guardados estan en un estado que el sistema no puede procesar (referencia rota,
    /// catalogo incompleto, dato obligatorio ausente).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>UseEnvelopeExceptionHandling</c> la responde con <b>422 Unprocessable Entity</b> y su mensaje
    /// en el envelope. Es un fallo esperado y accionable por el operador, por eso el mensaje se
    /// muestra: debe decir que dato corregir, sin datos internos.
    /// </para>
    /// <para>
    /// Hereda de <see cref="BusinessRuleException"/> para que quien solo use <c>UseCoreProblemDetails</c>
    /// la siga viendo como un error esperado (400) y no como un 500.
    /// </para>
    /// </remarks>
    public class DataIntegrityException : BusinessRuleException
    {
        /// <summary>Crea la excepcion sin mensaje.</summary>
        public DataIntegrityException()
        { }

        /// <summary>Crea la excepcion con el mensaje que se mostrara al cliente.</summary>
        /// <param name="message">Que dato esta mal y como corregirlo.</param>
        public DataIntegrityException(string? message)
            : base(message)
        { }

        /// <summary>Crea la excepcion con mensaje y la excepcion que la causo.</summary>
        /// <param name="message">Que dato esta mal y como corregirlo.</param>
        /// <param name="innerException">Excepcion original.</param>
        public DataIntegrityException(string? message, Exception? innerException)
            : base(message, innerException)
        { }
    }
}
