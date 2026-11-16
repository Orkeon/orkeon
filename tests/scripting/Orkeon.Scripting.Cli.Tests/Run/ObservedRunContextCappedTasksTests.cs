using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Runs;
using Orkeon.Hosting.Runs;
using Orkeon.Infrastructure.Orchestration;
using Orkeon.Scripting.Cli.Tests.Doubles;
using Orkeon.Scripting.Cli.Commands.Run;
using Orkeon.Scripting.Cli.Events;
using Orkeon.Scripting.Cli.Tests.Forge;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// CH-43: the <c>run.finished</c> line of <c>orkeon run</c> carries <c>cappedTasks</c>, the number of tasks
/// whose last <c>task.completed</c> attempt ended on the iteration cap.
/// </summary>
public sealed class ObservedRunContextCappedTasksTests : IDisposable
{
    private readonly StringWriter _output = new();

    public void Dispose() => _output.Dispose();

    private static TaskExecutionSnapshot Completed(string taskId, int attempt, bool capped, bool missing = false, bool success = true) => new()
    {
        TaskId = taskId,
        AgentRole = "Writer",
        Success = success,
        Duration = TimeSpan.FromMilliseconds(5),
        CompletedAt = DateTimeOffset.UnixEpoch,
        Attempt = attempt,
        EndedOnIterationCap = capped,
        DeliverableMissing = missing,
    };

    private async Task<JsonElement> RunFinishedAsync(params TaskExecutionSnapshot[] completions)
    {
        var events = RunEventStreams.Stdout(_output, new AsyncLocalRunScope(), new FakeRunTimeProvider());
        await using var context = new ObservedRunContext(events, stream: false, clientName: "test", input: null, recordOnly: true);
        var services = new ServiceCollection();
        context.WireServices(services);
        await using var provider = services.BuildServiceProvider();
        var hook = provider.GetRequiredService<ICrewExecutionHook>();
        foreach (var snapshot in completions)
            await hook.OnTaskCompletedAsync(snapshot, TestContext.Current.CancellationToken);

        await context.FinishAsync(0);

        var lines = _output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonElement.Parse(l));
        return lines.Single(e => e.GetProperty("kind").GetString() == "run.finished");
    }

    [Fact]
    [Trait("BR", "CH-43")]
    public async Task ShouldCountTheCappedTasks_InTheRunFinishedOfOrkeonRun()
    {
        var finished = await RunFinishedAsync(Completed("a", 1, capped: true), Completed("b", 1, capped: false));

        Assert.True(finished.TryGetProperty("cappedTasks", out var capped));
        Assert.Equal(1, capped.GetInt32());
    }

    [Fact]
    [Trait("BR", "CH-43")]
    public async Task ShouldWriteZeroCappedTasks_WhenNoTaskOfOrkeonRunEndsOnTheCap()
    {
        var finished = await RunFinishedAsync(Completed("a", 1, capped: false));

        Assert.True(finished.TryGetProperty("cappedTasks", out var capped));
        Assert.Equal(0, capped.GetInt32());
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async Task ShouldCountTheMissingDeliverables_InTheRunFinishedOfOrkeonRun()
    {
        var finished = await RunFinishedAsync(
            Completed("a", 1, capped: false, missing: true), Completed("b", 1, capped: false));

        Assert.True(finished.TryGetProperty("missingDeliverables", out var missing));
        Assert.Equal(1, missing.GetInt32());
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async Task ShouldWriteZeroMissingDeliverables_WhenEveryTaskOfOrkeonRunWroteIt()
    {
        var finished = await RunFinishedAsync(Completed("a", 1, capped: false));

        Assert.True(finished.TryGetProperty("missingDeliverables", out var missing));
        Assert.Equal(0, missing.GetInt32());
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async Task ShouldNotCountTheMissingDeliverable_WhenTheTasksLastAttemptFailed()
    {
        var finished = await RunFinishedAsync(
            Completed("a", 1, capped: false, missing: true),
            Completed("a", 2, capped: false, missing: true, success: false));

        Assert.Equal(0, finished.GetProperty("missingDeliverables").GetInt32());
    }
}
