using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Orchestration;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Events;
using Orkeon.Scripting.Cli.Tests.Doubles;
using Orkeon.Scripting.Cli.Tests.Forge;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// FWK-01: the events of a run carry the run's one identifier as <c>correlationId</c>, the id the
/// checkpoint session and the A2A and API task carry.
/// </summary>
public sealed class RunEventCorrelationTests : IDisposable
{
    private readonly StringWriter _output = new();

    public void Dispose() => _output.Dispose();

    private JsonElement Line() => JsonElement.Parse(_output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]);

    [Fact]
    [Trait("BR", "FWK-01")]
    public void ShouldWriteTheRunIdentifierAsTheCorrelationId_InsideARun()
    {
        var scope = new AsyncLocalRunScope();
        var writer = new OrkeonEventWriter(_output, new FakeOrkeonClock()) { RunScope = scope };
        var id = ExecutionId.New();

        using (scope.Enter(new RunScope(id, "crew@/crew/crew.yaml")))
            writer.Emit("task.started", new { taskId = "t1" });

        Assert.Equal(id.ToString(), Line().GetProperty("correlationId").GetString());
    }

    [Fact]
    [Trait("BR", "FWK-01")]
    public void ShouldWriteNoCorrelationId_OutsideARun()
    {
        var writer = new OrkeonEventWriter(_output, new FakeOrkeonClock()) { RunScope = new AsyncLocalRunScope() };

        writer.Emit("run.started");

        Assert.False(Line().TryGetProperty("correlationId", out _));
    }

    [Fact]
    [Trait("BR", "FWK-01")]
    public void ShouldKeepTheCorrelationIdTheScopeNames_OverTheRunsOne()
    {
        var scope = new AsyncLocalRunScope();
        var writer = new OrkeonEventWriter(_output, new FakeOrkeonClock()) { RunScope = scope };

        using (scope.Enter(new RunScope(ExecutionId.New(), "crew")))
            writer.Emit("hub.message", new OrkeonEventScope { CorrelationId = "msg-7" });

        Assert.Equal("msg-7", Line().GetProperty("correlationId").GetString());
    }

    private const string Crew =
        """
        name: correlated-crew
        goal: Run once and leave events
        process: sequential

        agents:
          writer:
            role: "Writer"
            goal: "Write"
            backstory: "An agent whose vendor bills every answer."

        tasks:
          draft:
            description: "Draft it."
            expected_output: "A draft."
            agent: writer
        """;

    [Fact]
    [Trait("BR", "FWK-01")]
    public async Task EveryEventOfARealRun_RunStartedAndRunFinishedIncluded_CarriesTheIdOfTheCheckpointSession()
    {
        // The real `orkeon run --events jsonl` path: the command opens the run's identity before
        // run.started, the kickoff adopts it, the checkpoint session takes it, and the stream closes
        // on run.finished inside it. One id, on every line.
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", Crew);
        using var console = new TestConsole(stdin: string.Empty);
        var store = new InMemoryStateStore();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
            AllowExternalMounts = true,
            Events = "jsonl",
            ConfigureTestServices = (_, services) =>
            {
                services.AddOrkeonLlmProvider(_ => new FakeBillingLlmProvider { ChargePerCall = 0.001 });
                services.AddSingleton<IStateStore>(store);
            },
        });

        Assert.Equal(0, exit);
        var events = console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonElement.Parse(line)).ToList();
        var session = Assert.Single(await store.ListAsync(TestContext.Current.CancellationToken));
        var kinds = events.Select(e => e.GetProperty("kind").GetString()).ToList();
        Assert.Equal("run.started", kinds[0]);
        Assert.Equal("run.finished", kinds[^1]);
        Assert.True(kinds.Count > 3, string.Join(",", kinds));
        Assert.All(events, e => Assert.Equal(session.SessionId, e.GetProperty("correlationId").GetString()));
    }
}
