using Orkeon.Domain.Common.StateMachine;

namespace Orkeon.Domain.Graph;

/// <summary>
/// Executes a compiled <see cref="StateGraph{TState}"/> with circuit breaker protection
/// against infinite loops, cycle storms, and runaway executions.
/// </summary>
/// <typeparam name="TState">The typed state that flows through the graph.</typeparam>
public sealed class GraphRunner<TState> where TState : class
{
    private readonly IReadOnlyDictionary<string, GraphNode<TState>> _nodes;
    private readonly IReadOnlyDictionary<string, IGraphEdge<TState>> _edges;
    private readonly CircuitBreakerPolicy _circuitPolicy;

    internal GraphRunner(
        IReadOnlyDictionary<string, GraphNode<TState>> nodes,
        IReadOnlyDictionary<string, IGraphEdge<TState>> edges,
        CircuitBreakerPolicy circuitPolicy)
    {
        _nodes = nodes;
        _edges = edges;
        _circuitPolicy = circuitPolicy;
    }

    /// <summary>
    /// Raised after each node completes, before routing to the next node.
    /// Useful for observability, logging, and interruptibility.
    /// </summary>
    public event EventHandler<NodeCompletedEventArgs<TState>>? OnNodeCompleted;

    /// <summary>
    /// Raised when the circuit breaker trips (too many transitions, cycles, or timeout).
    /// </summary>
    public event EventHandler<GraphCircuitBrokenEventArgs>? OnCircuitBroken;

    /// <summary>
    /// Executes the graph starting from the START node, flowing state through each node
    /// until END is reached or the circuit breaker trips.
    /// </summary>
    /// <param name="initialState">The initial typed state.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The final state after reaching END.</returns>
    /// <exception cref="GraphCircuitBrokenException">When the circuit breaker trips.</exception>
    /// <exception cref="OperationCanceledException">When cancellation is requested.</exception>
    public Task<GraphExecutionResult<TState>> RunAsync(
        TState initialState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        return RunCoreAsync(initialState, cancellationToken);
    }

    private async Task<GraphExecutionResult<TState>> RunCoreAsync(
        TState initialState,
        CancellationToken cancellationToken)
    {
        var tracker = new ExecutionTracker(_circuitPolicy);
        var state = initialState;
        var trace = new List<string>();

        // Resolve the first node from START edge
        var currentNodeName = _edges[StateGraph<TState>.StartNode].Resolve(state);
        tracker.RecordVisit(currentNodeName);

        while (currentNodeName != StateGraph<TState>.EndNode)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Circuit breaker check
            var breakReason = tracker.CheckBreaker(currentNodeName);
            if (breakReason != null)
            {
                var brokenArgs = new GraphCircuitBrokenEventArgs(breakReason, currentNodeName, tracker.TransitionCount, trace);
                OnCircuitBroken?.Invoke(this, brokenArgs);

                throw new GraphCircuitBrokenException(breakReason, currentNodeName, tracker.TransitionCount, trace);
            }

            // Execute the node
            if (!_nodes.TryGetValue(currentNodeName, out var node))
                throw new InvalidOperationException($"Node '{currentNodeName}' not found in the compiled graph.");

            state = await node.Action(state, cancellationToken).ConfigureAwait(false);
            trace.Add(currentNodeName);
            tracker.RecordTransition();

            // Notify observers
            OnNodeCompleted?.Invoke(this, new NodeCompletedEventArgs<TState>(
                currentNodeName, state, tracker.TransitionCount, trace.AsReadOnly()));

            // Route to next node
            if (!_edges.TryGetValue(currentNodeName, out var edge))
                throw new InvalidOperationException($"Node '{currentNodeName}' has no outgoing edge.");

            var nextNodeName = edge.Resolve(state);
            if (nextNodeName != StateGraph<TState>.EndNode)
            {
                tracker.RecordVisit(nextNodeName);
            }

            currentNodeName = nextNodeName;
        }

        return new GraphExecutionResult<TState>(
            FinalState: state,
            Trace: trace.AsReadOnly(),
            TotalTransitions: tracker.TransitionCount,
            Duration: tracker.Elapsed);
    }

    /// <summary>
    /// Internal tracker for circuit breaker enforcement during graph execution.
    /// </summary>
    private sealed class ExecutionTracker
    {
        private readonly CircuitBreakerPolicy _policy;
        private readonly Dictionary<string, int> _visitCounts = new(StringComparer.Ordinal);
        private readonly DateTime _startTime = DateTime.UtcNow;
        private int _transitionCount;

        public int TransitionCount => _transitionCount;
        public TimeSpan Elapsed => DateTime.UtcNow - _startTime;

        public ExecutionTracker(CircuitBreakerPolicy policy) => _policy = policy;

        public void RecordTransition() => _transitionCount++;

        public void RecordVisit(string nodeName)
        {
            if (!_visitCounts.TryGetValue(nodeName, out var count))
                count = 0;
            _visitCounts[nodeName] = count + 1;
        }

        public string? CheckBreaker(string currentNode)
        {
            if (_transitionCount >= _policy.MaxTransitions)
                return $"Max transitions exceeded ({_transitionCount}/{_policy.MaxTransitions})";

            if (_policy.MaxStateVisits > 0 &&
                _visitCounts.TryGetValue(currentNode, out var visits) &&
                visits > _policy.MaxStateVisits)
            {
                return $"Cycle detected: node '{currentNode}' visited {visits} times (max: {_policy.MaxStateVisits})";
            }

            if (_policy.MaxTotalDuration > TimeSpan.Zero && Elapsed > _policy.MaxTotalDuration)
                return $"Max total duration exceeded ({Elapsed.TotalMinutes:F1}min > {_policy.MaxTotalDuration.TotalMinutes:F1}min)";

            return null;
        }
    }
}

