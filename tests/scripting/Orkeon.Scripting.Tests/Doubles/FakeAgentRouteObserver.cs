using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>An <see cref="IAgentRouteObserver"/> that keeps every notice it is told, in order.</summary>
internal sealed class FakeAgentRouteObserver : IAgentRouteObserver
{
    /// <summary>The profile notices received so far, oldest first.</summary>
    public List<AgentRouteNotice> Notices { get; } = [];

    /// <summary>The thinking-level notices received so far, oldest first.</summary>
    public List<AgentEffortNotice> EffortNotices { get; } = [];

    public void ProfileRouted(AgentRouteNotice notice) => Notices.Add(notice);

    public void EffortPicked(AgentEffortNotice notice) => EffortNotices.Add(notice);
}
