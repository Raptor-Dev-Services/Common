using Common.Exceptions;

namespace Common.Errors
{
    /// <summary>
    /// Lista de mensajes de error de validacion que se acumulan antes de decidir si la
    /// operacion sigue. Es una <see cref="List{T}"/> de <see cref="string"/> con ayudas para
    /// convertirla en texto o en <see cref="BusinessRuleException"/>.
    /// </summary>
    public sealed class ErrorList : List<string>
    {
        /// <summary>
        /// Indica si la lista no tiene ningun mensaje.
        /// </summary>
        public bool IsEmpty => this.Count == 0;

        /// <summary>
        /// Crea una <see cref="BusinessRuleException"/> cuyo mensaje es <see cref="ToString"/>.
        /// No la lanza: quien llama decide si hacer <c>throw</c>.
        /// </summary>
        /// <returns>La excepcion con todos los mensajes, uno por linea.</returns>
        /// <exception cref="InvalidOperationException">
        /// Si la lista esta vacia (lo lanza <see cref="ToString"/>). Revisa <see cref="IsEmpty"/> antes.
        /// </exception>
        public BusinessRuleException AsException() => new(ToString());

        /// <summary>
        /// Une los mensajes en un solo texto: cada uno con el prefijo <c>"- "</c> y separados
        /// por salto de linea (<c>\n</c>).
        /// </summary>
        /// <returns>Los mensajes formateados, uno por linea.</returns>
        /// <exception cref="InvalidOperationException">
        /// Si la lista esta vacia: el formateo usa <c>Aggregate</c> sin semilla.
        /// </exception>
        public override string ToString() =>
            this.Select(item => $"- {item}").Aggregate((x, y) => $"{x}\n{y}");
    }
}

