using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Hosting;
using Orkeon.Infrastructure.DependencyInjection;

namespace Orkeon.Host.Tests;

/// <summary>
/// FWK-08: <c>orkeon-host</c> publishes the events of the runs it hosts to the run history, with no operator setting: the
/// daemon records by default. The wiring goes through the daemon's own registration (<c>AddHostServices</c>) and its own
/// runner, over the mount the runner host plans itself.
/// </summary>
public sealed class HostPublishesRunEventsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-host-history-{Guid.NewGuid():N}");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public HostPublishesRunEventsTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "crews"));
        File.WriteAllText(CrewPath, """
            name: support
            goal: Answer one question offline
            process: sequential
            agents:
              helper:
                role: Helper
                goal: Help
                backstory: A minimal test agent.
                maxIter: 1
            tasks:
              answer:
                description: Answer the question.
                expected_output: An answer.
                agent: helper
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string CrewPath => Path.Combine(_root, "crews", "support.yaml");

    private IHost Build(HeldLlmProvider llm, bool? history = null)
    {
        var root = new Dictionary<string, object?>
        {
            ["RaggableTree"] = new { Enabled = false },
            ["Orkeon"] = new Dictionary<string, object?>
            {
                ["Host"] = new Dictionary<string, object?> { ["Crews"] = new object[] { new { Name = "support", Path = CrewPath } } },
            },
        };
        if (history is { } enabled)
            ((Dictionary<string, object?>)root["Orkeon"]!)["RunHistory"] = new { Enabled = enabled };

        var settings = Path.Combine(_root, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(settings, JsonSerializer.Serialize(root));
        var crewPlan = HostCrewMounts.For([new HostedCrewOptions { Name = "support", Path = CrewPath }]);

        // What HostStartup builds: the daemon asks for the history by default.
        return RunnerHost.Build(
            settings,
            new RunnerMountPlan
            {
                CliMounts = [.. crewPlan.Mounts],
                AllowExternalMounts = true,
                WorkingDirectory = _root,
                RunHistoryByDefault = true,
            },
            configureLogging: (_, logging) => logging.ClearProviders(),
            configureServices: (context, services) =>
            {
                services.AddHostServices(context.Configuration, crewPlan);
                services.AddOrkeonLlmProvider(_ => llm);
            },
            configureBuilder: builder => builder.UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            }));
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task AHostedRunLeavesItsEventsInTheHistory_FromRunStartedToRunFinished()
    {
        using var host = Build(new HeldLlmProvider("the answer"));
        var runner = host.Services.GetRequiredService<ICrewRunner>();

        var result = await runner.RunAsync("support", "What changed?", "test");

        Assert.Equal(HostedRunOutcome.Completed, result.Outcome);
        var reader = host.Services.GetRequiredService<IRunHistoryReader>();
        var summary = Assert.Single(await reader.ListAsync(new RunQuery(), Ct));
        Assert.Equal(RunStatus.Succeeded, summary.Status);
        Assert.Equal("support", summary.CrewKey);
        var events = await reader.ReadEventsAsync(summary.RunId, 1, Ct);
        Assert.Equal(RunEventKinds.RunStarted, events[0].Kind);
        Assert.Equal(RunEventKinds.RunFinished, events[^1].Kind);
        Assert.Contains(events, e => e.Kind == RunEventKinds.TaskStarted);
        Assert.Contains(events, e => e.Kind == RunEventKinds.TaskCompleted);
        Assert.Equal(Enumerable.Range(1, events.Count), events.Select(e => e.Seq));
        // One id end to end (FWK-14): the daemon's run id is the history's run id, and run.started repeats it.
        Assert.Equal(result.RunId, summary.RunId);
        Assert.Equal(result.RunId, events[0].Payload?["hostRunId"]?.GetValue<string>());
        Assert.All(events, e => Assert.Equal(summary.RunId, e.CorrelationId));
        Assert.True(events[^1].Payload?["success"]?.GetValue<bool>());
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task AFailedRunIsRecordedAsFailed_WithItsErrorEvent()
    {
        using var host = Build(new HeldLlmProvider("unused", failure: "the model is down"));
        var runner = host.Services.GetRequiredService<ICrewRunner>();

        var result = await runner.RunAsync("support", "hello", "test");

        Assert.Equal(HostedRunOutcome.Failed, result.Outcome);
        var reader = host.Services.GetRequiredService<IRunHistoryReader>();
        var summary = Assert.Single(await reader.ListAsync(new RunQuery(), Ct));
        Assert.Equal(RunStatus.Failed, summary.Status);
        var events = await reader.ReadEventsAsync(summary.RunId, 1, Ct);
        Assert.Equal(RunEventKinds.RunFinished, events[^1].Kind);
        Assert.False(events[^1].Payload?["success"]?.GetValue<bool>());
        Assert.Contains(events, e => e.Kind == RunEventKinds.Error);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task TwoRunsKeepTheirOwnHistories()
    {
        using var host = Build(new HeldLlmProvider("the answer"));
        var runner = host.Services.GetRequiredService<ICrewRunner>();

        await Task.WhenAll(runner.RunAsync("support", "one", "test"), runner.RunAsync("support", "two", "test"));

        var reader = host.Services.GetRequiredService<IRunHistoryReader>();
        var runs = await reader.ListAsync(new RunQuery(), Ct);
        Assert.Equal(2, runs.Count);
        foreach (var run in runs)
        {
            var events = await reader.ReadEventsAsync(run.RunId, 1, Ct);
            Assert.Equal(RunEventKinds.RunStarted, events[0].Kind);
            Assert.Single(events, e => e.Kind == RunEventKinds.RunFinished);
            Assert.Equal(Enumerable.Range(1, events.Count), events.Select(e => e.Seq));
        }
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task WhenTheOperatorTurnsTheHistoryOff_NothingIsRecordedAndTheRunStillCompletes()
    {
        using var host = Build(new HeldLlmProvider("the answer"), history: false);
        var runner = host.Services.GetRequiredService<ICrewRunner>();

        var result = await runner.RunAsync("support", "hello", "test");

        Assert.Equal(HostedRunOutcome.Completed, result.Outcome);
        Assert.Null(host.Services.GetService<IRunHistoryReader>());
        Assert.False(Directory.Exists(Path.Combine(_root, ".orkeon", "runs")));
    }
}
