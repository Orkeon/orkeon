namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Who an LLM call is made for: the crew, the agent and the task it serves, and the kind of
/// work it is (<see cref="LlmUsageOperations"/>). The metering decorator every provider is
/// wrapped in stamps it on the usage event of each call (STUDIO-42): the code that makes a
/// call never reports its usage, it only says what it is doing.
/// </summary>
public sealed record LlmUsageAttribution
{
    /// <summary>A call no scope claimed. It is counted all the same.</summary>
    public static LlmUsageAttribution Unattributed { get; } = new();

    /// <summary>The crew the call runs for; empty when none is known.</summary>
    public string CrewId { get; init; } = string.Empty;

    /// <summary>
    /// The agent making the call — its role, the name the run's events already use for it;
    /// empty when no agent is involved.
    /// </summary>
    public string AgentId { get; init; } = string.Empty;

    /// <summary>The task the call serves; empty when none is known.</summary>
    public string TaskId { get; init; } = string.Empty;

    /// <summary>The kind of work (<see cref="LlmUsageOperations"/>).</summary>
    public string Operation { get; init; } = LlmUsageOperations.Unattributed;
}

/// <summary>
/// The <see cref="LlmUsageAttribution"/> in effect for the current async flow. The
/// orchestrator, the hierarchical manager, the planner, the RAG pipelines, the memory
/// services, the flows, the evaluators and the scripting facade open one around the calls
/// they make, and the metering decorator reads it when a call starts. A scope names what it
/// knows and inherits the rest: a RAG pipeline queried by an agent's tool says <c>rag</c>
/// and stays that agent's, on that task, in that crew.
/// </summary>
/// <remarks>
/// Backed by <see cref="AsyncLocal{T}"/>: the value follows the flow across awaits and
/// thread hops, and a scope opened inside an async method never leaks to its caller. One
/// constraint follows from it: an async iterator resumes from a <c>yield return</c> in its
/// consumer's context, where a scope it opened earlier does not exist. Open the scope around
/// the awaited call it must cover — or, for a stream, before its first yield: the metered
/// provider reads the attribution once, when the stream starts.
/// </remarks>
public static class LlmUsageScope
{
    private static readonly AsyncLocal<LlmUsageAttribution?> Ambient = new();

    /// <summary>
    /// The attribution in effect; <see cref="LlmUsageAttribution.Unattributed"/> outside any
    /// scope.
    /// </summary>
    public static LlmUsageAttribution Current => Ambient.Value ?? LlmUsageAttribution.Unattributed;

    /// <summary>
    /// Opens a scope that lasts until the returned handle is disposed. An argument left null
    /// or empty is inherited from the enclosing scope: a caller says what it knows, never
    /// erases what an outer scope knew.
    /// </summary>
    /// <param name="operation">The kind of work (<see cref="LlmUsageOperations"/>).</param>
    /// <param name="crewId">The crew the calls run for.</param>
    /// <param name="agentId">The agent making them — its role.</param>
    /// <param name="taskId">The task they serve.</param>
    /// <returns>The handle that restores the enclosing scope.</returns>
    public static IDisposable Begin(
        string? operation = null,
        string? crewId = null,
        string? agentId = null,
        string? taskId = null)
    {
        var enclosing = Ambient.Value;
        var inherited = enclosing ?? LlmUsageAttribution.Unattributed;
        Ambient.Value = new LlmUsageAttribution
        {
            Operation = Pick(operation, inherited.Operation),
            CrewId = Pick(crewId, inherited.CrewId),
            AgentId = Pick(agentId, inherited.AgentId),
            TaskId = Pick(taskId, inherited.TaskId),
        };
        return new Scope(enclosing);
    }

    private static string Pick(string? named, string inherited) =>
        string.IsNullOrEmpty(named) ? inherited : named;

    private sealed class Scope(LlmUsageAttribution? enclosing) : IDisposable
    {
        public void Dispose() => Ambient.Value = enclosing;
    }
}
