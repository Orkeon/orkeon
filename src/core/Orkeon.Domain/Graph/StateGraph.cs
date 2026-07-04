using Orkeon.Domain.Common.StateMachine;

namespace Orkeon.Domain.Graph;

/// <summary>
/// LangGraph-style typed state graph with controlled cycles and circuit breaker protection.
/// Nodes transform state, edges route to the next node based on state content.
/// </summary>
/// <typeparam name="TState">The typed state that flows through the graph.</typeparam>
public sealed class StateGraph<TState> where TState : class
{
    private readonly Dictionary<string, GraphNode<TState>> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IGraphEdge<TState>> _edges = new(StringComparer.Ordinal);
    private readonly CircuitBreakerPolicy _circuitPolicy;

    /// <summary>Well-known start sentinel node name.</summary>
    public const string StartNode = "__start__";

    /// <summary>Well-known end sentinel node name.</summary>
    public const string EndNode = "__end__";

    /// <summary>Creates a new StateGraph with the specified circuit breaker policy.</summary>
    /// <param name="circuitPolicy">Policy controlling max transitions, cycle limits, timeouts.</param>
    public StateGraph(CircuitBreakerPolicy? circuitPolicy = null)
    {
        _circuitPolicy = circuitPolicy ?? CircuitBreakerPolicy.Strict;
    }

    /// <summary>Gets all registered node names (excluding sentinels).</summary>
    public IReadOnlyCollection<string> NodeNames => _nodes.Keys;

    /// <summary>Gets the circuit breaker policy.</summary>
    public CircuitBreakerPolicy CircuitPolicy => _circuitPolicy;

    /// <summary>
    /// Adds a processing node to the graph.
    /// </summary>
    /// <param name="name">Unique node name.</param>
    /// <param name="action">Async function that receives state and returns modified state.</param>
    /// <returns>This graph for fluent chaining.</returns>
    /// <exception cref="ArgumentException">If name is a reserved sentinel or already registered.</exception>
    public StateGraph<TState> AddNode(string name, Func<TState, CancellationToken, Task<TState>> action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is StartNode or EndNode)
            throw new ArgumentException($"'{name}' is a reserved sentinel node name.", nameof(name));
        if (_nodes.ContainsKey(name))
            throw new ArgumentException($"Node '{name}' is already registered.", nameof(name));

        _nodes[name] = new GraphNode<TState>(name, action);
        return this;
    }

    /// <summary>
    /// Adds a fixed (unconditional) edge from one node to another.
    /// </summary>
    /// <param name="from">Source node name (or <see cref="StartNode"/>).</param>
    /// <param name="to">Target node name (or <see cref="EndNode"/>).</param>
    /// <returns>This graph for fluent chaining.</returns>
    public StateGraph<TState> AddEdge(string from, string to)
    {
        ValidateEdgeEndpoints(from, to);
        if (_edges.ContainsKey(from))
            throw new InvalidOperationException($"Node '{from}' already has an outgoing edge. Use AddConditionalEdge for branching.");

        _edges[from] = new FixedEdge<TState>(to);
        return this;
    }

    /// <summary>
    /// Adds a conditional edge that routes based on the current state.
    /// The router function returns the name of the next node (or <see cref="EndNode"/>).
    /// </summary>
    /// <param name="from">Source node name.</param>
    /// <param name="router">Function that inspects state and returns the next node name.</param>
    /// <param name="possibleTargets">Declared set of possible target node names (for validation).</param>
    /// <returns>This graph for fluent chaining.</returns>
    public StateGraph<TState> AddConditionalEdge(
        string from,
        Func<TState, string> router,
        IReadOnlyList<string>? possibleTargets = null)
    {
        if (from == EndNode)
            throw new ArgumentException("Cannot add an outgoing edge from the END node.", nameof(from));
        if (from != StartNode && !_nodes.ContainsKey(from))
            throw new ArgumentException($"Source node '{from}' is not registered.", nameof(from));
        if (_edges.ContainsKey(from))
            throw new InvalidOperationException($"Node '{from}' already has an outgoing edge.");

        _edges[from] = new ConditionalEdge<TState>(router, possibleTargets);
        return this;
    }

    /// <summary>
    /// Compiles the graph into an executable runner. Validates the graph structure.
    /// </summary>
    /// <returns>A compiled <see cref="GraphRunner{TState}"/>.</returns>
    /// <exception cref="InvalidOperationException">If the graph is invalid (missing start edge, unreachable nodes, etc.).</exception>
    public GraphRunner<TState> Compile()
    {
        // Validate: START must have an outgoing edge
        if (!_edges.ContainsKey(StartNode))
            throw new InvalidOperationException("Graph must have an edge from START. Use AddEdge(StartNode, \"first_node\").");

        // Validate: every node must have an outgoing edge
        foreach (var nodeName in _nodes.Keys)
        {
            if (!_edges.ContainsKey(nodeName))
                throw new InvalidOperationException($"Node '{nodeName}' has no outgoing edge. Add an edge or route to END.");
        }

        // Validate: fixed edge targets must exist
        foreach (var (from, edge) in _edges)
        {
            if (edge is FixedEdge<TState> fixedEdge)
            {
                var target = fixedEdge.Target;
                if (target != EndNode && !_nodes.ContainsKey(target))
                    throw new InvalidOperationException($"Edge from '{from}' targets unknown node '{target}'.");
            }
        }

        return new GraphRunner<TState>(
            new Dictionary<string, GraphNode<TState>>(_nodes),
            new Dictionary<string, IGraphEdge<TState>>(_edges),
            _circuitPolicy);
    }

    private void ValidateEdgeEndpoints(string from, string to)
    {
        if (from == EndNode)
            throw new ArgumentException("Cannot add an outgoing edge from the END node.", nameof(from));
        if (to == StartNode)
            throw new ArgumentException("Cannot route back to the START node.", nameof(to));
        if (from != StartNode && !_nodes.ContainsKey(from))
            throw new ArgumentException($"Source node '{from}' is not registered.", nameof(from));
        if (to != EndNode && !_nodes.ContainsKey(to))
            throw new ArgumentException($"Target node '{to}' is not registered.", nameof(to));
    }
}
