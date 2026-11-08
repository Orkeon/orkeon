using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Configuration;

/// <summary>
/// Configuration for graph-based (LangGraph-style) process execution.
/// Only relevant when <see cref="CrewConfiguration.Process"/> is <c>Graph</c>.
/// </summary>
public sealed record GraphConfig
{
    /// <summary>The reserved word that ends the graph: the <c>to</c> of an edge leaving the run.</summary>
    public const string End = "END";

    /// <summary>
    /// The node the run starts at, or null for the first node (ADR-018, point 5). Read only when
    /// <see cref="Edges"/> describe a topology.
    /// </summary>
    public string? Start { get; init; }

    /// <summary>
    /// The nodes of the graph, or null for the implicit graph: one node per task, its id the task's key.
    /// </summary>
    public IReadOnlyList<GraphNodeConfig>? Nodes { get; init; }

    /// <summary>
    /// The edges of the graph, in the order declared. Empty means no topology: the crew runs one node
    /// per task, chained in execution order (ADR-018, point 7).
    /// </summary>
    public IReadOnlyList<GraphEdgeConfig> Edges { get; init; } = [];

    /// <summary>
    /// Circuit breaker preset name ("strict", "permissive", or "default").
    /// Overridden by explicit field values below when specified.
    /// Default: "strict".
    /// </summary>
    public string CircuitBreakerPreset { get; init; } = "strict";

    /// <summary>
    /// Maximum number of graph node transitions before the circuit breaker trips.
    /// Null = use preset default.
    /// </summary>
    public int? MaxTransitions { get; init; }

    /// <summary>
    /// Maximum number of times the same node can be visited before cycle detection trips.
    /// Null = use preset default.
    /// </summary>
    public int? MaxStateVisits { get; init; }

    /// <summary>
    /// Maximum total execution duration in seconds before the circuit breaker trips.
    /// Null = use preset default.
    /// </summary>
    public int? MaxTotalDurationSeconds { get; init; }
}

/// <summary>A node of a crew's graph: an id, and the task it runs.</summary>
/// <param name="Id">The node id, unique in the graph; never <c>START</c> or <c>END</c>.</param>
/// <param name="Task">The key of the crew task the node runs.</param>
public sealed record GraphNodeConfig(string Id, string Task);

/// <summary>An edge of a crew's graph (ADR-018, point 6).</summary>
/// <param name="From">The node it leaves.</param>
/// <param name="To">The node it leads to, or <see cref="GraphConfig.End"/>.</param>
/// <param name="When">
/// The condition on the answer of the <c>From</c> node's decision task, or null for the edge taken when
/// no other of the node holds (the "otherwise").
/// </param>
public sealed record GraphEdgeConfig(string From, string To, TaskCondition? When = null);
