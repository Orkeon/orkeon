using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>An <see cref="IAgentSkillsObserver"/> that keeps every notice it is told, in order.</summary>
public sealed class FakeAgentSkillsObserver : IAgentSkillsObserver
{
    /// <summary>The notices received so far, oldest first.</summary>
    public List<AgentSkillsNotice> Notices { get; } = [];

    /// <inheritdoc />
    public void SkillsChosen(AgentSkillsNotice notice) => Notices.Add(notice);
}
