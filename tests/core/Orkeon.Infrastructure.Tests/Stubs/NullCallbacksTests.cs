using Microsoft.Extensions.Logging;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Stubs;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Stubs;

/// <summary>
/// SONAR-14: pins the three no-op callback stubs — every hook completes, logs at
/// Debug when the logger is enabled, and rejects null arguments.
/// </summary>
public class NullCallbacksTests
{
    /// <summary>Debug-enabled logger recording formatted entries.</summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(formatter(state, exception));
    }

    private static DomainAgent BuildAgent() =>
        new AgentBuilder().Role("Stub Agent").Goal("Do nothing loudly").Build();

    private static CrewTask BuildTask() =>
        CrewTask.Create(TaskDescription.From("Stub task"), ExpectedOutput.From("Nothing"));

    [Fact]
    public async Task TheStepCallback_LogsEveryHook_AndGuardsNulls()
    {
        var logger = new ListLogger<NullStepCallback>();
        var callback = new NullStepCallback(logger);
        var agent = BuildAgent();
        var task = BuildTask();
        var step = AgentStep.CreateSuccess("out", structuredOutput: null, action: "think", input: "in");

        await callback.OnStepStartAsync(agent, task, iteration: 1);
        await callback.OnStepCompletedAsync(agent, task, iteration: 1, step);
        await callback.OnStepFailedAsync(agent, task, iteration: 2, "boom");

        Assert.Equal(3, logger.Entries.Count);
        Assert.Contains("Step starting", logger.Entries[0], StringComparison.Ordinal);
        Assert.Contains("Step completed", logger.Entries[1], StringComparison.Ordinal);
        Assert.Contains("boom", logger.Entries[2], StringComparison.Ordinal);

        await Assert.ThrowsAsync<ArgumentNullException>(() => callback.OnStepStartAsync(null!, task, 0));
        await Assert.ThrowsAsync<ArgumentNullException>(() => callback.OnStepCompletedAsync(agent, null!, 0, step));
        await Assert.ThrowsAsync<ArgumentNullException>(() => callback.OnStepFailedAsync(null!, task, 0, "x"));
        Assert.Throws<ArgumentNullException>(() => new NullStepCallback(null!));
    }

    [Fact]
    public async Task TheProgressHandler_LogsTheProgress_AndGuardsNulls()
    {
        var logger = new ListLogger<NullStepProgressHandler>();
        var handler = new NullStepProgressHandler(logger);
        var context = new StepProgressContext(
            "task-1", "agent-1", "reading sources", CurrentStep: 2, TotalSteps: 5, DateTime.UtcNow);

        await handler.OnStepProgressAsync(context, TestContext.Current.CancellationToken);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("2/5", entry, StringComparison.Ordinal);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            handler.OnStepProgressAsync(null!, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentNullException>(() => new NullStepProgressHandler(null!));
    }

    [Fact]
    public async Task TheTaskCallback_LogsEveryHook_AndGuardsNulls()
    {
        var logger = new ListLogger<NullTaskCallback>();
        var callback = new NullTaskCallback(logger);
        var task = BuildTask();

        await callback.OnTaskStartAsync(task);
        await callback.OnTaskCompletedAsync(task, TaskOutput.Text("done"));
        await callback.OnTaskFailedAsync(task, "kaput");

        Assert.Equal(3, logger.Entries.Count);
        Assert.Contains("Task starting", logger.Entries[0], StringComparison.Ordinal);
        Assert.Contains("Task completed", logger.Entries[1], StringComparison.Ordinal);
        Assert.Contains("kaput", logger.Entries[2], StringComparison.Ordinal);

        await Assert.ThrowsAsync<ArgumentNullException>(() => callback.OnTaskStartAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => callback.OnTaskCompletedAsync(null!, TaskOutput.Text("x")));
        await Assert.ThrowsAsync<ArgumentNullException>(() => callback.OnTaskFailedAsync(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => new NullTaskCallback(null!));
    }

    [Fact]
    public async Task TheHooks_StayQuiet_WhenDebugLoggingIsDisabled()
    {
        var callback = new NullStepCallback(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NullStepCallback>.Instance);

        // Completes without logging — the IsEnabled guard short-circuits.
        await callback.OnStepStartAsync(BuildAgent(), BuildTask(), 0);
    }
}
