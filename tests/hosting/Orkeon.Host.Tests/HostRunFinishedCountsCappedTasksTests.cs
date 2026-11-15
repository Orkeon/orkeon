using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Hosting;
using Orkeon.Infrastructure.DependencyInjection;

namespace Orkeon.Host.Tests;

/// <summary>
/// CH-43: the <c>run.finished</c> event of a hosted run carries <c>cappedTasks</c>, the number of tasks whose last
/// <c>task.completed</c> attempt ended on the iteration cap. The hook is driven directly, then the recording is closed.
/// </summary>
public sealed class HostRunFinishedCountsCappedTasksTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-host-capped-{Guid.NewGuid():N}");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public HostRunFinishedCountsCappedTasksTests()
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

    private IHost Build()
    {
        var root = new Dictionary<string, object?>
        {
            ["RaggableTree"] = new { Enabled = false },
            ["Orkeon"] = new Dictionary<string, object?>
            {
                ["Host"] = new Dictionary<string, object?> { ["Crews"] = new object[] { new { Name = "support", Path = CrewPath } } },
            },
        };
        var settings = Path.Combine(_root, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(settings, JsonSerializer.Serialize(root));
        var crewPlan = HostCrewMounts.For([new HostedCrewOptions { Name = "support", Path = CrewPath }]);

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
                services.AddOrkeonLlmProvider(_ => new HeldLlmProvider("unused"));
            });
    }

    private static TaskExecutionSnapshot Completed(string taskId, int attempt, bool capped) => new()
    {
        TaskId = taskId,
        AgentRole = "Helper",
        Success = true,
        Duration = TimeSpan.FromMilliseconds(5),
        CompletedAt = DateTimeOffset.UtcNow,
        Attempt = attempt,
        EndedOnIterationCap = capped,
    };

    private async Task<JsonElement> RunFinishedPayloadAsync(params TaskExecutionSnapshot[] completions)
    {
        using var host = Build();
        using var scope = host.Services.CreateScope();
        var recording = scope.ServiceProvider.GetRequiredService<HostRunRecording>();
        var runId = Orkeon.Application.Interfaces.Services.ExecutionId.New().ToString();
        var recorded = recording.Begin("support", runId, () => false);
        var hook = recording.Hook(new RecordingHook());
        foreach (var snapshot in completions)
            await hook.OnTaskCompletedAsync(snapshot, Ct);
        recording.Succeeded = true;
        recorded.Dispose();

        var reader = host.Services.GetRequiredService<IRunHistoryReader>();
        var summary = Assert.Single(await reader.ListAsync(new RunQuery(), Ct), s => s.RunId.ToString() == runId);
        var events = await reader.ReadEventsAsync(summary.RunId, 1, Ct);
        var finished = Assert.Single(events, e => e.Kind == RunEventKinds.RunFinished);
        return JsonSerializer.SerializeToElement(finished.Payload);
    }

    [Fact]
    [Trait("BR", "CH-43")]
    public async Task ShouldCountTheCappedTasks_InRunFinished()
    {
        var payload = await RunFinishedPayloadAsync(Completed("a", 1, capped: true), Completed("b", 1, capped: false));

        Assert.True(payload.TryGetProperty("cappedTasks", out var capped));
        Assert.Equal(1, capped.GetInt32());
    }

    [Fact]
    [Trait("BR", "CH-43")]
    public async Task ShouldCountATaskOnItsLastAttempt_InRunFinished()
    {
        var recoveredPayload = await RunFinishedPayloadAsync(Completed("a", 1, capped: true), Completed("a", 2, capped: false));
        Assert.True(recoveredPayload.TryGetProperty("cappedTasks", out var recovered));
        Assert.Equal(0, recovered.GetInt32());

        var cappedPayload = await RunFinishedPayloadAsync(Completed("a", 1, capped: false), Completed("a", 2, capped: true));
        Assert.True(cappedPayload.TryGetProperty("cappedTasks", out var capped));
        Assert.Equal(1, capped.GetInt32());
    }

    [Fact]
    [Trait("BR", "CH-43")]
    public async Task ShouldWriteZeroCappedTasks_WhenNoTaskEndsOnTheCap()
    {
        var payload = await RunFinishedPayloadAsync(Completed("a", 1, capped: false), Completed("b", 1, capped: false));

        Assert.True(payload.TryGetProperty("cappedTasks", out var capped));
        Assert.Equal(0, capped.GetInt32());
    }

    [Fact]
    [Trait("BR", "CH-43")]
    public async Task ShouldWriteZeroCappedTasks_WhenTheRunEndsBeforeAnyTask()
    {
        var payload = await RunFinishedPayloadAsync();

        Assert.True(payload.TryGetProperty("cappedTasks", out var capped));
        Assert.Equal(0, capped.GetInt32());
    }

    private sealed class RecordingHook : ICrewExecutionHook
    {
        public System.Threading.Tasks.Task OnTaskStartedAsync(TaskStartSnapshot snapshot, CancellationToken ct) =>
            System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task OnTaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct) =>
            System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task OnCrewCompletedAsync(CrewExecutionSnapshot snapshot, CancellationToken ct) =>
            System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task OnCrewFailedAsync(CrewExecutionSnapshot isPartial, Exception? ex, CancellationToken ct) =>
            System.Threading.Tasks.Task.CompletedTask;
    }
}
