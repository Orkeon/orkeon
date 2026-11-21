using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>An <see cref="IAgentSkillsObserver"/> that keeps every notice it is told, in order.</summary>
public sealed class FakeAgentSkillsObserver : IAgentSkillsObserver
{
    private readonly ConcurrentQueue<AgentSkillsNotice> _notices = new();

    /// <summary>The notices told so far.</summary>
    public IReadOnlyList<AgentSkillsNotice> Notices => [.. _notices];

    /// <inheritdoc />
    public void SkillsChosen(AgentSkillsNotice notice) => _notices.Enqueue(notice);
}
