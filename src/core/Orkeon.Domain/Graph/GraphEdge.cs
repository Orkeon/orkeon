namespace Orkeon.Domain.Graph;

/// <summary>
/// Abstraction for a graph edge that determines the next node.
/// </summary>
/// <typeparam name="TState">The state type.</typeparam>
public interface IGraphEdge<in TState> where TState : class
{
    /// <summary>Resolves the name of the next node to execute.</summary>
    /// <param name="state">The current state after node execution.</param>
    /// <returns>The next node name (or <see cref="StateGraph{TState}.EndNode"/>).</returns>
    string Resolve(TState state);
}

/// <summary>
/// A fixed (unconditional) edge that always routes to the same target node.
/// </summary>
internal sealed class FixedEdge<TState> : IGraphEdge<TState> where TState : class
{
    public string Target { get; }

    public FixedEdge(string target) => Target = target;

    public string Resolve(TState state) => Target;
}

/// <summary>
/// A conditional edge that uses a router function to determine the next node.
/// </summary>
internal sealed class ConditionalEdge<TState> : IGraphEdge<TState> where TState : class
{
    private readonly Func<TState, string> _router;
    private readonly IReadOnlyList<string>? _possibleTargets;

    public ConditionalEdge(Func<TState, string> router, IReadOnlyList<string>? possibleTargets)
    {
        _router = router;
        _possibleTargets = possibleTargets;
    }

    public string Resolve(TState state)
    {
        var target = _router(state);

        if (_possibleTargets != null && !_possibleTargets.Contains(target))
        {
            throw new InvalidOperationException(
                $"Conditional edge router returned '{target}' which is not in the declared targets: [{string.Join(", ", _possibleTargets)}].");
        }

        return target;
    }
}
