using System.Diagnostics;
using System.Text;
using Common.Results;

namespace Common.Workflow.States
{
    /// <summary>
    /// Nombres de la telemetria de la maquina de estados, para que el producto se suscriba.
    /// </summary>
    public static class StateMachineTelemetry
    {
        /// <summary>Nombre del <see cref="ActivitySource"/>.</summary>
        public const string ActivitySourceName = "Common.Workflow.States";

        /// <summary>
        /// Nombre de la actividad que abre <c>ExecuteAsync</c>, con atributos <c>machine</c>, <c>transition</c>
        /// y <c>outcome</c>. El motivo nunca va a la traza.
        /// </summary>
        public const string TransitionActivityName = "workflow.transition";

        internal static readonly ActivitySource Source = new(ActivitySourceName);
    }

    /// <summary>
    /// Maquina de estados compilada: responde si una transicion existe, si el actor la puede hacer y si trae
    /// lo que exige (motivo, guarda), y ejecuta los ganchos en orden fijo. Inmutable y segura entre hilos: se
    /// registra como singleton por tipo de entidad.
    /// </summary>
    /// <typeparam name="TState">Tipo del estado (normalmente un enum del producto).</typeparam>
    /// <typeparam name="TContext">Datos que el producto precarga para guardas y ganchos.</typeparam>
    public sealed class StateMachine<TState, TContext> where TState : notnull
    {
        /// <summary>Largo maximo del motivo recortado.</summary>
        public const int MaxReasonLength = 1000;

        private static readonly EqualityComparer<TState> Eq = EqualityComparer<TState>.Default;

        private readonly Dictionary<TState, StateDefinition<TState>> _states;
        private readonly Dictionary<(TState From, TState To), Edge> _edges;
        private readonly Dictionary<TState, Edge[]> _outgoing;
        private readonly ITransitionHook<TState, TContext>[] _hooks;
        private readonly TransitionDecision<TState> _noChange = new(TransitionOutcome.NoChange, null);
        private readonly TransitionDecision<TState> _notDeclared = new(TransitionOutcome.NotDeclared, null,
            StateMachineCodes.NotDeclared, "La transicion no esta declarada.");

