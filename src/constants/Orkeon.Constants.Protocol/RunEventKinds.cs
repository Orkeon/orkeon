namespace Orkeon.Constants.Protocol;

/// <summary>
/// The event kinds a run streams as it executes.
/// <para>
/// This is a wire protocol between processes: the CLI runner writes the stream as JSON lines,
/// and whatever watches the run - Orkeon Studio, a script, a terminal - reads it. Producer and
/// consumer live in projects that cannot reference each other, so the vocabulary was written
/// twice, and nothing checked the two spellings against each other.
/// </para>
/// <para>
/// The two copies had already drifted: the runner emitted <c>tool.called</c>,
/// <c>tool.returned</c>, <c>delegation.started</c> and <c>agent.spawned</c>, and Studio's copy
/// knew none of them. An unknown kind is not an error on either side - the reader ignores it -
/// so the drift showed up as a run whose tool activity and delegations simply never appeared on
/// screen, with nothing anywhere reporting a problem. That is why the set below is exhaustive
/// and exposed as <see cref="All"/>: a consumer can assert it handles every kind, which is the
/// check the two copies could not perform.
/// </para>
/// </summary>
public static class RunEventKinds
{
    /// <summary>Opening event: what is about to run, and whether deltas were asked for.</summary>
    public const string RunStarted = "run.started";

    /// <summary>One task of the crew completed - the granularity <c>ICrewExecutionHook</c> gives.</summary>
    public const string TaskCompleted = "task.completed";

    /// <summary>The token meter moved.</summary>
    public const string CostUpdated = "cost.updated";

    /// <summary>Token-by-token generation; only under <c>--stream</c>.</summary>
    public const string LlmDelta = "llm.delta";

    /// <summary>Closing event; mirrors the process exit code.</summary>
    public const string RunFinished = "run.finished";

    /// <summary>An anomaly, recoverable or not.</summary>
    public const string Error = "error";

    /// <summary>A running crew is waiting on a human (BUS-04).</summary>
    public const string InputNeeded = "input.needed";

    /// <summary>Inbound: the human's answer, correlated to the question.</summary>
    public const string InputGiven = "input.given";

    /// <summary>A tool was invoked; correlated with its <see cref="ToolReturned"/>.</summary>
    public const string ToolCalled = "tool.called";

    /// <summary>A tool finished, successfully or not - including when it threw.</summary>
    public const string ToolReturned = "tool.returned";

    /// <summary>
    /// One agent handed work to another. A delegation is a tool call underneath, but calling it
    /// one would bury the single thing that makes a hierarchical run readable.
    /// </summary>
    public const string DelegationStarted = "delegation.started";

    /// <summary>The team grew at runtime - the autonomous mode's most opaque moment.</summary>
    public const string AgentSpawned = "agent.spawned";

    /// <summary>Something the run's event hub relayed to this process.</summary>
    public const string HubMessage = "hub.message";

    /// <summary>
    /// Every kind, so a consumer can assert it handles them all rather than discovering a gap
    /// as an event that silently never arrives.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        RunStarted, TaskCompleted, CostUpdated, LlmDelta, RunFinished, Error,
        InputNeeded, InputGiven, ToolCalled, ToolReturned, DelegationStarted,
        AgentSpawned, HubMessage,
    ];
}
