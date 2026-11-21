using System.Text.Json;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Hosting.Runs;
using Orkeon.Scripting.Cli.Events;
using Orkeon.Scripting.Cli.Tests.Forge;
using RunEventKinds = Orkeon.Constants.Protocol.RunEventKinds;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>The profile a task of an agent on llm.profile: auto runs on, on the run stream: agent.route (#434).</summary>
public sealed class RunEventsRouteObserverTests : IDisposable
{
    private readonly StringWriter _output = new();

    public void Dispose() => _output.Dispose();

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldWriteEveryFieldOfThePick_WhenATasksProfileIsRouted()
    {
        // Arrange
        var observer = new RunEventsRouteObserver(new OrkeonEventWriter(_output, new FakeOrkeonClock()));
        var notice = new AgentRouteNotice
        {
            TaskId = "task-1",
            AgentRole = "Coder",
            Profile = "default",
            Reason = AgentRouteReasons.LowConfidence,
            Picked = "large",
            Probability = 0.55,
            Ladder = ["small", "large"],
        };

        // Act
        observer.ProfileRouted(notice);

        // Assert
        var line = Assert.Single(_output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var json = JsonElement.Parse(line);
        Assert.Equal(RunEventKinds.AgentRoute, json.GetProperty("kind").GetString());
        Assert.Equal("task-1", json.GetProperty("taskId").GetString());
        Assert.Equal("Coder", json.GetProperty("agentRole").GetString());
        Assert.Equal("default", json.GetProperty("profile").GetString());
        Assert.Equal("low-confidence", json.GetProperty("reason").GetString());
        Assert.Equal("large", json.GetProperty("picked").GetString());
        Assert.Equal(0.55, json.GetProperty("probability").GetDouble());
        Assert.Equal(["small", "large"], json.GetProperty("ladder").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldWriteEveryFieldOfThePick_WhenATasksThinkingLevelIsPicked()
    {
        // Arrange
        var observer = new RunEventsRouteObserver(new OrkeonEventWriter(_output, new FakeOrkeonClock()));
        var notice = new AgentEffortNotice
        {
            TaskId = "task-1",
            AgentRole = "Coder",
            Level = "high",
            Reason = AgentEffortReasons.Model,
            Picked = "high",
            Probability = 0.8,
            Levels = ["low", "medium", "high"],
        };

        // Act
        observer.EffortPicked(notice);

        // Assert
        var line = Assert.Single(_output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var json = JsonElement.Parse(line);
        Assert.Equal(RunEventKinds.AgentEffort, json.GetProperty("kind").GetString());
        Assert.Equal("task-1", json.GetProperty("taskId").GetString());
        Assert.Equal("Coder", json.GetProperty("agentRole").GetString());
        Assert.Equal("high", json.GetProperty("level").GetString());
        Assert.Equal("model", json.GetProperty("reason").GetString());
        Assert.Equal("high", json.GetProperty("picked").GetString());
        Assert.Equal(0.8, json.GetProperty("probability").GetDouble());
        Assert.Equal(["low", "medium", "high"], json.GetProperty("levels").EnumerateArray().Select(e => e.GetString()));
    }
}
