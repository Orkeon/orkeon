using Orkeon.Application.Callback;

namespace Orkeon.Application.Tests.Callback;

/// <summary>
/// SONAR-14 T2: pins the callback plumbing — the no-op base handler completes every
/// hook, the composite fans every hook out to all handlers in registration order, and
/// the convenience accessors on the grouped context records read through correctly.
/// </summary>
public class CallbackHandlerTests
{
    private static readonly DateTime Stamp = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static StepStartedContext StepStarted() =>
        new("agent-1", "Analyst", "task-1", "search", "I should search", Stamp);

    private static StepCompletedContext StepCompleted() =>
        new(new StepIdentity("agent-1", "Analyst", "task-1"),
            "search", "I searched", "found 3 items", Success: true, TimeSpan.FromSeconds(1), Stamp);

    private static TaskStartedContext TaskStarted() =>
        new("task-1", "Describe", "Output", "agent-1", "Analyst", Stamp);

    private static TaskProgressContext TaskProgress() =>
        new("task-1", "agent-1", 2, 5, 40.0, "reading", Stamp);

    private static TaskCompletedContext TaskCompleted() =>
        new("task-1", "agent-1",
            new TaskExecutionOutcome(Success: true, "final", StructuredOutput: null, Error: null),
            TimeSpan.FromSeconds(2), StepsExecuted: 3, Stamp);

    private static FlowStepStartedContext FlowStepStarted() =>
        new("flow-1", "pipeline", "step-1", "extract", Stamp);

    private static FlowStepCompletedContext FlowStepCompleted() =>
        new(new FlowStepIdentity("flow-1", "pipeline", "step-1", "extract"),
            Success: false, Output: null, Error: "boom", TimeSpan.FromSeconds(1), Stamp);

    /// <summary>Records the hook names it receives, in order.</summary>
    private sealed class RecordingHandler : BaseCallbackHandler
    {
        public List<string> Hooks { get; } = [];

        public override System.Threading.Tasks.Task OnStepStartedAsync(StepStartedContext context, CancellationToken cancellationToken = default)
        {
            Hooks.Add(nameof(OnStepStartedAsync));
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public override System.Threading.Tasks.Task OnTaskCompletedAsync(TaskCompletedContext context, CancellationToken cancellationToken = default)
        {
            Hooks.Add(nameof(OnTaskCompletedAsync));
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    /// <summary>A bare subclass: every hook falls through to the no-op base.</summary>
    private sealed class BareHandler : BaseCallbackHandler
    {
    }

    [Fact]
    public async System.Threading.Tasks.Task TheBaseHandler_CompletesEveryHook_WithoutOverrides()
    {
        var handler = new BareHandler();
        var ct = TestContext.Current.CancellationToken;

        await handler.OnStepStartedAsync(StepStarted(), ct);
        await handler.OnStepCompletedAsync(StepCompleted(), ct);
        await handler.OnTaskStartedAsync(TaskStarted(), ct);
        await handler.OnTaskProgressAsync(TaskProgress(), ct);
        await handler.OnTaskCompletedAsync(TaskCompleted(), ct);
        await handler.OnFlowStepStartedAsync(FlowStepStarted(), ct);
        await handler.OnFlowStepCompletedAsync(FlowStepCompleted(), ct);
    }

    [Fact]
    public async System.Threading.Tasks.Task TheComposite_FansEveryHookOut_ToAllHandlersInOrder()
    {
        var first = new RecordingHandler();
        var second = new RecordingHandler();
        var composite = new CompositeCallbackHandler(first, second);
        var ct = TestContext.Current.CancellationToken;

        await composite.OnStepStartedAsync(StepStarted(), ct);
        await composite.OnStepCompletedAsync(StepCompleted(), ct);
        await composite.OnTaskStartedAsync(TaskStarted(), ct);
        await composite.OnTaskProgressAsync(TaskProgress(), ct);
        await composite.OnTaskCompletedAsync(TaskCompleted(), ct);
        await composite.OnFlowStepStartedAsync(FlowStepStarted(), ct);
        await composite.OnFlowStepCompletedAsync(FlowStepCompleted(), ct);

        Assert.Equal([nameof(BaseCallbackHandler.OnStepStartedAsync), nameof(BaseCallbackHandler.OnTaskCompletedAsync)], first.Hooks);
        Assert.Equal(first.Hooks, second.Hooks);
    }

    [Fact]
    public async System.Threading.Tasks.Task TheComposite_AcceptsAnEnumerable_AndAnEmptyHandlerList()
    {
        var handler = new RecordingHandler();
        var composite = new CompositeCallbackHandler(new List<ICallbackHandler> { handler });
        var empty = new CompositeCallbackHandler();
        var ct = TestContext.Current.CancellationToken;

        await composite.OnStepStartedAsync(StepStarted(), ct);
        await empty.OnStepStartedAsync(StepStarted(), ct);

        Assert.Single(handler.Hooks);
    }

    [Fact]
    public void GroupedContextRecords_ReadThroughTheirConvenienceAccessors()
    {
        var step = StepCompleted();
        Assert.Equal("agent-1", step.AgentId);
        Assert.Equal("Analyst", step.AgentRole);
        Assert.Equal("task-1", step.TaskId);

        var task = TaskCompleted();
        Assert.True(task.Success);
        Assert.Equal("final", task.Output);
        Assert.Null(task.StructuredOutput);
        Assert.Null(task.Error);

        var flow = FlowStepCompleted();
        Assert.Equal("flow-1", flow.FlowExecutionId);
        Assert.Equal("pipeline", flow.FlowName);
        Assert.Equal("step-1", flow.StepId);
        Assert.Equal("extract", flow.StepName);
    }
}
