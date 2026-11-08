using Orkeon.Domain.Graph;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>Records every node a graph engine entered.</summary>
internal sealed class MockGraphNodeObserver : IGraphNodeObserver
{
    private readonly object _gate = new();
    private readonly List<GraphNodeEntered> _entered = [];

    /// <summary>The entries, in the order they were told.</summary>
    public IReadOnlyList<GraphNodeEntered> Entered
    {
        get { lock (_gate) { return [.. _entered]; } }
    }

    public void NodeEntered(GraphNodeEntered entered)
    {
        lock (_gate) { _entered.Add(entered); }
    }
}
