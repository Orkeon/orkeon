using System.Text.Json;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Hosting.Runs;
using Orkeon.Scripting.Cli.Events;
using Orkeon.Scripting.Cli.Tests.Forge;
using RunEventKinds = Orkeon.Constants.Protocol.RunEventKinds;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>An agent loop's compaction on the run stream: agent.compacted (#432).</summary>
public sealed class RunEventsCompactionObserverTests : IDisposable
{
    private readonly StringWriter _output = new();

    public void Dispose() => _output.Dispose();

    [Fact]
    [Trait("BR", "CH-432")]
    public void ShouldWriteEveryFieldOfTheCompaction_WhenALoopCompacts()
    {
        // Arrange
        var observer = new RunEventsCompactionObserver(new OrkeonEventWriter(_output, new FakeOrkeonClock()));
        var notice = new AgentCompactionNotice
        {
            TaskId = "task-1",
            AgentRole = "Writer",
            Outcome = AgentCompactionOutcomes.Reverted,
            TokensBefore = 9_000,
            TokensAfter = 8_500,
            Window = 10_000,
            WindowSource = "table",
            KeptMessages = 7,
            SummaryTokens = 120,
        };

        // Act
        observer.Compacted(notice);

        // Assert
        var line = Assert.Single(_output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var json = JsonElement.Parse(line);
        Assert.Equal(RunEventKinds.AgentCompacted, json.GetProperty("kind").GetString());
        Assert.Equal("task-1", json.GetProperty("taskId").GetString());
        Assert.Equal("Writer", json.GetProperty("agentRole").GetString());
        Assert.Equal("reverted", json.GetProperty("outcome").GetString());
        Assert.Equal(9_000, json.GetProperty("tokensBefore").GetInt32());
        Assert.Equal(8_500, json.GetProperty("tokensAfter").GetInt32());
        Assert.Equal(10_000, json.GetProperty("window").GetInt32());
        Assert.Equal("table", json.GetProperty("windowSource").GetString());
        Assert.Equal(7, json.GetProperty("keptMessages").GetInt32());
        Assert.Equal(120, json.GetProperty("summaryTokens").GetInt32());
    }
}
