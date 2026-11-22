using System.Text.Json;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Hosting.Runs;
using Orkeon.Scripting.Cli.Events;
using Orkeon.Scripting.Cli.Tests.Forge;
using RunEventKinds = Orkeon.Constants.Protocol.RunEventKinds;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>What became of a loop's older read-tool results, on the run stream: agent.pruned (#434).</summary>
public sealed class RunEventsPruneObserverTests : IDisposable
{
    private static readonly string[] s_counts = ["estimatedTokens", "thresholdTokens", "offered", "judged", "kept", "cut", "dropped", "freedTokens"];

    private readonly StringWriter _output = new();

    public void Dispose() => _output.Dispose();

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldWriteEveryFieldOfThePruning_WhenALoopsResultsWereJudged()
    {
        // Arrange
        var observer = new RunEventsPruneObserver(new OrkeonEventWriter(_output, new FakeOrkeonClock()));
        var notice = new AgentPruneNotice
        {
            TaskId = "task-1",
            AgentRole = "Reader",
            EstimatedTokens = 5_000,
            ThresholdTokens = 4_000,
            Offered = 3,
            Judged = 3,
            Kept = 1,
            Cut = 1,
            Dropped = 1,
            FreedTokens = 900,
        };

        // Act
        observer.Pruned(notice);

        // Assert
        var line = Assert.Single(_output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var json = JsonElement.Parse(line);
        Assert.Equal(RunEventKinds.AgentPruned, json.GetProperty("kind").GetString());
        Assert.Equal("task-1", json.GetProperty("taskId").GetString());
        Assert.Equal("Reader", json.GetProperty("agentRole").GetString());
        Assert.Equal([5_000, 4_000, 3, 3, 1, 1, 1, 900], s_counts.Select(name => json.GetProperty(name).GetInt32()));
    }

    [Fact]
    [Trait("BR", "CH-461")]
    public void ShouldWriteNoTaskId_WhenThePruningBelongsToAScriptsAct()
    {
        // Arrange: an act runs on no task of a crew, and its notice names none (an empty id).
        var observer = new RunEventsPruneObserver(new OrkeonEventWriter(_output, new FakeOrkeonClock()));
        var notice = new AgentPruneNotice
        {
            TaskId = string.Empty,
            AgentRole = "Reader",
            EstimatedTokens = 5_000,
            ThresholdTokens = 4_000,
            Offered = 1,
            Judged = 1,
            Kept = 0,
            Cut = 0,
            Dropped = 1,
            FreedTokens = 900,
        };

        // Act
        observer.Pruned(notice);

        // Assert: null on the payload, which the wire omits, as a pick's agent.route (#460).
        var json = JsonElement.Parse(Assert.Single(_output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)));
        Assert.False(json.TryGetProperty("taskId", out _));
        Assert.Equal("Reader", json.GetProperty("agentRole").GetString());
    }
}
