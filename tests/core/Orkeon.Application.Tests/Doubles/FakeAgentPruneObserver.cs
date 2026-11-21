using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>An <see cref="IAgentPruneObserver"/> that keeps every notice it is told, in order.</summary>
public sealed class FakeAgentPruneObserver : IAgentPruneObserver
{
    /// <summary>The notices received so far, oldest first.</summary>
    public List<AgentPruneNotice> Notices { get; } = [];

    /// <inheritdoc />
    public void Pruned(AgentPruneNotice notice) => Notices.Add(notice);
}
