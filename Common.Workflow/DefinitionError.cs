namespace Common.Workflow
{
    /// <summary>
    /// Un error encontrado al validar la definicion de un motor (calendario, maquina de estados,
    /// reglas, formulario...). Las definiciones se validan completas: se reportan todos los errores,
    /// no solo el primero.
    /// </summary>
    /// <param name="Code">
    /// Codigo estable con la forma <c>componente.motivo</c> (por ejemplo <c>calendar.segment_overlap</c>).
    /// Es contrato publico: no se renombra en una version menor.
    /// </param>
    /// <param name="Path">Ruta del elemento que fallo dentro de la definicion (por ejemplo <c>Segments[2]</c>).</param>
    /// <param name="Message">Explicacion legible para quien escribio la definicion.</param>
    public sealed record DefinitionError(string Code, string Path, string Message);
}
