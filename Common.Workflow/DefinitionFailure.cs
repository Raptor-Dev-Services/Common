using Common.Results;

namespace Common.Workflow
{
    /// <summary>
    /// Resultado fallido de compilar o validar una definicion: lleva <b>todos</b> los
    /// <see cref="DefinitionError"/> encontrados. Es un <see cref="IValidationFailure"/>, asi que el
    /// consumidor lo mapea a HTTP igual que cualquier error de validacion.
    /// </summary>
    /// <typeparam name="T">Tipo que se habria construido si la definicion fuera valida.</typeparam>
    public sealed class DefinitionFailure<T> : Result<T>, IValidationFailure
    {
        /// <summary>
        /// Crea el fallo con los errores encontrados.
        /// </summary>
        /// <param name="errors">Errores de la definicion; al menos uno.</param>
        /// <exception cref="ArgumentException">Si <paramref name="errors"/> esta vacio.</exception>
        public DefinitionFailure(IEnumerable<DefinitionError> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            Errors = errors.ToArray();
            if (Errors.Count == 0)
                throw new ArgumentException("Un DefinitionFailure necesita al menos un error.", nameof(errors));
        }

        /// <summary>
        /// Todos los errores de la definicion, en el orden en que se encontraron.
        /// </summary>
        public IReadOnlyList<DefinitionError> Errors { get; }

        /// <summary>
        /// Resumen legible: cada error como <c>Path: Code - Message</c>, separados por <c>; </c>.
        /// </summary>
        public string Message => string.Join("; ", Errors.Select(e => $"{e.Path}: {e.Code} - {e.Message}"));
    }
}
