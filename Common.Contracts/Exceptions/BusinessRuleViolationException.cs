namespace Common.Exceptions
{
    /// <summary>
    /// La operacion choca con el estado actual del recurso (ya existe, ya fue procesado, esta en uso).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>UseEnvelopeExceptionHandling</c> la responde con <b>409 Conflict</b> y su mensaje en el
    /// envelope. La <see cref="BusinessRuleException"/> base, en cambio, es un <b>400 Bad Request</b>:
    /// la peticion en si es invalida, sin importar el estado del recurso.
    /// </para>
    /// <para>
    /// Hereda de <see cref="BusinessRuleException"/>: quien solo use <c>UseCoreProblemDetails</c> la ve
    /// como 400, el mismo comportamiento de siempre ante una regla rota.
    /// </para>
    /// </remarks>
    public class BusinessRuleViolationException : BusinessRuleException
    {
        /// <summary>Crea la excepcion sin mensaje.</summary>
        public BusinessRuleViolationException()
        { }

        /// <summary>Crea la excepcion con el mensaje que se mostrara al cliente.</summary>
        /// <param name="message">Descripcion del conflicto.</param>
        public BusinessRuleViolationException(string? message)
            : base(message)
        { }

        /// <summary>Crea la excepcion con mensaje y la excepcion que la causo.</summary>
        /// <param name="message">Descripcion del conflicto.</param>
        /// <param name="innerException">Excepcion original.</param>
        public BusinessRuleViolationException(string? message, Exception? innerException)
            : base(message, innerException)
        { }
    }
}
