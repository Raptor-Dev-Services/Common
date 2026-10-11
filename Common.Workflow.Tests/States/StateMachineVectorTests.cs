using Common.Results;
using Common.Workflow.States;
using static Common.Workflow.Tests.States.TicketMachine;

namespace Common.Workflow.Tests.States;

/// <summary>C02 §12: los doce vectores sobre la maquina de referencia de M01.</summary>
public class StateMachineVectorTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task V1_el_tecnico_resuelve_con_solucion_y_se_registra()
    {
        var machine = Compile();
        var request = Request(Ticket.Assigned, Ticket.Solved, Technician);
        var applied = new List<Ticket>();

        var decision = machine.Evaluate(request, Policy());
        var result = await machine.ExecuteAsync(request, Policy(), (to, _) => { applied.Add(to); return Task.CompletedTask; }, Ct);

        Assert.Equal(TransitionOutcome.Allowed, decision.Outcome);
        Assert.True(decision.IsAllowed);
        Assert.Equal([Ticket.Solved], applied);
        var record = Assert.IsType<SuccessResult<TransitionRecord<Ticket>>>(result).Data;
        Assert.Equal(new TransitionRecord<Ticket>("ticket", "solve", Ticket.Assigned, Ticket.Solved, "tec-1", false, null, At), record);
    }

    [Fact]
    public void V2_el_solicitante_acepta_la_solucion()
    {
        var decision = Compile().Evaluate(Request(Ticket.Solved, Ticket.Closed, Requester), Policy());

        Assert.Equal(TransitionOutcome.Allowed, decision.Outcome);
        Assert.Equal("accept_solution", decision.Transition!.Code);
    }

    [Fact]
    public void V3_el_solicitante_no_puede_cerrar()
    {
        var decision = Compile().Evaluate(Request(Ticket.Assigned, Ticket.Closed, Requester, "ya no lo necesito"), Policy());

        Assert.Equal(TransitionOutcome.Denied, decision.Outcome);
        Assert.Equal("states.denied", decision.FailureCode);
        Assert.Equal("close", decision.Transition!.Code);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    public void V4_rechazar_la_solucion_sin_motivo_pide_motivo(string? motivo)
    {
        var decision = Compile().Evaluate(Request(Ticket.Solved, Ticket.Assigned, Requester, motivo), Policy());

        Assert.Equal(TransitionOutcome.ReasonRequired, decision.Outcome);
        Assert.Equal("states.reason_required", decision.FailureCode);
    }

    [Fact]
    public async Task V5_rechazar_con_motivo_corre_el_gancho_una_vez()
    {
        var log = new List<string>();
        var markSolutionRefused = new RecordingHook("MarkSolutionRefused", log, only: "reject_solution");
        var machine = Compile(hooks: [markSolutionRefused]);
        var request = Request(Ticket.Solved, Ticket.Assigned, Requester, "  Sigue sin encender la caminadora 3 ");

        var result = await machine.ExecuteAsync(request, Policy(), (_, _) => Task.CompletedTask, Ct);

        Assert.Equal(TransitionOutcome.Allowed, machine.Evaluate(request, Policy()).Outcome);
        Assert.IsType<SuccessResult<TransitionRecord<Ticket>>>(result);
        var record = Assert.Single(markSolutionRefused.After);
        Assert.Equal("reject_solution", record.TransitionCode);
        Assert.Equal("Sigue sin encender la caminadora 3", record.Reason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void V6_una_transicion_no_declarada_no_la_hace_nadie(bool sistema)
    {
        var actor = sistema ? new TransitionActor.System("tickets.autoclose") : Supervisor;

        var decision = Compile().Evaluate(Request(Ticket.Closed, Ticket.Waiting, actor, "motivo"), Policy());

        Assert.Equal(TransitionOutcome.NotDeclared, decision.Outcome);
        Assert.Null(decision.Transition);
        Assert.Equal("states.not_declared", decision.FailureCode);
    }

    [Fact]
    public async Task V7_mismo_estado_no_es_cambio_y_no_corre_ganchos()
    {
        var log = new List<string>();
        var machine = Compile(hooks: [new RecordingHook("h", log)]);
        var request = Request(Ticket.Assigned, Ticket.Assigned, Technician);
        var applied = false;

        var decision = machine.Evaluate(request, Policy());
        var result = await machine.ExecuteAsync(request, Policy(), (_, _) => { applied = true; return Task.CompletedTask; }, Ct);

        Assert.Equal(TransitionOutcome.NoChange, decision.Outcome);
        Assert.IsType<TransitionNoChangeResult<TransitionRecord<Ticket>>>(result);
        Assert.IsAssignableFrom<ISuccess>(result);
        Assert.False(result is ISuccess<TransitionRecord<Ticket>>);
        Assert.Empty(log);
        Assert.False(applied);
    }

    [Fact]
    public async Task V8_el_sistema_cierra_sin_consultar_la_politica()
    {
        var machine = Compile();
        var request = Request(Ticket.Solved, Ticket.Closed, new TransitionActor.System("tickets.autoclose"));
        var vacia = PolicyOf([]);

        var decision = machine.Evaluate(request, vacia);
        var result = await machine.ExecuteAsync(request, vacia, (_, _) => Task.CompletedTask, Ct);

        Assert.Equal(TransitionOutcome.Allowed, decision.Outcome);
        var record = Assert.IsType<SuccessResult<TransitionRecord<Ticket>>>(result).Data;
        Assert.True(record.IsSystem);
        Assert.Equal("tickets.autoclose", record.ActorId);
        Assert.Equal("accept_solution", record.TransitionCode);
    }

    [Fact]
    public void V9_una_transicion_nueva_sin_politica_queda_denegada()
    {
        var states = StateList();
        states.Add(new(Ticket.Escalated, "escalated", StateSet.InProgress));
        var transitions = TransitionList();
        transitions.Add(new("escalate", Ticket.Assigned, Ticket.Escalated));
        var machine = Compile(new StateMachineDefinition<Ticket>("ticket", Ticket.New, states, transitions));

        var tecnico = machine.Evaluate(Request(Ticket.Assigned, Ticket.Escalated, Technician), Policy());
        var sistema = machine.Evaluate(Request(Ticket.Assigned, Ticket.Escalated, new TransitionActor.System("sla.escalate")), Policy());

        Assert.Equal(TransitionOutcome.Denied, tecnico.Outcome);   // en GLPI se habria permitido por clave ausente
        Assert.Equal(TransitionOutcome.Allowed, sistema.Outcome);  // coste aceptado: solo el sistema
    }

    [Fact]
    public void V10_resolver_sin_solucion_falla_la_guarda()
    {
        var decision = Compile().Evaluate(Request(Ticket.Assigned, Ticket.Solved, Technician, hasSolution: false), Policy());

        Assert.Equal(TransitionOutcome.GuardFailed, decision.Outcome);
        Assert.Equal("tickets.solution_required", decision.FailureCode);
        Assert.Equal("Escribe la solucion antes de resolver.", decision.FailureMessage);
    }

    [Fact]
    public async Task V11_un_veto_en_BeforeAsync_impide_aplicar()
    {
        var log = new List<string>();
        var checklist = new RecordingHook("checklist", log,
            veto: GuardResult.Fail("tickets.checklist_open", "Hay pendientes en la lista de verificacion."));
        var despues = new RecordingHook("despues", log);
        var machine = Compile(hooks: [checklist, despues]);
        var applied = false;

        var result = await machine.ExecuteAsync(Request(Ticket.Assigned, Ticket.Solved, Technician), Policy(),
            (_, _) => { applied = true; return Task.CompletedTask; }, Ct);

        var vetoed = Assert.IsType<TransitionVetoedFailure<TransitionRecord<Ticket>>>(result);
        Assert.IsAssignableFrom<IConflictFailure>(result);
        Assert.Equal("tickets.checklist_open", vetoed.HookCode);
        Assert.Equal("Hay pendientes en la lista de verificacion.", vetoed.Message);
        Assert.False(applied);
        Assert.Equal(["checklist.before"], log);   // el veto detiene a los siguientes y no hay AfterAsync
    }

    [Fact]
    public void V12_una_transicion_a_un_estado_no_declarado_no_compila()
    {
        var transitions = TransitionList();
        transitions[1] = new("plan", Ticket.Assigned, Ticket.Scheduled);

        var result = StateMachine<Ticket, TicketContext>.Compile(
            new StateMachineDefinition<Ticket>("ticket", Ticket.New, StateList(), transitions), Guards);

        var failure = Assert.IsType<DefinitionFailure<StateMachine<Ticket, TicketContext>>>(result);
        Assert.Equal(("states.unknown_state", "Transitions[1].To"), (failure.Errors[0].Code, failure.Errors[0].Path));
        // Sin 'plan', 'planned' ya no se alcanza: tambien se reporta (se reportan todos los errores).
        Assert.Equal(("states.unreachable", "States[3]"), (failure.Errors[1].Code, failure.Errors[1].Path));
        Assert.Equal(2, failure.Errors.Count);
    }
}
