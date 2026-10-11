using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Common.Results;
using Common.Workflow.States;
using static Common.Workflow.Tests.States.TicketMachine;

namespace Common.Workflow.Tests.States;

/// <summary>C02 REQ-001, 010, 011: compilacion validada, conjuntos y exportacion a Mermaid.</summary>
public class StateMachineCompileTests
{
    private static IReadOnlyList<DefinitionError> Errors(StateMachineDefinition<Ticket> definition,
        IReadOnlyDictionary<string, Func<TicketContext, GuardResult>>? guards = null)
    {
        var result = StateMachine<Ticket, TicketContext>.Compile(definition, guards ?? Guards);
        return Assert.IsType<DefinitionFailure<StateMachine<Ticket, TicketContext>>>(result).Errors;
    }

    private static (string Code, string Path)[] Pairs(IReadOnlyList<DefinitionError> errors) =>
        errors.Select(e => (e.Code, e.Path)).ToArray();

    [Fact]
    public void La_maquina_de_referencia_compila()
    {
        var machine = Compile();

        Assert.Equal("ticket", machine.Name);
        Assert.Equal(7, machine.Definition.States.Count);
        Assert.Equal(16, machine.Definition.Transitions.Count);
    }

    [Fact]
    public void La_definicion_compilada_no_cambia_si_se_modifica_la_lista_original()
    {
        var transitions = TransitionList();
        var machine = Compile(new StateMachineDefinition<Ticket>("ticket", Ticket.New, StateList(), transitions));

        transitions.Clear();

        Assert.Equal(16, machine.Definition.Transitions.Count);
        Assert.Equal(TransitionOutcome.Allowed, machine.Evaluate(Request(Ticket.New, Ticket.Assigned, Supervisor), Policy()).Outcome);
    }

    [Fact]
    public void Estado_duplicado_por_valor_o_por_codigo()
    {
        var states = StateList();
        states.Add(new(Ticket.New, "otro", StateSet.None));
        states.Add(new(Ticket.Escalated, "closed", StateSet.None));

        Assert.Equal(
            [("states.duplicate_state", "States[7].State"), ("states.duplicate_state", "States[8].Code")],
            Pairs(Errors(new("ticket", Ticket.New, states, TransitionList()))));
    }

    [Fact]
    public void Arista_duplicada_y_codigo_inconsistente()
    {
        var transitions = TransitionList();
        transitions.Add(new("assign_again", Ticket.New, Ticket.Assigned));          // misma arista
        transitions.Add(new("pause", Ticket.New, Ticket.Waiting));                  // pause sin motivo
        transitions.Add(new("resume", Ticket.Closed, Ticket.Planned));              // otro destino
        transitions.Add(new("solve", Ticket.Waiting, Ticket.Solved));               // sin la guarda
        transitions.Add(new("reopen", Ticket.Solved, Ticket.Planned, true, 5));     // otro destino y otro largo

        Assert.Equal(
        [
            ("states.duplicate_edge", "Transitions[16]"),
            ("states.inconsistent_transition_code", "Transitions[17].Code"),
            ("states.inconsistent_transition_code", "Transitions[18].Code"),
            ("states.inconsistent_transition_code", "Transitions[19].Code"),
            ("states.inconsistent_transition_code", "Transitions[20].Code"),
        ], Pairs(Errors(new("ticket", Ticket.New, StateList(), transitions))));
    }

    [Theory]
    [InlineData(true, false, 1, null)]
    [InlineData(false, false, 2, null)]
    [InlineData(false, false, 1, "has_solution")]
    public void Un_codigo_repetido_tiene_que_compartir_motivo_largo_y_guarda(bool to, bool reason, int min, string? guard)
    {
        var transitions = TransitionList();
        transitions.Add(new("plan", Ticket.Waiting, to ? Ticket.Solved : Ticket.Planned, reason, min, guard));

        Assert.Equal([("states.inconsistent_transition_code", "Transitions[16].Code")],
            Pairs(Errors(new("ticket", Ticket.New, StateList(), transitions))));
    }

    [Fact]
    public void Inicial_desconocido_no_reporta_inalcanzables()
    {
        Assert.Equal([("states.initial_unknown", "Initial")],
            Pairs(Errors(new("ticket", Ticket.Scheduled, StateList(), TransitionList()))));
    }

