using System.Text.Json.Serialization;

namespace Common.Workflow.States
{
    /// <summary>
    /// Quien pide la transicion: una persona (se consulta la matriz) o el sistema (no se consulta).
    /// </summary>
    public abstract record TransitionActor
    {
        private TransitionActor() { }

        /// <summary>
        /// Persona con sus grants. Solo hace las transiciones que la politica le concede.
        /// </summary>
        /// <param name="ActorId">Id del usuario, va al registro.</param>
        /// <param name="Grants">Roles o codigos de permiso del usuario.</param>
        public sealed record User(string ActorId, IReadOnlySet<string> Grants) : TransitionActor;

        /// <summary>
        /// Proceso automatico (cierre automatico, reapertura por seguimiento). Hace cualquier transicion
        /// <b>declarada</b> sin consultar la matriz; respeta estructura, motivo y guardas.
        /// </summary>
        /// <param name="Process">Nombre del proceso (<c>tickets.autoclose</c>); va al registro como id del actor.</param>
        public sealed record System(string Process) : TransitionActor;
    }

    /// <summary>
    /// Peticion de transicion.
    /// </summary>
    /// <typeparam name="TState">Tipo del estado.</typeparam>
    /// <typeparam name="TContext">Datos precargados por el producto para las guardas y los ganchos.</typeparam>
    /// <param name="From">Estado actual.</param>
    /// <param name="To">Estado pedido.</param>
    /// <param name="Actor">Quien la pide.</param>
    /// <param name="Reason">Motivo, si lo hay.</param>
    /// <param name="Context">Contexto del producto.</param>
    /// <param name="AtUtc">Instante de la transicion, con <c>Kind = Utc</c>.</param>
    public sealed record TransitionRequest<TState, TContext>(
        TState From, TState To, TransitionActor Actor, string? Reason, TContext Context, DateTime AtUtc)
        where TState : notnull;

    /// <summary>
    /// Resultado de evaluar una transicion. Los fallos se evaluan en este orden fijo: NotDeclared, NoChange,
    /// Denied, ReasonRequired, GuardFailed.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<TransitionOutcome>))]
    public enum TransitionOutcome
    {
        /// <summary>Se permite.</summary>
        Allowed,

        /// <summary>Origen y destino son el mismo estado: no es error, no hay nada que hacer.</summary>
        NoChange,

        /// <summary>No hay transicion declarada entre esos estados (o alguno no existe). Ni el sistema la hace.</summary>
        NotDeclared,

        /// <summary>El actor no tiene la transicion concedida en la politica.</summary>
        Denied,

        /// <summary>Falta el motivo, es demasiado corto o excede 1000 caracteres.</summary>
        ReasonRequired,

        /// <summary>La guarda de la transicion no se cumplio.</summary>
        GuardFailed,
    }

    /// <summary>
    /// Lo que devuelve una guarda o el <c>BeforeAsync</c> de un gancho.
    /// </summary>
    /// <param name="Passed">Si se cumple.</param>
    /// <param name="Code">Codigo estable del fallo (<c>tickets.solution_required</c>).</param>
    /// <param name="Message">Mensaje para el usuario.</param>
    public sealed record GuardResult(bool Passed, string? Code = null, string? Message = null)
    {
        /// <summary>Se cumple.</summary>
        public static readonly GuardResult Ok = new(true);

        /// <summary>
        /// No se cumple.
        /// </summary>
        /// <param name="code">Codigo estable del fallo.</param>
        /// <param name="message">Mensaje para el usuario.</param>
        /// <returns>El fallo.</returns>
        public static GuardResult Fail(string code, string message) => new(false, code, message);
    }

    /// <summary>
    /// Decision de <c>Evaluate</c>: no es excepcion, es un dato.
    /// </summary>
    /// <typeparam name="TState">Tipo del estado.</typeparam>
    /// <param name="Outcome">Resultado.</param>
    /// <param name="Transition">Transicion encontrada; nula en NotDeclared y NoChange.</param>
    /// <param name="FailureCode">Codigo del fallo (<c>states.denied</c>, el de la guarda...); nulo si se permite.</param>
    /// <param name="FailureMessage">Mensaje del fallo; nulo si se permite.</param>
    public sealed record TransitionDecision<TState>(
        TransitionOutcome Outcome, TransitionDefinition<TState>? Transition,
        string? FailureCode = null, string? FailureMessage = null) where TState : notnull
    {
        /// <summary>Si la transicion se permite.</summary>
        public bool IsAllowed => Outcome == TransitionOutcome.Allowed;
    }

    /// <summary>
    /// Lo que el producto guarda en su historial tras una transicion ejecutada.
    /// </summary>
    /// <typeparam name="TState">Tipo del estado.</typeparam>
    /// <param name="MachineName">Nombre de la maquina.</param>
    /// <param name="TransitionCode">Codigo de la transicion.</param>
    /// <param name="From">Estado de origen.</param>
    /// <param name="To">Estado de destino.</param>
    /// <param name="ActorId">Id del usuario, o el proceso si fue el sistema.</param>
    /// <param name="IsSystem">Si la hizo el sistema.</param>
    /// <param name="Reason">Motivo recortado, o nulo si no hubo.</param>
    /// <param name="AtUtc">Instante de la transicion (UTC).</param>
    public sealed record TransitionRecord<TState>(
        string MachineName, string TransitionCode, TState From, TState To,
        string ActorId, bool IsSystem, string? Reason, DateTime AtUtc) where TState : notnull;

    /// <summary>
    /// Gancho del producto alrededor de una transicion (fijar fecha de solucion, borrar niveles de SLA,
    /// notificar). Todos los miembros tienen implementacion por omision.
    /// </summary>
    /// <typeparam name="TState">Tipo del estado.</typeparam>
    /// <typeparam name="TContext">Contexto del producto.</typeparam>
    public interface ITransitionHook<TState, TContext> where TState : notnull
    {
        /// <summary>
        /// Si el gancho aplica a esa transicion. Por omision, a todas.
        /// </summary>
        /// <param name="transitionCode">Codigo de la transicion.</param>
        /// <returns>Verdadero si aplica.</returns>
        bool AppliesTo(string transitionCode) => true;

        /// <summary>
        /// Antes de aplicar: puede vetar devolviendo un <see cref="GuardResult"/> fallido. Si veta, no se aplica
        /// la transicion ni corren los ganchos siguientes.
        /// </summary>
        /// <param name="request">La peticion.</param>
        /// <param name="ct">Token de cancelacion.</param>
        /// <returns><see cref="GuardResult.Ok"/> o el veto.</returns>
        ValueTask<GuardResult> BeforeAsync(TransitionRequest<TState, TContext> request, CancellationToken ct)
            => ValueTask.FromResult(GuardResult.Ok);

        /// <summary>
        /// Despues de aplicar, dentro de la transaccion del producto. Si lanza, la excepcion se propaga.
        /// </summary>
        /// <param name="record">El registro de la transicion.</param>
        /// <param name="context">Contexto del producto.</param>
        /// <param name="ct">Token de cancelacion.</param>
        /// <returns>Tarea.</returns>
        ValueTask AfterAsync(TransitionRecord<TState> record, TContext context, CancellationToken ct)
            => ValueTask.CompletedTask;
    }
}