/// <summary>Result of a graph execution.</summary>
/// <typeparam name="TState">The state type.</typeparam>
/// <param name="FinalState">The final state after reaching END.</param>
/// <param name="Trace">Ordered list of node names visited.</param>
/// <param name="TotalTransitions">Total number of node executions.</param>
/// <param name="Duration">Total execution duration.</param>
public sealed record GraphExecutionResult<TState>(
    TState FinalState,
    IReadOnlyList<string> Trace,
    int TotalTransitions,
    TimeSpan Duration) where TState : class;

/// <summary>Event args raised when a node completes execution.</summary>
public sealed class NodeCompletedEventArgs<TState> : EventArgs where TState : class
{
    /// <summary>Name of the node that just completed.</summary>
    public string NodeName { get; }
    /// <summary>State observed after the node ran.</summary>
    public TState State { get; }
    /// <summary>Ordinal index of this transition in the run.</summary>
    public int TransitionOrdinal { get; }
    /// <summary>Snapshot of the execution trace up to this point.</summary>
    public IReadOnlyList<string> TraceSnapshot { get; }

    /// <summary>Creates a new <see cref="NodeCompletedEventArgs{TState}"/>.</summary>
    public NodeCompletedEventArgs(string nodeName, TState state, int transitionOrdinal, IReadOnlyList<string> trace)
    {
        NodeName = nodeName;
        State = state;
        TransitionOrdinal = transitionOrdinal;
        TraceSnapshot = trace;
    }
}

/// <summary>Event args raised when the circuit breaker trips.</summary>
public sealed class GraphCircuitBrokenEventArgs : EventArgs
{
    /// <summary>Reason the circuit breaker tripped.</summary>
    public string Reason { get; }
    /// <summary>Name of the node at which the breaker tripped.</summary>
    public string NodeName { get; }
    /// <summary>Number of transitions executed before the breaker tripped.</summary>
    public int TransitionCount { get; }
    /// <summary>Execution trace captured at the moment of tripping.</summary>
    public IReadOnlyList<string> Trace { get; }

    /// <summary>Creates a new <see cref="GraphCircuitBrokenEventArgs"/>.</summary>
    public GraphCircuitBrokenEventArgs(string reason, string nodeName, int transitionCount, IReadOnlyList<string> trace)
    {
        Reason = reason;
        NodeName = nodeName;
        TransitionCount = transitionCount;
        Trace = trace;
    }
}

/// <summary>Exception thrown when the graph circuit breaker trips.</summary>
#pragma warning disable S3925 // BinaryFormatter serialization is obsolete in .NET 10; ISerializable pattern not required
public sealed class GraphCircuitBrokenException : Exception
#pragma warning restore S3925
{
    /// <summary>Name of the node at which the breaker tripped.</summary>
    public string NodeName { get; }
    /// <summary>Number of transitions executed before the breaker tripped.</summary>
    public int TransitionCount { get; }
    /// <summary>Execution trace captured at the moment of tripping.</summary>
    public IReadOnlyList<string> Trace { get; }

    /// <summary>Creates a new <see cref="GraphCircuitBrokenException"/>.</summary>
    public GraphCircuitBrokenException(string reason, string nodeName, int transitionCount, IReadOnlyList<string> trace)
        : base($"Graph circuit breaker tripped at node '{nodeName}': {reason}")
    {
        NodeName = nodeName;
        TransitionCount = transitionCount;
        Trace = trace;
    }

    /// <summary>Initializes a new instance of <see cref="GraphCircuitBrokenException"/>.</summary>
    public GraphCircuitBrokenException() { NodeName = string.Empty; Trace = []; }

    /// <summary>Initializes a new instance of <see cref="GraphCircuitBrokenException"/>.</summary>
    /// <param name="message">The exception message.</param>
    public GraphCircuitBrokenException(string message) : base(message) { NodeName = string.Empty; Trace = []; }

    /// <summary>Initializes a new instance of <see cref="GraphCircuitBrokenException"/> with an inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public GraphCircuitBrokenException(string message, Exception innerException) : base(message, innerException) { NodeName = string.Empty; Trace = []; }
}