    [Fact]
    public void Estado_inalcanzable_desde_el_inicial()
    {
        var transitions = TransitionList();
        transitions.RemoveAll(t => t.Code == "request_approval");

        Assert.Equal([("states.unreachable", "States[1]")],
            Pairs(Errors(new("ticket", Ticket.New, StateList(), transitions))));
    }

    [Fact]
    public void Guarda_sin_funcion_registrada()
    {
        Assert.Equal(
            [("states.guard_unknown", "Transitions[5].Guard"), ("states.guard_unknown", "Transitions[6].Guard")],
            Pairs(StateMachine<Ticket, TicketContext>.Compile(Definition()) is DefinitionFailure<StateMachine<Ticket, TicketContext>> f
                ? f.Errors : []));
        Assert.Equal(2, Errors(Definition(), new Dictionary<string, Func<TicketContext, GuardResult>>()).Count);
    }

    [Fact]
    public void Transicion_a_si_mismo_y_estado_de_origen_desconocido()
    {
        var transitions = TransitionList();
        transitions.Add(new("stay", Ticket.Assigned, Ticket.Assigned));
        transitions.Add(new("from_nowhere", Ticket.Scheduled, Ticket.Assigned));

        Assert.Equal(
            [("states.self_transition", "Transitions[16]"), ("states.unknown_state", "Transitions[17].From")],
            Pairs(Errors(new("ticket", Ticket.New, StateList(), transitions))));
    }

    [Fact]
    public void Codigos_invalidos_y_largo_de_motivo_fuera_de_rango()
    {
        var states = StateList();
        states[0] = new(Ticket.New, "1new", StateSet.New);
        var transitions = TransitionList();
        transitions.Add(new("con espacio", Ticket.Waiting, Ticket.Solved));
        transitions.Add(new("long_reason", Ticket.Waiting, Ticket.Planned, true, 0));
        transitions.Add(new("huge_reason", Ticket.Planned, Ticket.Assigned, true, 1001));

        Assert.Equal(
        [
            ("states.invalid_code", "Name"),
            ("states.invalid_code", "States[0].Code"),
            ("states.invalid_code", "Transitions[16].Code"),
            ("states.invalid_reason_length", "Transitions[17].MinReasonLength"),
            ("states.invalid_reason_length", "Transitions[18].MinReasonLength"),
        ], Pairs(Errors(new("", Ticket.New, states, transitions))));
    }

    [Fact]
    public void Reporta_todos_los_errores_a_la_vez()
    {
        var transitions = TransitionList();
        transitions[1] = new("plan", Ticket.Assigned, Ticket.Scheduled);
        transitions.Add(new("stay", Ticket.Closed, Ticket.Closed));

        var errors = Errors(new("ticket", Ticket.New, StateList(), transitions));

        Assert.Equal(["states.unknown_state", "states.self_transition", "states.unreachable"], errors.Select(e => e.Code));
    }

    [Fact]
    public void Nulos_son_error_del_programador()
    {
        var def = Definition();
        Assert.Throws<ArgumentNullException>(() => StateMachine<Ticket, TicketContext>.Compile(null!));
        Assert.Throws<ArgumentException>(() => StateMachine<Ticket, TicketContext>.Compile(def with { States = null! }));
        Assert.Throws<ArgumentException>(() => StateMachine<Ticket, TicketContext>.Compile(def with { Transitions = [null!] }));
        Assert.Throws<ArgumentException>(() => StateMachine<Ticket, TicketContext>.Compile(def, new Dictionary<string, Func<TicketContext, GuardResult>> { ["x"] = null! }));
        Assert.Throws<ArgumentException>(() => StateMachine<Ticket, TicketContext>.Compile(def, Guards, [null!]));
        Assert.Throws<ArgumentException>(() => StateMachine<string, int>.Compile(
            new StateMachineDefinition<string>("m", null!, [new("a", "a", StateSet.None)], [])));
    }

    [Fact]
    public void Compila_30_estados_y_100_transiciones_en_menos_de_1_ms()
    {
        var states = Enumerable.Range(0, 30).Select(i => new StateDefinition<int>(i, $"s{i}", StateSet.None)).ToList();
        var transitions = Enumerable.Range(0, 100)
            .Select(i => new TransitionDefinition<int>($"t{i}", i % 30, (i % 30 + 1 + i / 30) % 30))
            .ToList();
        var definition = new StateMachineDefinition<int>("grande", 0, states, transitions);
        Assert.IsType<SuccessResult<StateMachine<int, object>>>(StateMachine<int, object>.Compile(definition));

        var mejor = TimeSpan.MaxValue;
        for (var i = 0; i < 20; i++)
        {
            var sw = Stopwatch.StartNew();
            StateMachine<int, object>.Compile(definition);
            sw.Stop();
            if (sw.Elapsed < mejor) mejor = sw.Elapsed;
        }

        Assert.True(mejor < TimeSpan.FromMilliseconds(1), $"Tardo {mejor.TotalMilliseconds} ms");
    }

