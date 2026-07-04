namespace Orkeon.Domain.Graph;

/// <summary>
/// A processing node in a <see cref="StateGraph{TState}"/>.
/// Transforms the typed state and passes it to the next node.
/// </summary>
/// <typeparam name="TState">The state type.</typeparam>
public sealed class GraphNode<TState> where TState : class
{
    /// <summary>Gets the unique name of this node.</summary>
    public string Name { get; }

    /// <summary>Gets the async action that transforms the state.</summary>
    public Func<TState, CancellationToken, Task<TState>> Action { get; }

    /// <summary>Creates a new graph node.</summary>
    /// <param name="name">Unique node name.</param>
    /// <param name="action">State transformation function.</param>
    internal GraphNode(string name, Func<TState, CancellationToken, Task<TState>> action)
    {
        Name = name;
        Action = action;
    }
}
