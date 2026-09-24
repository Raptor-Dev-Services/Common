namespace Common.ViewModels
{
    /// <summary>
    /// Envoltorio de respuesta de API (datos, exito, mensaje y marca de tiempo). Obsoleto: usar
    /// <see cref="ResultViewModel{T}"/>.
    /// </summary>
    /// <typeparam name="T">No se usa: <see cref="Data"/> es <see cref="object"/>.</typeparam>
    [Obsolete("Use ResultViewModel<T> to map Result/ISuccess/IFailure consistently.")]
    public class GenericViewModel<T>
    {
        /// <summary>
        /// Crea el envoltorio vacio: sin datos, sin exito, mensaje vacio y marca de tiempo actual (UTC).
        /// </summary>
        public GenericViewModel()
        {
            Data = null;
            Message = string.Empty;
            IsSuccess = false;
            UtcTimeStamp = DateTime.UtcNow;
        }

        /// <summary>
        /// Datos de la respuesta; <c>null</c> si fallo.
        /// </summary>
        public object? Data { get; private set; }

        /// <summary>
        /// Indica si la operacion salio bien.
        /// </summary>
        public bool IsSuccess { get; private set; }

        /// <summary>
        /// Mensaje de error; <c>null</c> tras <see cref="OK"/>.
        /// </summary>
        public string? Message { get; private set; }

        /// <summary>
        /// Momento (UTC) de la ultima asignacion.
        /// </summary>
        public DateTime UtcTimeStamp { get; private set; }

        /// <summary>
        /// Marca exito con los datos dados y borra el mensaje.
        /// </summary>
        /// <param name="data">Datos de la respuesta.</param>
        /// <returns>Esta misma instancia.</returns>
        public GenericViewModel<T> OK(object data)
        {
            Data = data;
            Message = null;
            IsSuccess = true;
            UtcTimeStamp = DateTime.UtcNow;
            return this;
        }

        /// <summary>
        /// Marca fallo con el mensaje dado y borra los datos.
        /// </summary>
        /// <param name="message">Mensaje de error.</param>
        /// <returns>Esta misma instancia.</returns>
        public GenericViewModel<T> Fail(string message)
        {
            Data = null;
            Message = message;
            IsSuccess = false;
            UtcTimeStamp = DateTime.UtcNow;
            return this;
        }
    }
}