    [Fact]
    public void Conjuntos_de_estados()
    {
        var machine = Compile();

        Assert.Equal([Ticket.Waiting, Ticket.Solved, Ticket.Closed], machine.StatesIn(StateSet.Reopenable));
        Assert.True(machine.IsIn(Ticket.Approval, StateSet.New));
        Assert.Equal([Ticket.New, Ticket.Approval], machine.StatesIn(StateSet.New));
        Assert.Equal([Ticket.Assigned, Ticket.Planned], machine.StatesIn(StateSet.InProgress));
        Assert.Equal([Ticket.Waiting], machine.StatesIn(StateSet.Paused));
        Assert.Equal([Ticket.Solved], machine.StatesIn(StateSet.Solved));
        Assert.Equal([Ticket.Closed], machine.StatesIn(StateSet.Closed));
        Assert.Equal([Ticket.Waiting], machine.StatesIn(StateSet.Paused | StateSet.Reopenable));
        Assert.Empty(machine.StatesIn(StateSet.None));
        Assert.False(machine.IsIn(Ticket.Assigned, StateSet.New));
        Assert.False(machine.IsIn(Ticket.Scheduled, StateSet.New));   // no declarado
    }

    [Fact]
    public void Mermaid_es_determinista_y_marca_el_motivo()
    {
        const string expected = """
            stateDiagram-v2
                [*] --> new
                new
                approval
                assigned
                planned
                waiting
                solved
                closed
                new --> assigned : assign
                assigned --> planned : plan
                assigned --> waiting : pause *
                planned --> waiting : pause *
                waiting --> assigned : resume
                assigned --> solved : solve
                planned --> solved : solve
                solved --> closed : accept_solution
                solved --> assigned : reject_solution *
                closed --> assigned : reopen *
                new --> closed : close *
                assigned --> closed : close *
                planned --> closed : close *
                waiting --> closed : close *
                new --> approval : request_approval
                approval --> assigned : approve

            """;

        Assert.Equal(expected.ReplaceLineEndings("\n"), Compile().ToMermaid());
        Assert.Equal(Compile().ToMermaid(), Compile().ToMermaid());
    }

    [Fact]
    public void La_definicion_y_la_politica_se_serializan_con_enums_como_texto()
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var definition = Definition();

        var json = JsonSerializer.Serialize(definition, options);
        var read = JsonSerializer.Deserialize<StateMachineDefinition<Ticket>>(json, options)!;

        Assert.Contains("\"Initial\":\"New\"", json);
        Assert.Contains("\"Sets\":\"Paused, Reopenable\"", json);
        Assert.Equal(definition.States, read.States);
        Assert.Equal(definition.Transitions, read.Transitions);
        Assert.IsType<SuccessResult<StateMachine<Ticket, TicketContext>>>(StateMachine<Ticket, TicketContext>.Compile(read, Guards));

        var policyJson = JsonSerializer.Serialize(Policy());
        var policy = JsonSerializer.Deserialize<TransitionPolicy>(policyJson)!;
        Assert.True(policy.IsGranted("close", new HashSet<string> { "supervisor" }));
        Assert.False(policy.IsGranted("close", new HashSet<string> { "requester" }));
        Assert.Contains("\"close\":[\"supervisor\"]", policyJson);
        Assert.StartsWith("{\"GrantsByTransition\":{", policyJson);
        Assert.Empty(JsonSerializer.Deserialize<TransitionPolicy>("{}")!.GrantsByTransition);
        Assert.Empty(JsonSerializer.Deserialize<TransitionPolicy>(
            """{"GrantsByTransition":{"close":null}}""")!.GrantsByTransition["close"]);
        Assert.Equal("""{"GrantsByTransition":{"close":[]}}""", JsonSerializer.Serialize(
            new TransitionPolicy(new Dictionary<string, IReadOnlySet<string>> { ["close"] = null! })));
        Assert.Null(JsonSerializer.Deserialize<TransitionPolicy>("null"));
        Assert.Contains("\"Denied\"", JsonSerializer.Serialize(TransitionOutcome.Denied));
    }
}
