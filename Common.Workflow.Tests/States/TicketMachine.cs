using Common.Results;
using Common.Workflow.States;

namespace Common.Workflow.Tests.States;

public enum Ticket { New, Approval, Assigned, Planned, Waiting, Solved, Closed, Escalated, Scheduled }

public sealed record TicketContext(bool HasSolution);

/// <summary>
/// La maquina de referencia de M01 (SRS C02 §5) y la politica por omision de los vectores (§12).
/// </summary>
/// <remarks>
/// El SRS no declara ninguna transicion que llegue a <c>approval</c>, y con eso la maquina no compilaria
/// (<c>states.unreachable</c>). Se anaden <c>request_approval</c> (new -> approval) y <c>approve</c>
/// (approval -> assigned) al final para que la maquina sea coherente; ningun vector las usa.
/// </remarks>
public static class TicketMachine
{
    public static readonly DateTime At = new(2026, 11, 17, 17, 0, 0, DateTimeKind.Utc);

    public static readonly IReadOnlyDictionary<string, Func<TicketContext, GuardResult>> Guards =
        new Dictionary<string, Func<TicketContext, GuardResult>>
        {
            ["has_solution"] = c => c.HasSolution
                ? GuardResult.Ok
                : GuardResult.Fail("tickets.solution_required", "Escribe la solucion antes de resolver."),
        };

    public static List<StateDefinition<Ticket>> StateList() =>
    [
        new(Ticket.New, "new", StateSet.New),
        new(Ticket.Approval, "approval", StateSet.New),
        new(Ticket.Assigned, "assigned", StateSet.InProgress),
        new(Ticket.Planned, "planned", StateSet.InProgress),
        new(Ticket.Waiting, "waiting", StateSet.Paused | StateSet.Reopenable),
        new(Ticket.Solved, "solved", StateSet.Solved | StateSet.Reopenable),
        new(Ticket.Closed, "closed", StateSet.Closed | StateSet.Reopenable),
    ];

    public static List<TransitionDefinition<Ticket>> TransitionList() =>
    [
        new("assign", Ticket.New, Ticket.Assigned),
        new("plan", Ticket.Assigned, Ticket.Planned),
        new("pause", Ticket.Assigned, Ticket.Waiting, RequiresReason: true),
        new("pause", Ticket.Planned, Ticket.Waiting, RequiresReason: true),
        new("resume", Ticket.Waiting, Ticket.Assigned),
        new("solve", Ticket.Assigned, Ticket.Solved, Guard: "has_solution"),
        new("solve", Ticket.Planned, Ticket.Solved, Guard: "has_solution"),
        new("accept_solution", Ticket.Solved, Ticket.Closed),
        new("reject_solution", Ticket.Solved, Ticket.Assigned, RequiresReason: true),
        new("reopen", Ticket.Closed, Ticket.Assigned, RequiresReason: true),
        new("close", Ticket.New, Ticket.Closed, RequiresReason: true),
        new("close", Ticket.Assigned, Ticket.Closed, RequiresReason: true),
        new("close", Ticket.Planned, Ticket.Closed, RequiresReason: true),
        new("close", Ticket.Waiting, Ticket.Closed, RequiresReason: true),
        new("request_approval", Ticket.New, Ticket.Approval),
        new("approve", Ticket.Approval, Ticket.Assigned),
    ];

    public static StateMachineDefinition<Ticket> Definition() => new("ticket", Ticket.New, StateList(), TransitionList());

    public static TransitionPolicy Policy() => PolicyOf(new()
    {
        ["assign"] = ["supervisor"],
        ["plan"] = ["technician", "supervisor"],
        ["pause"] = ["technician", "supervisor"],
        ["resume"] = ["technician", "supervisor"],
        ["solve"] = ["technician", "supervisor"],
        ["accept_solution"] = ["requester"],
        ["reject_solution"] = ["requester"],
        ["reopen"] = ["supervisor"],
        ["close"] = ["supervisor"],
        ["request_approval"] = ["supervisor"],
        ["approve"] = ["supervisor"],
    });

    public static TransitionPolicy PolicyOf(Dictionary<string, string[]> grants) =>
        new(grants.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<string>)kv.Value.ToHashSet()));

    public static readonly TransitionActor Technician = new TransitionActor.User("tec-1", new HashSet<string> { "technician" });
    public static readonly TransitionActor Supervisor = new TransitionActor.User("sup-1", new HashSet<string> { "supervisor" });
    public static readonly TransitionActor Requester = new TransitionActor.User("req-1", new HashSet<string> { "requester" });

    public static StateMachine<Ticket, TicketContext> Compile(
        StateMachineDefinition<Ticket>? definition = null,
        IReadOnlyList<ITransitionHook<Ticket, TicketContext>>? hooks = null)
    {
        var result = StateMachine<Ticket, TicketContext>.Compile(definition ?? Definition(), Guards, hooks);
        return Assert.IsType<SuccessResult<StateMachine<Ticket, TicketContext>>>(result).Data;
    }

    public static TransitionRequest<Ticket, TicketContext> Request(
        Ticket from, Ticket to, TransitionActor actor, string? reason = null, bool hasSolution = true) =>
        new(from, to, actor, reason, new TicketContext(hasSolution), At);
}

/// <summary>Gancho de prueba que anota lo que le pasa y puede vetar.</summary>
public sealed class RecordingHook(string name, List<string> log, string? only = null, GuardResult? veto = null)
    : ITransitionHook<Ticket, TicketContext>
{
    public List<TransitionRecord<Ticket>> After { get; } = [];

    public bool AppliesTo(string transitionCode) => only is null || only == transitionCode;

    public ValueTask<GuardResult> BeforeAsync(TransitionRequest<Ticket, TicketContext> request, CancellationToken ct)
    {
        log.Add($"{name}.before");
        return ValueTask.FromResult(veto ?? GuardResult.Ok);
    }

    public ValueTask AfterAsync(TransitionRecord<Ticket> record, TicketContext context, CancellationToken ct)
    {
        log.Add($"{name}.after");
        After.Add(record);
        return ValueTask.CompletedTask;
    }
}
