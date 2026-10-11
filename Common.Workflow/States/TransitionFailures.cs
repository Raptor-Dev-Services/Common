using Common.Results;

namespace Common.Workflow.States
{
    /// <summary>
    /// <c>ExecuteAsync</c> fallo porque <c>Evaluate</c> no lo permitio: NotDeclared, Denied o GuardFailed.
    /// Es un <see cref="IConflictFailure"/>; el producto mapea <see cref="TransitionOutcome.Denied"/> a 403 mirando
    /// <see cref="Outcome"/>. El motivo faltante es <see cref="TransitionReasonRequiredFailure{T}"/>.
    /// </summary>
    /// <typeparam name="T">Tipo del exito.</typeparam>
    public sealed class TransitionDeniedFailure<T> : Result<T>, IConflictFailure
    {
        /// <summary>
        /// Crea el fallo.
        /// </summary>
        /// <param name="outcome">Resultado de la evaluacion.</param>
        /// <param name="code">Codigo estable del fallo.</param>
        /// <param name="message">Mensaje legible.</param>
        public TransitionDeniedFailure(TransitionOutcome outcome, string code, string message)
        {
            Outcome = outcome;
            Code = code;
            Message = message;
        }

        /// <summary>Resultado de la evaluacion.</summary>
        public TransitionOutcome Outcome { get; }

        /// <summary>Codigo estable del fallo (<c>states.denied</c>, o el de la guarda).</summary>
        public string Code { get; }

        /// <summary>Mensaje legible.</summary>
        public string Message { get; }
    }

    /// <summary>
    /// <c>ExecuteAsync</c> fallo porque falta el motivo o no cumple el largo (<see cref="TransitionOutcome.ReasonRequired"/>).
    /// Es un <see cref="IValidationFailure"/>: dato de entrada invalido.
    /// </summary>
    /// <typeparam name="T">Tipo del exito.</typeparam>
    public sealed class TransitionReasonRequiredFailure<T> : Result<T>, IValidationFailure
    {
        /// <summary>
        /// Crea el fallo.
        /// </summary>
        /// <param name="transitionCode">Transicion que exige el motivo.</param>
        /// <param name="message">Mensaje legible.</param>
        public TransitionReasonRequiredFailure(string transitionCode, string message)
        {
            TransitionCode = transitionCode;
            Message = message;
        }

        /// <summary>Resultado de la evaluacion: siempre <see cref="TransitionOutcome.ReasonRequired"/>.</summary>
        public TransitionOutcome Outcome => TransitionOutcome.ReasonRequired;

        /// <summary>Codigo estable del fallo: <c>states.reason_required</c>.</summary>
        public string Code => StateMachineCodes.ReasonRequired;

        /// <summary>Transicion que exige el motivo.</summary>
        public string TransitionCode { get; }

        /// <summary>Mensaje legible.</summary>
        public string Message { get; }
    }

    /// <summary>
    /// Un gancho veto la transicion en su <c>BeforeAsync</c>; no se aplico nada.
    /// </summary>
    /// <typeparam name="T">Tipo del exito.</typeparam>
    public sealed class TransitionVetoedFailure<T> : Result<T>, IConflictFailure
    {
        /// <summary>
        /// Crea el fallo.
        /// </summary>
        /// <param name="hookCode">Codigo que devolvio el gancho.</param>
        /// <param name="message">Mensaje legible.</param>
        public TransitionVetoedFailure(string hookCode, string message)
        {
            HookCode = hookCode;
            Message = message;
        }

        /// <summary>Codigo que devolvio el gancho (<c>tickets.checklist_open</c>).</summary>
        public string HookCode { get; }

        /// <summary>Mensaje legible.</summary>
        public string Message { get; }
    }

    /// <summary>
    /// <c>ExecuteAsync</c> con origen igual a destino: exito sin datos (<see cref="ISuccess"/>, no
    /// <see cref="ISuccess{T}"/>). No corrieron ganchos, no se llamo a <c>apply</c> y no hay registro que guardar.
    /// </summary>
    /// <typeparam name="T">Tipo del exito con datos.</typeparam>
    public sealed class TransitionNoChangeResult<T> : Result<T>, ISuccess
    { }

    /// <summary>
    /// Codigos estables de fallo de la maquina de estados. Son contrato publico.
    /// </summary>
    public static class StateMachineCodes
    {
        /// <summary>Transicion no declarada.</summary>
        public const string NotDeclared = "states.not_declared";

        /// <summary>Actor sin la transicion concedida.</summary>
        public const string Denied = "states.denied";

        /// <summary>Motivo faltante o fuera de largo.</summary>
        public const string ReasonRequired = "states.reason_required";

        /// <summary>Guarda o gancho que fallo sin dar codigo propio.</summary>
        public const string Vetoed = "states.vetoed";

        /// <summary>Definicion: estado no declarado citado por una transicion.</summary>
        public const string UnknownState = "states.unknown_state";

        /// <summary>Definicion: estado (valor o codigo) repetido.</summary>
        public const string DuplicateState = "states.duplicate_state";

        /// <summary>Definicion: dos transiciones con el mismo origen y destino.</summary>
        public const string DuplicateEdge = "states.duplicate_edge";

        /// <summary>Definicion: un mismo codigo con distinto destino, motivo o guarda.</summary>
        public const string InconsistentTransitionCode = "states.inconsistent_transition_code";

        /// <summary>Definicion: estado inicial no declarado.</summary>
        public const string InitialUnknown = "states.initial_unknown";

        /// <summary>Definicion: estado no alcanzable desde el inicial.</summary>
        public const string Unreachable = "states.unreachable";

        /// <summary>Definicion: guarda sin funcion registrada.</summary>
        public const string GuardUnknown = "states.guard_unknown";

        /// <summary>Definicion: transicion de un estado a si mismo.</summary>
        public const string SelfTransition = "states.self_transition";

        /// <summary>Definicion: nombre o codigo vacio o con caracteres no permitidos.</summary>
        public const string InvalidCode = "states.invalid_code";

        /// <summary>Definicion: largo minimo de motivo fuera de 1 a 1000.</summary>
        public const string InvalidReasonLength = "states.invalid_reason_length";

        /// <summary>Politica: concede una transicion que la maquina no declara.</summary>
        public const string PolicyUnknownTransition = "states.policy_unknown_transition";
    }
}
