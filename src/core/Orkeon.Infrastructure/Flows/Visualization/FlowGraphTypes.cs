namespace Orkeon.Infrastructure.Flows.Visualization;

/// <summary>
/// Represents the execution state of a single step.
/// </summary>
public enum StepState
{
    /// <summary>The step has not started.</summary>
    Pending,
    /// <summary>The step is currently running.</summary>
    Running,
    /// <summary>The step completed successfully.</summary>
    Completed,
    /// <summary>The step failed.</summary>
    Failed,
    /// <summary>The step was skipped.</summary>
    Skipped
}

/// <summary>
/// Represents a node in the flow graph.
/// </summary>
/// <param name="Id">The unique node identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Type">The step type (e.g. "tool", "crew", "conditional").</param>
/// <param name="State">The current execution state.</param>
public sealed record GraphNode(
    string Id,
    string Name,
    string Type,
    StepState State = StepState.Pending);

/// <summary>
/// Represents a directed edge between two nodes in the flow graph.
/// </summary>
/// <param name="SourceId">The source node identifier.</param>
/// <param name="TargetId">The target node identifier.</param>
/// <param name="Label">An optional label for the edge.</param>
public sealed record GraphEdge(
    string SourceId,
    string TargetId,
    string? Label = null);

/// <summary>
/// Represents the complete graph of a flow definition.
/// </summary>
/// <param name="FlowName">The name of the flow.</param>
/// <param name="Nodes">The nodes in the graph.</param>
/// <param name="Edges">The directed edges in the graph.</param>
public sealed record FlowGraph(
    string FlowName,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges);

/// <summary>
/// Represents the runtime execution state of a flow, tracking per-step states.
/// </summary>
/// <param name="FlowId">The flow execution identifier.</param>
/// <param name="StepStates">A dictionary mapping step IDs to their current states.</param>
/// <param name="StartedAt">When the flow execution started.</param>
/// <param name="CompletedAt">When the flow execution completed, or null if still running.</param>
public sealed record FlowExecutionState(
    string FlowId,
    IReadOnlyDictionary<string, StepState> StepStates,
    DateTime StartedAt,
    DateTime? CompletedAt = null);
