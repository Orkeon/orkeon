using System.Text.Json;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Hosting.Runs;
using Orkeon.Scripting.Cli.Events;
using Orkeon.Scripting.Cli.Tests.Forge;
using RunEventKinds = Orkeon.Constants.Protocol.RunEventKinds;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>The skills a task's prompt carries on the run stream: agent.skills (#434).</summary>
public sealed class RunEventsSkillsObserverTests : IDisposable
{
    private readonly StringWriter _output = new();

    public void Dispose() => _output.Dispose();

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldWriteEveryFieldOfTheChoice_WhenAnAgentsSkillsAreChosen()
    {
        // Arrange
        var observer = new RunEventsSkillsObserver(new OrkeonEventWriter(_output, new FakeOrkeonClock()));
        var notice = new AgentSkillsNotice
        {
            TaskId = "task-1",
            AgentRole = "Reviewer",
            Mode = AgentSkillsModes.Fallback,
            Skills = ["/workspace/skills/review.md"],
            Offered = 0,
            Cut = ["/workspace/skills/gone.md"],
            Reason = "the decision circuit is off: --no-decision",
        };

        // Act
        observer.SkillsChosen(notice);

        // Assert
        var line = Assert.Single(_output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var json = JsonElement.Parse(line);
        Assert.Equal(RunEventKinds.AgentSkills, json.GetProperty("kind").GetString());
        Assert.Equal("task-1", json.GetProperty("taskId").GetString());
        Assert.Equal("Reviewer", json.GetProperty("agentRole").GetString());
        Assert.Equal("fallback", json.GetProperty("mode").GetString());
        Assert.Equal("/workspace/skills/review.md", Assert.Single(json.GetProperty("skills").EnumerateArray()).GetString());
        Assert.Equal(0, json.GetProperty("offered").GetInt32());
        Assert.Equal("/workspace/skills/gone.md", Assert.Single(json.GetProperty("cut").EnumerateArray()).GetString());
        Assert.Equal("the decision circuit is off: --no-decision", json.GetProperty("reason").GetString());
    }
}
