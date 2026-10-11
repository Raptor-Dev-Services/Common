using System.Diagnostics;
using Common.Results;
using Common.Workflow.States;
using static Common.Workflow.Tests.States.TicketMachine;

namespace Common.Workflow.Tests.States;

/// <summary>C02 REQ-002 a 009: evaluacion, matriz, motivo, guardas, ganchos y transiciones disponibles.</summary>
public class StateMachineBehaviorTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly Func<Ticket, CancellationToken, Task> Nada = (_, _) => Task.CompletedTask;

    [Fact]
    public void Estados_no_declarados_son_NotDeclared_incluso_si_coinciden()
    {
        var machine = Compile();

        Assert.Equal(TransitionOutcome.NotDeclared, machine.Evaluate(Request(Ticket.Scheduled, Ticket.Scheduled, Supervisor), Policy()).Outcome);
        Assert.Equal(TransitionOutcome.NotDeclared, machine.Evaluate(Request(Ticket.Assigned, Ticket.Scheduled, Supervisor), Policy()).Outcome);
    }

    [Fact]
    public void El_orden_de_evaluacion_es_fijo()
    {
        var machine = Compile();
        var sinNada = PolicyOf([]);

        // Denegado gana a motivo faltante y a guarda fallida.
        Assert.Equal(TransitionOutcome.Denied,
            machine.Evaluate(Request(Ticket.Assigned, Ticket.Solved, Requester, hasSolution: false), Policy()).Outcome);
        // Motivo faltante gana a guarda (el sistema no consulta la politica).
        var definition = Definition() with
        {
            Transitions = [.. TransitionList().Select(t => t.Code == "reopen" ? t with { Guard = "has_solution" } : t)],
        };
        var conGuarda = Compile(definition);
        Assert.Equal(TransitionOutcome.ReasonRequired,
            conGuarda.Evaluate(Request(Ticket.Closed, Ticket.Assigned, new TransitionActor.System("p"), null, hasSolution: false), sinNada).Outcome);
        Assert.Equal(TransitionOutcome.GuardFailed,
            conGuarda.Evaluate(Request(Ticket.Closed, Ticket.Assigned, new TransitionActor.System("p"), "motivo", hasSolution: false), sinNada).Outcome);
    }

    [Fact]
    public void El_comodin_concede_todas_las_transiciones_declaradas()
    {
        var machine = Compile();
        var policy = PolicyOf(new() { ["*"] = ["platform.superadmin"] });
        var admin = new TransitionActor.User("root", new HashSet<string> { "platform.superadmin" });

        Assert.Equal(TransitionOutcome.Allowed, machine.Evaluate(Request(Ticket.Assigned, Ticket.Closed, admin, "limpieza"), policy).Outcome);
        Assert.Equal(TransitionOutcome.NotDeclared, machine.Evaluate(Request(Ticket.Closed, Ticket.Waiting, admin, "x"), policy).Outcome);
        Assert.Equal(TransitionOutcome.Denied, machine.Evaluate(Request(Ticket.Assigned, Ticket.Closed, Supervisor, "x"), policy).Outcome);
    }

    [Fact]
    public void La_politica_deniega_por_omision()
    {
        var policy = PolicyOf(new() { ["close"] = ["supervisor"], ["solve"] = [] });

        Assert.True(policy.IsGranted("close", new HashSet<string> { "otro", "supervisor" }));
        Assert.False(policy.IsGranted("close", new HashSet<string> { "Supervisor" }));   // ordinal
        Assert.False(policy.IsGranted("solve", new HashSet<string> { "supervisor" }));
        Assert.False(policy.IsGranted("reopen", new HashSet<string> { "supervisor" }));
        Assert.False(policy.IsGranted("close", new HashSet<string>()));
        Assert.False(new TransitionPolicy(new Dictionary<string, IReadOnlySet<string>> { ["close"] = null! })
            .IsGranted("close", new HashSet<string> { "supervisor" }));
        Assert.Throws<ArgumentNullException>(() => policy.IsGranted(null!, new HashSet<string>()));
        Assert.Throws<ArgumentNullException>(() => policy.IsGranted("close", null!));
    }

    [Theory]
    [InlineData("a", 1, true)]
    [InlineData(" a ", 1, true)]
    [InlineData("abcd", 5, false)]
    [InlineData("  abcd  ", 5, false)]
    [InlineData("abcde", 5, true)]
    public void El_motivo_se_mide_recortado_contra_el_minimo(string motivo, int minimo, bool permitido)
    {
        var definition = Definition() with
        {
            Transitions = [.. TransitionList().Select(t => t.Code == "reopen" ? t with { MinReasonLength = minimo } : t)],
        };

        var decision = Compile(definition).Evaluate(Request(Ticket.Closed, Ticket.Assigned, Supervisor, motivo), Policy());

        Assert.Equal(permitido ? TransitionOutcome.Allowed : TransitionOutcome.ReasonRequired, decision.Outcome);
    }

    [Fact]
    public void El_motivo_de_mas_de_1000_caracteres_se_rechaza()
    {
        var machine = Compile();

        Assert.True(machine.Evaluate(Request(Ticket.Closed, Ticket.Assigned, Supervisor, new string('x', 1000)), Policy()).IsAllowed);
        Assert.Equal(TransitionOutcome.ReasonRequired,
            machine.Evaluate(Request(Ticket.Closed, Ticket.Assigned, Supervisor, new string('x', 1001)), Policy()).Outcome);
    }

    [Fact]
    public void El_sistema_tambien_debe_dar_motivo()
    {
        var machine = Compile();
        var sistema = new TransitionActor.System("tickets.autoclose");

        Assert.Equal(TransitionOutcome.ReasonRequired,
            machine.Evaluate(Request(Ticket.Waiting, Ticket.Closed, sistema), Policy()).Outcome);
        Assert.True(machine.Evaluate(Request(Ticket.Waiting, Ticket.Closed, sistema, "Cierre automatico: 3 dias habiles resuelto"), Policy()).IsAllowed);
    }

    [Fact]
    public void El_sistema_respeta_las_guardas()
    {
        var decision = Compile().Evaluate(
            Request(Ticket.Assigned, Ticket.Solved, new TransitionActor.System("p"), hasSolution: false), PolicyOf([]));

        Assert.Equal(TransitionOutcome.GuardFailed, decision.Outcome);
    }

    [Fact]
    public void Una_guarda_sin_codigo_usa_el_codigo_generico()
    {
        var guards = new Dictionary<string, Func<TicketContext, GuardResult>> { ["has_solution"] = _ => new GuardResult(false) };
        var machine = Assert.IsType<SuccessResult<StateMachine<Ticket, TicketContext>>>(
            StateMachine<Ticket, TicketContext>.Compile(Definition(), guards)).Data;

        var decision = machine.Evaluate(Request(Ticket.Assigned, Ticket.Solved, Technician), Policy());

        Assert.Equal(("states.vetoed", "No se cumple 'has_solution'."), (decision.FailureCode, decision.FailureMessage));
    }

    [Fact]
    public void Una_guarda_que_devuelve_null_es_error_del_programador()
    {
        var guards = new Dictionary<string, Func<TicketContext, GuardResult>> { ["has_solution"] = _ => null! };
        var machine = Assert.IsType<SuccessResult<StateMachine<Ticket, TicketContext>>>(
            StateMachine<Ticket, TicketContext>.Compile(Definition(), guards)).Data;

        Assert.Throws<InvalidOperationException>(() => machine.Evaluate(Request(Ticket.Assigned, Ticket.Solved, Technician), Policy()));
    }

    [Fact]
    public void Errores_del_programador_al_evaluar()
    {
        var machine = Compile();
        var request = Request(Ticket.New, Ticket.Assigned, Supervisor);

        Assert.Throws<ArgumentNullException>(() => machine.Evaluate(null!, Policy()));
        Assert.Throws<ArgumentNullException>(() => machine.Evaluate(request, null!));
        Assert.Throws<ArgumentException>(() => machine.Evaluate(request with { AtUtc = DateTime.SpecifyKind(At, DateTimeKind.Local) }, Policy()));
        Assert.Throws<ArgumentException>(() => machine.Evaluate(request with { AtUtc = DateTime.SpecifyKind(At, DateTimeKind.Unspecified) }, Policy()));
        Assert.Throws<ArgumentException>(() => machine.Evaluate(request with { Actor = null! }, Policy()));
        Assert.Throws<ArgumentException>(() => machine.Evaluate(request with { Actor = new TransitionActor.User(" ", new HashSet<string>()) }, Policy()));
        Assert.Throws<ArgumentException>(() => machine.Evaluate(request with { Actor = new TransitionActor.User("u", null!) }, Policy()));
        Assert.Throws<ArgumentException>(() => machine.Evaluate(request with { Actor = new TransitionActor.System("") }, Policy()));
    }

    [Fact]
    public async Task Ganchos_en_orden_de_registro_y_filtrados_por_transicion()
    {
        var log = new List<string>();
        var machine = Compile(hooks:
        [
            new RecordingHook("a", log),
            new RecordingHook("solo_reabrir", log, only: "reopen"),
            new RecordingHook("b", log),
        ]);

        var result = await machine.ExecuteAsync(Request(Ticket.Assigned, Ticket.Solved, Technician), Policy(),
            (_, _) => { log.Add("apply"); return Task.CompletedTask; }, Ct);

        Assert.IsType<SuccessResult<TransitionRecord<Ticket>>>(result);
        Assert.Equal(["a.before", "b.before", "apply", "a.after", "b.after"], log);
    }

    [Fact]
    public async Task Un_gancho_con_los_miembros_por_omision_no_estorba()
    {
        var machine = Compile(hooks: [new HookVacio()]);

        var result = await machine.ExecuteAsync(Request(Ticket.New, Ticket.Assigned, Supervisor), Policy(), Nada, Ct);

        Assert.IsType<SuccessResult<TransitionRecord<Ticket>>>(result);
    }

    [Fact]
    public async Task Un_veto_sin_codigo_usa_el_codigo_generico()
    {
        var machine = Compile(hooks: [new RecordingHook("v", [], veto: new GuardResult(false))]);

        var result = await machine.ExecuteAsync(Request(Ticket.New, Ticket.Assigned, Supervisor), Policy(), Nada, Ct);

        var vetoed = Assert.IsType<TransitionVetoedFailure<TransitionRecord<Ticket>>>(result);
        Assert.Equal(("states.vetoed", "Un gancho veto la transicion."), (vetoed.HookCode, vetoed.Message));
    }

    [Fact]
    public async Task Un_gancho_que_devuelve_null_es_error_del_programador()
    {
        var machine = Compile(hooks: [new HookNulo()]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            machine.ExecuteAsync(Request(Ticket.New, Ticket.Assigned, Supervisor), Policy(), Nada, Ct));
    }

    [Fact]
    public async Task Si_apply_lanza_la_excepcion_se_propaga_y_no_corren_los_AfterAsync()
    {
        var log = new List<string>();
        var machine = Compile(hooks: [new RecordingHook("h", log)]);

        await Assert.ThrowsAsync<TimeoutException>(() => machine.ExecuteAsync(
            Request(Ticket.New, Ticket.Assigned, Supervisor), Policy(), (_, _) => throw new TimeoutException(), Ct));

        Assert.Equal(["h.before"], log);
    }

    [Fact]
    public async Task Si_un_AfterAsync_lanza_la_excepcion_se_propaga()
    {
        var machine = Compile(hooks: [new HookQueFallaDespues()]);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            machine.ExecuteAsync(Request(Ticket.New, Ticket.Assigned, Supervisor), Policy(), Nada, Ct));
    }

    [Theory]
    [InlineData(Ticket.Closed, Ticket.Waiting, "Supervisor", null, TransitionOutcome.NotDeclared, "states.not_declared")]
    [InlineData(Ticket.Assigned, Ticket.Closed, "Requester", "x", TransitionOutcome.Denied, "states.denied")]
    [InlineData(Ticket.Assigned, Ticket.Solved, "Technician", null, TransitionOutcome.GuardFailed, "tickets.solution_required")]
    public async Task Los_fallos_de_evaluacion_son_conflictos_con_su_resultado(
        Ticket from, Ticket to, string actor, string? reason, TransitionOutcome outcome, string code)
    {
        var who = actor switch { "Supervisor" => Supervisor, "Requester" => Requester, _ => Technician };
        var applied = false;

        var result = await Compile().ExecuteAsync(Request(from, to, who, reason, hasSolution: false), Policy(),
            (_, _) => { applied = true; return Task.CompletedTask; }, Ct);

        var denied = Assert.IsType<TransitionDeniedFailure<TransitionRecord<Ticket>>>(result);
        Assert.IsAssignableFrom<IConflictFailure>(result);
        Assert.Equal((outcome, code), (denied.Outcome, denied.Code));
        Assert.False(string.IsNullOrEmpty(denied.Message));
        Assert.False(applied);
    }

    [Fact]
    public async Task Falta_de_motivo_es_un_fallo_de_validacion()
    {
        var result = await Compile().ExecuteAsync(Request(Ticket.Solved, Ticket.Assigned, Requester, " "), Policy(), Nada, Ct);

        var failure = Assert.IsType<TransitionReasonRequiredFailure<TransitionRecord<Ticket>>>(result);
        Assert.IsAssignableFrom<IValidationFailure>(result);
        Assert.Equal((TransitionOutcome.ReasonRequired, "states.reason_required", "reject_solution"),
            (failure.Outcome, failure.Code, failure.TransitionCode));
        Assert.Contains("motivo", failure.Message);
    }

    [Fact]
    public async Task ExecuteAsync_exige_apply()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Compile().ExecuteAsync(Request(Ticket.New, Ticket.Assigned, Supervisor), Policy(), null!, Ct));
    }

    [Fact]
    public async Task ExecuteAsync_abre_una_actividad_sin_el_motivo()
    {
        // El listener es global: otras pruebas en paralelo tambien producen actividades de la misma
        // maquina. Se aislan las de esta prueba colgandolas de una traza padre propia.
        var activities = new System.Collections.Concurrent.ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == StateMachineTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        using var parent = new Activity("prueba").SetIdFormat(ActivityIdFormat.W3C).Start();
        var machine = Compile(hooks: [new RecordingHook("v", [], only: "plan", veto: GuardResult.Fail("x", "y"))]);

        await machine.ExecuteAsync(Request(Ticket.Solved, Ticket.Assigned, Requester, "motivo secreto"), Policy(), Nada, Ct);
        await machine.ExecuteAsync(Request(Ticket.Assigned, Ticket.Assigned, Technician), Policy(), Nada, Ct);
        await machine.ExecuteAsync(Request(Ticket.Assigned, Ticket.Closed, Requester, "x"), Policy(), Nada, Ct);
        await machine.ExecuteAsync(Request(Ticket.Assigned, Ticket.Planned, Technician), Policy(), Nada, Ct);
        await machine.ExecuteAsync(Request(Ticket.Solved, Ticket.Assigned, Requester), Policy(), Nada, Ct);
        await machine.ExecuteAsync(Request(Ticket.Closed, Ticket.Waiting, Supervisor), Policy(), Nada, Ct);
        await machine.ExecuteAsync(Request(Ticket.Assigned, Ticket.Solved, Technician, hasSolution: false), Policy(), Nada, Ct);
        await Assert.ThrowsAsync<TimeoutException>(() => machine.ExecuteAsync(
            Request(Ticket.New, Ticket.Assigned, Supervisor), Policy(), (_, _) => throw new TimeoutException(), Ct));

        var mine = activities.Where(a => a.TraceId == parent.TraceId).ToList();
        Assert.All(mine, a => Assert.Equal(StateMachineTelemetry.TransitionActivityName, a.OperationName));
        Assert.Equal(
            ["allowed", "no_change", "denied", "vetoed", "reason_required", "not_declared", "guard_failed", "error"],
            mine.Select(a => (string?)a.GetTagItem("outcome")));
        Assert.Equal("reject_solution", mine[0].GetTagItem("transition"));
        Assert.Equal(ActivityStatusCode.Error, mine[^1].Status);
        Assert.DoesNotContain(mine.SelectMany(a => a.TagObjects), t => t.Value is string s && s.Contains("secreto"));
    }

    [Fact]
    public void Transiciones_disponibles_para_pintar_botones()
    {
        var machine = Compile();
        var conSolucion = new TicketContext(true);

        Assert.Equal(["plan", "pause", "solve"],
            machine.AvailableTransitions(Ticket.Assigned, Technician, conSolucion, Policy()).Select(t => t.Code));
        Assert.Equal(["plan", "pause"],
            machine.AvailableTransitions(Ticket.Assigned, Technician, new TicketContext(false), Policy()).Select(t => t.Code));
        Assert.Equal(["plan", "pause", "solve", "close"],
            machine.AvailableTransitions(Ticket.Assigned, Supervisor, conSolucion, Policy()).Select(t => t.Code));
        Assert.True(machine.AvailableTransitions(Ticket.Assigned, Technician, conSolucion, Policy()).Single(t => t.Code == "pause").RequiresReason);
        Assert.Equal(["accept_solution"],
            machine.AvailableTransitions(Ticket.Solved, new TransitionActor.System("p"), conSolucion, PolicyOf([])).Take(1).Select(t => t.Code));
        Assert.Empty(machine.AvailableTransitions(Ticket.Scheduled, Supervisor, conSolucion, Policy()));
        Assert.Throws<ArgumentNullException>(() => machine.AvailableTransitions(Ticket.New, Supervisor, conSolucion, null!));
        Assert.Throws<ArgumentException>(() => machine.AvailableTransitions(Ticket.New, null!, conSolucion, Policy()));
    }

    [Fact]
    public void Validar_una_politica_contra_la_maquina()
    {
        var machine = Compile();

        Assert.IsType<SuccessResult<TransitionPolicy>>(machine.ValidatePolicy(Policy()));
        Assert.IsType<SuccessResult<TransitionPolicy>>(machine.ValidatePolicy(PolicyOf(new() { ["*"] = ["root"] })));
        var failure = Assert.IsType<DefinitionFailure<TransitionPolicy>>(
            machine.ValidatePolicy(PolicyOf(new() { ["close"] = ["s"], ["borrar"] = ["s"] })));
        Assert.Equal(("states.policy_unknown_transition", "GrantsByTransition[borrar]"),
            (failure.Errors.Single().Code, failure.Errors.Single().Path));
        Assert.Throws<ArgumentNullException>(() => machine.ValidatePolicy(null!));
    }

    [Fact]
    public async Task La_maquina_compilada_se_puede_usar_desde_varios_hilos()
    {
        var machine = Compile();
        var policy = Policy();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() =>
            machine.Evaluate(Request(Ticket.Assigned, i % 2 == 0 ? Ticket.Solved : Ticket.Closed, Technician), policy).Outcome)));

        Assert.Equal(100, outcomes.Count(o => o == TransitionOutcome.Allowed));
        Assert.Equal(100, outcomes.Count(o => o == TransitionOutcome.Denied));
    }

    private sealed class HookVacio : ITransitionHook<Ticket, TicketContext>;

    private sealed class HookNulo : ITransitionHook<Ticket, TicketContext>
    {
        public ValueTask<GuardResult> BeforeAsync(TransitionRequest<Ticket, TicketContext> request, CancellationToken ct) =>
            ValueTask.FromResult<GuardResult>(null!);
    }

    private sealed class HookQueFallaDespues : ITransitionHook<Ticket, TicketContext>
    {
        public ValueTask AfterAsync(TransitionRecord<Ticket> record, TicketContext context, CancellationToken ct) =>
            throw new InvalidDataException();
    }
}
