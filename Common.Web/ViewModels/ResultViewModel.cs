using Common.Results;
namespace Common.ViewModels
{
    /// <summary>
    /// Envoltorio de respuesta de API (datos, exito, mensaje y marca de tiempo) que se llena a partir de
    /// un <see cref="ISuccess{T}"/> o un <see cref="IFailure"/>.
    /// </summary>
    /// <remarks>
    /// No decide el status HTTP: lo decide quien devuelve el view model (el presenter o el controlador).
    /// </remarks>
    /// <typeparam name="T">No se usa: <see cref="Data"/> es <see cref="object"/>.</typeparam>
    public class ResultViewModel<T>
    {
        /// <summary>
        /// Crea el envoltorio vacio: sin datos, sin exito, mensaje vacio y marca de tiempo actual (UTC).
        /// </summary>
        public ResultViewModel()
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
        /// Mensaje de error; <c>null</c> cuando hubo exito, cadena vacia recien creado.
        /// </summary>
        public string? Message { get; private set; }

        /// <summary>
        /// Momento (UTC) de la ultima asignacion.
        /// </summary>
        public DateTime UtcTimeStamp { get; private set; }

        /// <summary>
        /// Llena el envoltorio como fallo con el mensaje de <paramref name="failure"/> y borra los datos.
        /// </summary>
        /// <param name="failure">Fallo a mostrar.</param>
        public void Set(IFailure failure)
        {
            Data = null;
            Message = failure.Message;
            IsSuccess = false;
            UtcTimeStamp = DateTime.UtcNow;
        }

        /// <summary>
        /// Llena el envoltorio como exito con los datos de <paramref name="success"/> y borra el mensaje.
        /// </summary>
        /// <typeparam name="TData">Tipo de los datos del exito.</typeparam>
        /// <param name="success">Resultado exitoso.</param>
        /// <param name="callback">
        /// Transformacion opcional de los datos (por ejemplo a un DTO). Si devuelve <c>null</c>, se usan los datos originales.
        /// </param>
        public void Set<TData>(ISuccess<TData> success, Func<TData, object>? callback = null)
        {
            Data = callback?.Invoke(success.Data) ?? success.Data;
            IsSuccess = true;
            UtcTimeStamp = DateTime.UtcNow;
            Message = null;
        }

        /// <summary>
        /// Marca fallo con el mensaje dado y borra los datos.
        /// </summary>
        /// <param name="message">Mensaje de error.</param>
        /// <param name="statusCode">No se usa: el status HTTP lo decide quien devuelve el view model.</param>
        /// <returns>Esta misma instancia.</returns>
        public ResultViewModel<T> Fail(string message, int statusCode = 400)
        {
            Data = null;
            Message = message;
            IsSuccess = false;
            UtcTimeStamp = DateTime.UtcNow;
            return this;
        }

        /// <summary>
        /// Marca fallo con el mensaje de la excepcion y borra los datos.
        /// </summary>
        /// <param name="ex">Excepcion cuyo <see cref="Exception.Message"/> se muestra tal cual.</param>
        /// <param name="statusCode">No se usa: el status HTTP lo decide quien devuelve el view model.</param>
        /// <returns>Esta misma instancia.</returns>
        public ResultViewModel<T> Fail(Exception ex, int statusCode = 400) => Fail(ex.Message, statusCode);

        /// <summary>
        /// Marca exito con los datos dados y borra el mensaje.
        /// </summary>
        /// <param name="data">Datos de la respuesta.</param>
        /// <param name="statusCode">No se usa: el status HTTP lo decide quien devuelve el view model.</param>
        /// <returns>Esta misma instancia.</returns>
        public ResultViewModel<T> OK(object data, int statusCode = 200)
        {
            Data = data;
            Message = null;
            IsSuccess = true;
            UtcTimeStamp = DateTime.UtcNow;
            return this;
        }
    }
}
