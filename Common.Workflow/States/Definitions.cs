namespace Common.Workflow.States
{
    /// <summary>
    /// Un estado de la maquina.
    /// </summary>
    /// <typeparam name="TState">Tipo del estado del producto (normalmente un enum).</typeparam>
    /// <param name="State">Valor del estado.</param>
    /// <param name="Code">Codigo estable para JSON, base de datos y UI (<c>solved</c>): letra inicial y <c>[A-Za-z0-9_]</c>.</param>
    /// <param name="Sets">Conjuntos a los que pertenece.</param>
    public sealed record StateDefinition<TState>(TState State, string Code, StateSet Sets) where TState : notnull;

    /// <summary>
    /// Una arista de la maquina: de un estado a otro. Un mismo <see cref="Code"/> puede repetirse en varias
    /// aristas (<c>pause</c> desde <c>assigned</c> y desde <c>planned</c>) si comparten destino, motivo y guarda.
    /// </summary>
    /// <typeparam name="TState">Tipo del estado.</typeparam>
    /// <param name="Code">Codigo de la transicion (<c>solve</c>): letra inicial y <c>[A-Za-z0-9_]</c>.</param>
    /// <param name="From">Estado de origen.</param>
    /// <param name="To">Estado de destino; distinto del origen.</param>
    /// <param name="RequiresReason">Si exige motivo.</param>
    /// <param name="MinReasonLength">Largo minimo del motivo recortado, de 1 a 1000.</param>
    /// <param name="Guard">Nombre de la guarda registrada al compilar, o nulo.</param>
    public sealed record TransitionDefinition<TState>(
        string Code, TState From, TState To,
        bool RequiresReason = false, int MinReasonLength = 1, string? Guard = null) where TState : notnull;

    /// <summary>
    /// Definicion completa de una maquina de estados: la estructura es codigo, no datos por tenant.
    /// </summary>
    /// <typeparam name="TState">Tipo del estado.</typeparam>
    /// <param name="Name">Nombre de la maquina (<c>ticket</c>), va en el registro y en la traza.</param>
    /// <param name="Initial">Estado inicial; todos los estados deben ser alcanzables desde el.</param>
    /// <param name="States">Estados, en orden de declaracion.</param>
    /// <param name="Transitions">Transiciones, en orden de declaracion.</param>
    public sealed record StateMachineDefinition<TState>(
        string Name, TState Initial,
        IReadOnlyList<StateDefinition<TState>> States,
        IReadOnlyList<TransitionDefinition<TState>> Transitions) where TState : notnull;
}
