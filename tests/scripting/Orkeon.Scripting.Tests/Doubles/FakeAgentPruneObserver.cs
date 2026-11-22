using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>An <see cref="IAgentPruneObserver"/> that keeps every notice it is told, in order.</summary>
public sealed class FakeAgentPruneObserver : IAgentPruneObserver
{
    private readonly Lock _gate = new();
    private readonly List<AgentPruneNotice> _notices = [];

    /// <summary>The notices told so far.</summary>
    public IReadOnlyList<AgentPruneNotice> Notices
    {
        get { lock (_gate) return [.. _notices]; }
    }

    /// <inheritdoc />
    public void Pruned(AgentPruneNotice notice)
    {
        lock (_gate)
            _notices.Add(notice);
    }
}
