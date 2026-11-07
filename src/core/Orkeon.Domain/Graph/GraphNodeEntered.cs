namespace Orkeon.Domain.Graph;

/// <summary>
/// A node a graph engine is about to run: the fact behind the <c>graph.node.entered</c> run event.
/// </summary>
/// <param name="Node">The node id.</param>
/// <param name="Visit">How many times this node has been entered in the run, this entry included (1 on the first).</param>
/// <param name="Task">The crew task the node runs, when the engine is the crew's; null for a script's graph.</param>
/// <param name="Graph">The graph's name, when it has one (a script's <c>stateGraph</c>); null for a crew.</param>
public sealed record GraphNodeEntered(string Node, int Visit, string? Task = null, string? Graph = null);

/// <summary>
/// Told, synchronously, each time either graph engine enters a node. The observer must not throw
/// and must not block: the engine waits for it.
/// </summary>
public interface IGraphNodeObserver
{
    /// <summary>A node is about to run.</summary>
    /// <param name="entered">Which node, and which visit of it.</param>
    void NodeEntered(GraphNodeEntered entered);
}
