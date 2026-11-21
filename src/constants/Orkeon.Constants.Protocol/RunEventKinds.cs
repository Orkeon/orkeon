namespace Orkeon.Constants.Protocol;

/// <summary>
/// The event kinds a run streams as it executes.
/// <para>
/// This is a wire protocol between processes: the CLI runner writes the stream as JSON lines,
/// and whatever watches the run - a client, a script, a terminal - reads it. Producer and
/// consumer live in projects that cannot reference each other, so the vocabulary was written
/// twice, and nothing checked the two spellings against each other.
/// </para>
/// <para>
/// It is also the vocabulary of a run observed in process: the events
/// <c>ICrewOrchestrationService.KickoffStreamingAsync</c> yields carry these kinds (GAP-32), so a
/// C# host and a process watching the wire read the same words — one vocabulary, not a third.
/// </para>
/// <para>
/// The two copies had already drifted: the runner emitted <c>tool.called</c>,
/// <c>tool.returned</c>, <c>delegation.started</c> and <c>agent.spawned</c>, and the client's copy
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

    /// <summary>
    /// One task of the crew started: its agent's turn begins. Between this and the matching
    /// <see cref="TaskCompleted"/> a watcher shows the task as in progress - the moment the
    /// screen could not tell apart from a run that had stalled.
    /// </summary>
    public const string TaskStarted = "task.started";

    /// <summary>One task of the crew completed - the granularity <c>ICrewExecutionHook</c> gives.</summary>
    public const string TaskCompleted = "task.completed";

    /// <summary>The token meter moved.</summary>
    public const string CostUpdated = "cost.updated";

    /// <summary>Token-by-token generation; only under <c>--stream</c>.</summary>
    public const string LlmDelta = "llm.delta";

    /// <summary>A model call was sent: its id, the mode on the wire and its estimated prompt tokens.</summary>
    public const string LlmCallStarted = "llm.call.started";

    /// <summary>A streamed model call received more: its cumulative estimated completion tokens, at most every 250 ms.</summary>
    public const string LlmCallProgress = "llm.call.progress";

    /// <summary>A model call ended: the mode actually used, whether it fell back, and the provider's count when it gave one.</summary>
    public const string LlmCallFinished = "llm.call.finished";

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

    /// <summary>
    /// A profile's fallback chain moved a call to the next profile (FWK-05): <c>profile</c>, <c>from</c>,
    /// <c>to</c>, <c>reason</c>. Additive: a consumer that does not know the kind ignores it.
    /// </summary>
    public const string LlmFallback = "llm.fallback";

    /// <summary>Something the run's event hub relayed to this process.</summary>
    public const string HubMessage = "hub.message";

    /// <summary>
    /// A graph engine is about to run a node: <c>node</c> and <c>visit</c>, plus <c>task</c> for a
    /// crew's graph and <c>graph</c> (its name) for a script's <c>stateGraph</c> (ADR-018).
    /// </summary>
    public const string GraphNodeEntered = "graph.node.entered";

    /// <summary>
    /// Once per run, after the crew loads and before its first <see cref="TaskStarted"/>: the crew's
    /// <c>mode</c> (<see cref="RunDecisionModes"/>), the decision <c>circuit</c>
    /// (<see cref="DecisionCircuitStates"/>) and, off, its <c>cause</c> (<see cref="DecisionCircuitCauses"/>), #429.
    /// </summary>
    public const string RunDecision = "run.decision";

    /// <summary>
    /// An agent loop compacted its conversation, failed to, or reverted to the 40-message trim (#432):
    /// <c>taskId</c>, <c>agentRole</c>, <c>outcome</c> (<c>compacted</c>, <c>failed</c>, <c>reverted</c>),
    /// <c>tokensBefore</c>, <c>tokensAfter</c>, <c>window</c>, <c>windowSource</c> (<c>profile</c>,
    /// <c>table</c>, <c>settings</c>), <c>keptMessages</c>, <c>summaryTokens</c>. Additive: a consumer that
    /// does not know the kind ignores it.
    /// </summary>
    public const string AgentCompacted = "agent.compacted";

    /// <summary>
    /// The skills a task's prompt carries (#434), once per task of an agent that declares <c>skills:</c>:
    /// <c>taskId</c>, <c>agentRole</c>, <c>mode</c> (<c>list</c>, <c>auto</c>, <c>fallback</c>), <c>skills</c>
    /// (the virtual paths carried), <c>offered</c> (the skills offered to the decision model), <c>cut</c> (the
    /// skills cut by a cap or left out) and <c>reason</c> (why the fallback stands, else null). Additive: a
    /// consumer that does not know the kind ignores it.
    /// </summary>
    public const string AgentSkills = "agent.skills";

    /// <summary>
    /// The profile a task of an agent on <c>llm.profile: auto</c> runs on (#434), once per task, at its start:
    /// <c>taskId</c>, <c>agentRole</c>, <c>profile</c> (a rung of the ladder, or <c>default</c>), <c>reason</c>
    /// (<c>model</c>, <c>low-confidence</c>, <c>no-answer</c>, <c>circuit-off</c>, <c>no-ladder</c>, <c>decision-none</c>),
    /// <c>picked</c> and <c>probability</c> (the model's pick and its probability, null when it picked no rung) and
    /// <c>ladder</c>. Additive: a consumer that does not know the kind ignores it.
    /// </summary>
    public const string AgentRoute = "agent.route";

    /// <summary>
    /// A loop's older read-tool results were judged for pruning before a model call (#434): <c>taskId</c>,
    /// <c>agentRole</c>, <c>estimatedTokens</c> and <c>thresholdTokens</c> (the measure and the estimate from which
    /// results are judged), <c>offered</c>, <c>judged</c>, <c>kept</c>, <c>cut</c>, <c>dropped</c> and
    /// <c>freedTokens</c>. Never written with the decision circuit off. Additive: a consumer that does not know the
    /// kind ignores it.
    /// </summary>
    public const string AgentPruned = "agent.pruned";

    /// <summary>
    /// Every kind, so a consumer can assert it handles them all rather than discovering a gap
    /// as an event that silently never arrives.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        RunStarted, TaskStarted, TaskCompleted, CostUpdated, LlmDelta, RunFinished, Error,
        InputNeeded, InputGiven, ToolCalled, ToolReturned, DelegationStarted,
        AgentSpawned, HubMessage, LlmCallStarted, LlmCallProgress, LlmCallFinished, LlmFallback, GraphNodeEntered,
        RunDecision, AgentCompacted, AgentSkills, AgentRoute, AgentPruned,
    ];
}