        private StateMachine(
            StateMachineDefinition<TState> definition,
            Dictionary<TState, StateDefinition<TState>> states,
            Dictionary<(TState, TState), Edge> edges,
            ITransitionHook<TState, TContext>[] hooks)
        {
            Definition = definition;
            _states = states;
            _edges = edges;
            _hooks = hooks;
            _outgoing = edges.Values
                .GroupBy(e => e.Definition.From)
                .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Order).ToArray());
        }

        /// <summary>La definicion compilada (copia de solo lectura).</summary>
        public StateMachineDefinition<TState> Definition { get; }

        /// <summary>Nombre de la maquina.</summary>
        public string Name => Definition.Name;

        /// <summary>
        /// Compila una definicion. Valida todo y reporta <b>todos</b> los errores.
        /// </summary>
        /// <param name="definition">Definicion de la maquina.</param>
        /// <param name="guards">Guardas por nombre: funciones puras, sincronas y sin E/S.</param>
        /// <param name="hooks">Ganchos en orden de ejecucion.</param>
        /// <returns>
        /// La maquina, o un <see cref="DefinitionFailure{T}"/> con codigos <c>states.*</c> (ver <see cref="StateMachineCodes"/>).
        /// </returns>
        /// <exception cref="ArgumentException">Si la definicion, sus listas o sus elementos son nulos, o hay guardas o ganchos nulos.</exception>
        public static Result<StateMachine<TState, TContext>> Compile(
            StateMachineDefinition<TState> definition,
            IReadOnlyDictionary<string, Func<TContext, GuardResult>>? guards = null,
            IReadOnlyList<ITransitionHook<TState, TContext>>? hooks = null)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (definition.States is null || definition.Transitions is null)
                throw new ArgumentException("La definicion necesita listas de estados y transiciones.", nameof(definition));
            if (definition.States.Any(s => s is null || s.State is null) || definition.Transitions.Any(t => t is null)
                || definition.Initial is null)
                throw new ArgumentException("La definicion tiene estados o transiciones nulos.", nameof(definition));
            if (guards is not null && guards.Values.Any(g => g is null))
                throw new ArgumentException("Hay guardas nulas.", nameof(guards));
            var hookArray = hooks?.ToArray() ?? [];
            if (hookArray.Any(h => h is null))
                throw new ArgumentException("Hay ganchos nulos.", nameof(hooks));

            var errors = new List<DefinitionError>();
            if (!IsValidCode(definition.Name))
                errors.Add(new(StateMachineCodes.InvalidCode, "Name", $"Nombre de maquina invalido: '{definition.Name}'."));

            var states = new Dictionary<TState, StateDefinition<TState>>(Eq);
            var codes = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < definition.States.Count; i++)
            {
                var s = definition.States[i];
                if (!IsValidCode(s.Code))
                    errors.Add(new(StateMachineCodes.InvalidCode, $"States[{i}].Code", $"Codigo de estado invalido: '{s.Code}'."));
                if (states.ContainsKey(s.State))
                    errors.Add(new(StateMachineCodes.DuplicateState, $"States[{i}].State", $"El estado {s.State} esta repetido."));
                else if (s.Code is not null && !codes.Add(s.Code))
                    errors.Add(new(StateMachineCodes.DuplicateState, $"States[{i}].Code", $"El codigo '{s.Code}' esta repetido."));
                else
                    states.Add(s.State, s);
            }

            if (!states.ContainsKey(definition.Initial))
                errors.Add(new(StateMachineCodes.InitialUnknown, "Initial", $"El estado inicial {definition.Initial} no esta declarado."));

            var edges = new Dictionary<(TState, TState), Edge>();
            var firstByCode = new Dictionary<string, TransitionDefinition<TState>>(StringComparer.Ordinal);
            for (var i = 0; i < definition.Transitions.Count; i++)
            {
                var t = definition.Transitions[i];
                var path = $"Transitions[{i}]";
                var ok = true;
                if (!IsValidCode(t.Code))
                {
                    errors.Add(new(StateMachineCodes.InvalidCode, path + ".Code", $"Codigo de transicion invalido: '{t.Code}'."));
                    ok = false;
                }
                if (t.MinReasonLength is < 1 or > MaxReasonLength)
                    errors.Add(new(StateMachineCodes.InvalidReasonLength, path + ".MinReasonLength",
                        $"El largo minimo del motivo debe estar entre 1 y {MaxReasonLength}: {t.MinReasonLength}."));
                if (t.From is null || !states.ContainsKey(t.From))
                {
                    errors.Add(new(StateMachineCodes.UnknownState, path + ".From", $"El estado {t.From} no esta declarado."));
                    ok = false;
                }
                if (t.To is null || !states.ContainsKey(t.To))
                {
                    errors.Add(new(StateMachineCodes.UnknownState, path + ".To", $"El estado {t.To} no esta declarado."));
                    ok = false;
                }
                if (ok && Eq.Equals(t.From, t.To))
                {
                    errors.Add(new(StateMachineCodes.SelfTransition, path, $"'{t.Code}' va de {t.From} a si mismo."));
                    ok = false;
                }

                Func<TContext, GuardResult>? guard = null;
                if (t.Guard is not null && (guards is null || !guards.TryGetValue(t.Guard, out guard)))
                    errors.Add(new(StateMachineCodes.GuardUnknown, path + ".Guard", $"La guarda '{t.Guard}' no esta registrada."));

                if (ok)
                {
                    if (edges.ContainsKey((t.From, t.To)))
                        errors.Add(new(StateMachineCodes.DuplicateEdge, path, $"Ya hay una transicion de {t.From} a {t.To}."));
                    else
                        edges.Add((t.From, t.To), new Edge(t, guard, i));

                    if (!firstByCode.TryGetValue(t.Code, out var first))
                        firstByCode.Add(t.Code, t);
                    else if (!Eq.Equals(first.To, t.To) || first.RequiresReason != t.RequiresReason
                             || first.MinReasonLength != t.MinReasonLength || first.Guard != t.Guard)
                        errors.Add(new(StateMachineCodes.InconsistentTransitionCode, path + ".Code",
                            $"'{t.Code}' se repite con otro destino, motivo o guarda."));
                }
            }

            if (states.ContainsKey(definition.Initial))
            {
                var reached = new HashSet<TState>(Eq) { definition.Initial };
                var pending = new Queue<TState>([definition.Initial]);
                while (pending.TryDequeue(out var state))
                {
                    foreach (var edge in edges.Values)
                    {
                        if (Eq.Equals(edge.Definition.From, state) && reached.Add(edge.Definition.To))
                            pending.Enqueue(edge.Definition.To);
                    }
                }
                for (var i = 0; i < definition.States.Count; i++)
                {
                    var s = definition.States[i].State;
                    if (!reached.Contains(s) && ReferenceEquals(states.GetValueOrDefault(s), definition.States[i]))
                        errors.Add(new(StateMachineCodes.Unreachable, $"States[{i}]", $"El estado {s} no es alcanzable desde {definition.Initial}."));
                }
            }

            if (errors.Count > 0)
                return new DefinitionFailure<StateMachine<TState, TContext>>(errors);

            var snapshot = definition with
            {
                States = Array.AsReadOnly(definition.States.ToArray()),
                Transitions = Array.AsReadOnly(definition.Transitions.ToArray()),
            };
            return Result.OK(new StateMachine<TState, TContext>(snapshot, states, edges, hookArray));
        }

        /// <summary>
        /// Si el estado pertenece a todos los conjuntos de <paramref name="set"/>. Un estado no declarado no
        /// pertenece a ninguno.
        /// </summary>
        /// <param name="state">Estado.</param>
        /// <param name="set">Conjunto (o combinacion de conjuntos).</param>
        /// <returns>Verdadero si pertenece.</returns>
        public bool IsIn(TState state, StateSet set) =>
            set != StateSet.None && state is not null && _states.TryGetValue(state, out var s) && (s.Sets & set) == set;

        /// <summary>
        /// Los estados que pertenecen al conjunto, en orden de declaracion.
        /// </summary>
        /// <param name="set">Conjunto (o combinacion de conjuntos).</param>
        /// <returns>Los estados.</returns>
        public IReadOnlyList<TState> StatesIn(StateSet set) =>
            Definition.States.Where(s => IsIn(s.State, set)).Select(s => s.State).ToArray();

        /// <summary>
        /// Valida una politica contra la maquina: cada transicion que concede tiene que estar declarada (o ser el
        /// comodin). Util para la matriz configurable por tenant que llega como JSON.
        /// </summary>
        /// <param name="policy">Politica.</param>
        /// <returns>La politica, o un <see cref="DefinitionFailure{T}"/> con <c>states.policy_unknown_transition</c>.</returns>
        public Result<TransitionPolicy> ValidatePolicy(TransitionPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentNullException.ThrowIfNull(policy.GrantsByTransition);
            var declared = Definition.Transitions.Select(t => t.Code).ToHashSet(StringComparer.Ordinal);
            var errors = policy.GrantsByTransition.Keys
                .Where(k => k != TransitionPolicy.AnyTransition && !declared.Contains(k))
                .Select(k => new DefinitionError(StateMachineCodes.PolicyUnknownTransition, $"GrantsByTransition[{k}]",
                    $"La transicion '{k}' no esta declarada en '{Name}'."))
                .ToList();
            return errors.Count > 0 ? new DefinitionFailure<TransitionPolicy>(errors) : Result.OK(policy);
        }

        /// <summary>
        /// Evalua una transicion sin ejecutarla. Orden fijo: NotDeclared, NoChange, Denied, ReasonRequired,
        /// GuardFailed. El sistema omite solo la matriz.
        /// </summary>
        /// <param name="request">Peticion.</param>
        /// <param name="policy">Matriz de permisos (deniega por omision).</param>
        /// <returns>La decision.</returns>
        /// <exception cref="ArgumentException">Si falta algo, el actor no es valido o <c>AtUtc</c> no es UTC.</exception>
        public TransitionDecision<TState> Evaluate(TransitionRequest<TState, TContext> request, TransitionPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(policy);
            ValidateActor(request.Actor);
            if (request.AtUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("AtUtc debe tener Kind = Utc.", nameof(request));
            return Decide(request.From, request.To, request.Actor, request.Reason, request.Context, policy, checkReason: true);
        }

        /// <summary>
        /// Las transiciones que <see cref="Evaluate"/> permitiria desde <paramref name="from"/>, ignorando el motivo,
        /// en orden de declaracion. Cada una dice si exige motivo, para que la UI lo pida antes de enviar.
        /// </summary>
        /// <param name="from">Estado actual.</param>
        /// <param name="actor">Actor.</param>
        /// <param name="context">Contexto para las guardas.</param>
        /// <param name="policy">Matriz de permisos.</param>
        /// <returns>Las transiciones disponibles; vacia si el estado no esta declarado.</returns>
        public IReadOnlyList<TransitionDefinition<TState>> AvailableTransitions(
            TState from, TransitionActor actor, TContext context, TransitionPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(policy);
            ValidateActor(actor);
            if (from is null || !_outgoing.TryGetValue(from, out var edges))
                return [];

            var available = new List<TransitionDefinition<TState>>(edges.Length);
            foreach (var edge in edges)
            {
                if (Decide(from, edge.Definition.To, actor, null, context, policy, checkReason: false).IsAllowed)
                    available.Add(edge.Definition);
            }
            return available;
        }

        /// <summary>
        /// Ejecuta una transicion: evalua; si se permite corre los <c>BeforeAsync</c> en orden (el primer veto
        /// detiene todo); llama a <paramref name="apply"/>; corre los <c>AfterAsync</c> en orden; devuelve el
        /// registro. Las excepciones de <paramref name="apply"/> y de los ganchos se propagan sin capturar: el
        /// producto lo envuelve en su transaccion. No reintenta.
        /// </summary>
        /// <param name="request">Peticion.</param>
        /// <param name="policy">Matriz de permisos.</param>
        /// <param name="apply">El producto cambia el estado y persiste, dentro de su transaccion.</param>
        /// <param name="ct">Token de cancelacion.</param>
        /// <returns>
        /// <see cref="SuccessResult{T}"/> con el registro; <see cref="TransitionNoChangeResult{T}"/> si origen y
        /// destino coinciden; <see cref="TransitionDeniedFailure{T}"/>, <see cref="TransitionReasonRequiredFailure{T}"/>
        /// o <see cref="TransitionVetoedFailure{T}"/> si no se aplico.
        /// </returns>
        public async Task<Result<TransitionRecord<TState>>> ExecuteAsync(
            TransitionRequest<TState, TContext> request, TransitionPolicy policy,
            Func<TState, CancellationToken, Task> apply, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(apply);
            var decision = Evaluate(request, policy);

            using var activity = StateMachineTelemetry.Source.StartActivity(StateMachineTelemetry.TransitionActivityName);
            activity?.SetTag("machine", Name);
            activity?.SetTag("transition", decision.Transition?.Code);

            switch (decision.Outcome)
            {
                case TransitionOutcome.Allowed:
                    break;
                case TransitionOutcome.NoChange:
                    activity?.SetTag("outcome", "no_change");
                    return new TransitionNoChangeResult<TransitionRecord<TState>>();
                case TransitionOutcome.ReasonRequired:
                    activity?.SetTag("outcome", "reason_required");
                    return new TransitionReasonRequiredFailure<TransitionRecord<TState>>(
                        decision.Transition!.Code, decision.FailureMessage!);
                default:
                    activity?.SetTag("outcome", OutcomeTag(decision.Outcome));
                    return new TransitionDeniedFailure<TransitionRecord<TState>>(
                        decision.Outcome, decision.FailureCode!, decision.FailureMessage!);
            }

            var transition = decision.Transition!;
            try
            {
                foreach (var hook in _hooks)
                {
                    if (!hook.AppliesTo(transition.Code)) continue;
                    var veto = await hook.BeforeAsync(request, ct).ConfigureAwait(false)
                               ?? throw new InvalidOperationException("Un gancho devolvio un GuardResult nulo.");
                    if (!veto.Passed)
                    {
                        activity?.SetTag("outcome", "vetoed");
                        return new TransitionVetoedFailure<TransitionRecord<TState>>(
                            veto.Code ?? StateMachineCodes.Vetoed, veto.Message ?? "Un gancho veto la transicion.");
                    }
                }

                await apply(request.To, ct).ConfigureAwait(false);

                var (actorId, isSystem) = request.Actor switch
                {
                    TransitionActor.User user => (user.ActorId, false),
                    TransitionActor.System system => (system.Process, true),
                    _ => throw new ArgumentException("Actor desconocido.", nameof(request)),
                };
                var record = new TransitionRecord<TState>(Name, transition.Code, request.From, request.To,
                    actorId, isSystem, TrimmedReason(request.Reason), request.AtUtc);

                foreach (var hook in _hooks)
                {
                    if (hook.AppliesTo(transition.Code))
                        await hook.AfterAsync(record, request.Context, ct).ConfigureAwait(false);
                }

                activity?.SetTag("outcome", "allowed");
                return Result.OK(record);
            }
            catch (Exception) when (MarkError(activity))
            {
                throw; // nunca se llega: MarkError devuelve false y la excepcion sigue su curso
            }
        }

        /// <summary>
        /// Diagrama <c>stateDiagram-v2</c> de Mermaid: estados por codigo y transiciones etiquetadas con su codigo
        /// (y <c>*</c> si exigen motivo), en orden de declaracion. Determinista.
        /// </summary>
        /// <returns>El diagrama, con saltos de linea <c>\n</c>.</returns>
        public string ToMermaid()
        {
            var sb = new StringBuilder("stateDiagram-v2\n");
            sb.Append("    [*] --> ").Append(_states[Definition.Initial].Code).Append('\n');
            foreach (var s in Definition.States)
                sb.Append("    ").Append(s.Code).Append('\n');
            foreach (var t in Definition.Transitions)
            {
                sb.Append("    ").Append(_states[t.From].Code).Append(" --> ").Append(_states[t.To].Code)
                  .Append(" : ").Append(t.Code);
                if (t.RequiresReason) sb.Append(" *");
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private TransitionDecision<TState> Decide(TState from, TState to, TransitionActor actor, string? reason,
            TContext context, TransitionPolicy policy, bool checkReason)
        {
            if (from is null || to is null || !_states.ContainsKey(from) || !_states.ContainsKey(to))
                return _notDeclared;
            if (Eq.Equals(from, to))
                return _noChange;
            if (!_edges.TryGetValue((from, to), out var edge))
                return _notDeclared;
            if (actor is TransitionActor.User user && !policy.IsGranted(edge.Definition.Code, user.Grants))
                return edge.Denied;
            if (checkReason && edge.Definition.RequiresReason && !IsReasonValid(reason, edge.Definition.MinReasonLength))
                return edge.ReasonRequired;
            if (edge.Guard is { } guard)
            {
                var result = guard(context) ?? throw new InvalidOperationException(
                    $"La guarda '{edge.Definition.Guard}' devolvio null.");
                if (!result.Passed)
                    return new(TransitionOutcome.GuardFailed, edge.Definition,
                        result.Code ?? StateMachineCodes.Vetoed, result.Message ?? $"No se cumple '{edge.Definition.Guard}'.");
            }
            return edge.Allowed;
        }

        private static bool IsReasonValid(string? reason, int minLength)
        {
            var length = reason.AsSpan().Trim().Length;
            return length >= minLength && length <= MaxReasonLength;
        }

        private static string? TrimmedReason(string? reason)
        {
            var trimmed = reason?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        private static void ValidateActor(TransitionActor actor)
        {
            switch (actor)
            {
                case TransitionActor.User user when !string.IsNullOrWhiteSpace(user.ActorId) && user.Grants is not null:
                case TransitionActor.System system when !string.IsNullOrWhiteSpace(system.Process):
                    return;
                default:
                    throw new ArgumentException("El actor es nulo o le falta id, grants o proceso.", nameof(actor));
            }
        }

        private static bool IsValidCode(string? code) =>
            !string.IsNullOrEmpty(code) && char.IsAsciiLetter(code[0])
            && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

        private static string OutcomeTag(TransitionOutcome outcome) => outcome switch
        {
            TransitionOutcome.NotDeclared => "not_declared",
            TransitionOutcome.Denied => "denied",
            _ => "guard_failed",
        };

        private static bool MarkError(Activity? activity)
        {
            activity?.SetTag("outcome", "error");
            activity?.SetStatus(ActivityStatusCode.Error);
            return false;
        }

        private sealed class Edge
        {
            public Edge(TransitionDefinition<TState> definition, Func<TContext, GuardResult>? guard, int order)
            {
                Definition = definition;
                Guard = guard;
                Order = order;
                Allowed = new(TransitionOutcome.Allowed, definition);
                Denied = new(TransitionOutcome.Denied, definition, StateMachineCodes.Denied,
                    $"No tienes permiso para '{definition.Code}'.");
                ReasonRequired = new(TransitionOutcome.ReasonRequired, definition, StateMachineCodes.ReasonRequired,
                    $"'{definition.Code}' exige un motivo de {definition.MinReasonLength} a {MaxReasonLength} caracteres.");
            }

            public TransitionDefinition<TState> Definition { get; }

            public Func<TContext, GuardResult>? Guard { get; }

            public int Order { get; }

            public TransitionDecision<TState> Allowed { get; }

            public TransitionDecision<TState> Denied { get; }

            public TransitionDecision<TState> ReasonRequired { get; }
        }
    }
}
