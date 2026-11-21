using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>An <see cref="IAgentRouteObserver"/> that keeps every notice it is told, in order.</summary>
public sealed class FakeAgentRouteObserver : IAgentRouteObserver
{
    /// <summary>The notices received so far, oldest first.</summary>
    public List<AgentRouteNotice> Notices { get; } = [];

    /// <inheritdoc />
    public void ProfileRouted(AgentRouteNotice notice) => Notices.Add(notice);
}
