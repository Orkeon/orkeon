using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Crew;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Crew;

/// <summary>
/// FWK-15: the decorator that gives a task its attempts. A failed attempt runs the whole task again, up
/// to the task's <c>retry:</c>; cancellation, a refusal of the guardian and an exhausted budget are never
/// retried; the wait between attempts comes from the injected clock, so no test sleeps.
/// </summary>
public sealed class RetryingAgentExecutionServiceTests : IDisposable
{
    private readonly DomainAgent _agent = new AgentBuilder().Role("Worker").Goal("Work").Build();
    private readonly MockMemoryScope _memory = new();
    private readonly FakeImmediateTimeProvider _time = new();
    private readonly MockCrewExecutionHook _hook = new();

    public void Dispose() => _memory.Dispose();

    private RetryingAgentExecutionService Decorate(IAgentExecutionService inner) =>
        new(inner, NullLogger<RetryingAgentExecutionService>.Instance, _time, _hook);

    private SimpleExecutionContext Context(CancellationToken ct) =>
        new(CrewId.Create(), new Dictionary<string, string>(), _memory, [], ct);

    private static CrewTask Task(TaskRetryPolicy? retry) =>
        new CrewTaskBuilder().WithKey("t").Description("do t").ExpectedOutput("done").Retry(retry).Build();

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldSucceedOnTheThirdAttempt_WhenTheFirstTwoFail()
    {
        var inner = new FakeScriptedAgentExecutionService(
            FakeScriptedAgentExecutionService.Fail(), FakeScriptedAgentExecutionService.Fail(), FakeScriptedAgentExecutionService.Ok("third"));

        var result = await Decorate(inner).ExecuteTaskAsync(
            _agent, Task(new TaskRetryPolicy(3)), Context(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("third", result.Output);
        Assert.Equal(3, inner.Calls);
        Assert.Equal(2, result.RetryCount);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldReturnTheLastFailure_WhenEveryAttemptFails()
    {
        var inner = new FakeScriptedAgentExecutionService(FakeScriptedAgentExecutionService.Fail("still down"));

        var result = await Decorate(inner).ExecuteTaskAsync(
            _agent, Task(new TaskRetryPolicy(2)), Context(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("still down", result.Error);
        Assert.Equal(2, inner.Calls);
        Assert.Equal(1, result.RetryCount);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldRunOnce_WhenTheTaskHasNoRetryPolicy()
    {
        var inner = new FakeScriptedAgentExecutionService(FakeScriptedAgentExecutionService.Fail());

        var result = await Decorate(inner).ExecuteTaskAsync(
            _agent, Task(null), Context(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(1, inner.Calls);
        Assert.Equal(0, result.RetryCount);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldRetryAnException_AndSucceedAfterIt()
    {
        var inner = new FakeScriptedAgentExecutionService(
            FakeScriptedAgentExecutionService.Throw(new InvalidOperationException("transient")), FakeScriptedAgentExecutionService.Ok());

        var result = await Decorate(inner).ExecuteTaskAsync(
            _agent, Task(new TaskRetryPolicy(2)), Context(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(2, inner.Calls);
        Assert.Equal(1, result.RetryCount);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldRethrowTheLastException_WhenEveryAttemptThrows()
    {
        var inner = new FakeScriptedAgentExecutionService(FakeScriptedAgentExecutionService.Throw(new InvalidOperationException("always")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Decorate(inner).ExecuteTaskAsync(
            _agent, Task(new TaskRetryPolicy(3)), Context(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));

        Assert.Equal("always", ex.Message);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldNotRetry_WhenTheAttemptIsCancelled()
    {
        var inner = new FakeScriptedAgentExecutionService(FakeScriptedAgentExecutionService.Throw(new OperationCanceledException()));

        await Assert.ThrowsAsync<OperationCanceledException>(() => Decorate(inner).ExecuteTaskAsync(
            _agent, Task(new TaskRetryPolicy(3)), Context(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldStop_WhenTheRunIsCancelledDuringAnAttempt()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var inner = new FakeScriptedAgentExecutionService(ct =>
        {
            cts.Cancel();
            return FakeScriptedAgentExecutionService.Fail("cut short")(ct);
        });

        var result = await Decorate(inner).ExecuteTaskAsync(_agent, Task(new TaskRetryPolicy(3)), Context(cts.Token), cts.Token);

        Assert.False(result.Success);
        Assert.Equal(1, inner.Calls);
    }

    [Theory]
    [InlineData(AgentExitReason.Cancelled)]
    [InlineData(AgentExitReason.BudgetExhausted)]
    [InlineData(AgentExitReason.GuardianBlocked)]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldNotRetry_WhenTheFailureIsARefusalOrABudget(AgentExitReason reason)
    {
        var inner = new FakeScriptedAgentExecutionService(FakeScriptedAgentExecutionService.Fail("refused", reason));

        var result = await Decorate(inner).ExecuteTaskAsync(
            _agent, Task(new TaskRetryPolicy(3)), Context(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(1, inner.Calls);
        Assert.Equal(0, result.RetryCount);
    }

    [Theory]
    [InlineData(AgentExitReason.Cancelled)]
    [InlineData(AgentExitReason.BudgetExhausted)]
    [InlineData(AgentExitReason.GuardianBlocked)]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldNotRetryTheTypedOverload_WhenTheFailureIsARefusalOrABudget(AgentExitReason reason)
    {
        var inner = new FakeScriptedAgentExecutionService { TypedFailuresLeft = 5, TypedReason = reason };

        var result = await Decorate(inner).ExecuteTaskAsync<string>(
            _agent, Task(new TaskRetryPolicy(3)), Context(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(1, inner.Calls);
        Assert.Equal(0, result.RetryCount);
        Assert.Equal(reason, result.ExitReason);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldRetryAnExecutionTimeout_BecauseItMayBeTransient()
    {
        var inner = new FakeScriptedAgentExecutionService(
            FakeScriptedAgentExecutionService.Fail("too slow", AgentExitReason.ExecutionTimeExceeded), FakeScriptedAgentExecutionService.Ok());

        var result = await Decorate(inner).ExecuteTaskAsync(
            _agent, Task(new TaskRetryPolicy(2)), Context(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldRetryAnExhaustedFailoverChain_WhichCanBeTransientToo()
    {
        var inner = new FakeScriptedAgentExecutionService(
            FakeScriptedAgentExecutionService.Fail("every profile failed (FallbackExhausted)", AgentExitReason.LlmCallFailed),
            FakeScriptedAgentExecutionService.Ok());

        var result = await Decorate(inner).ExecuteTaskAsync(
            _agent, Task(new TaskRetryPolicy(2)), Context(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldNoteTheAttemptThatEndedTheTaskOnTheRun()
    {
        var scope = new Orkeon.Infrastructure.Orchestration.AsyncLocalRunScope();
        var run = new Orkeon.Application.Interfaces.Services.RunScope(Orkeon.Application.Interfaces.Services.ExecutionId.New(), "crew@/crew/crew.yaml");
        var inner = new FakeScriptedAgentExecutionService(
            FakeScriptedAgentExecutionService.Fail(), FakeScriptedAgentExecutionService.Ok());
        var task = Task(new TaskRetryPolicy(3));

        using (scope.Enter(run))
            await Decorate(inner).ExecuteTaskAsync(_agent, task, Context(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.Equal(2, run.TaskAttempts[task.Id.Value.ToString()]);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldNotRetry_WhenTheBudgetIsExhaustedByAnException()
    {
#pragma warning disable ORKEXP002 // the budget exception is experimental; the decorator must treat it as final
        var inner = new FakeScriptedAgentExecutionService(
            FakeScriptedAgentExecutionService.Throw(new Orkeon.Domain.Autonomous.BudgetExhaustedException("spent")));
#pragma warning restore ORKEXP002

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Decorate(inner).ExecuteTaskAsync(
            _agent, Task(new TaskRetryPolicy(3)), Context(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldWaitTheSameBetweenAttempts_WhenTheBackoffIsFixed()
    {
        var inner = new FakeScriptedAgentExecutionService(FakeScriptedAgentExecutionService.Fail());

        await Decorate(inner).ExecuteTaskAsync(
            _agent, Task(new TaskRetryPolicy(3, 100, TaskRetryBackoff.Fixed)), Context(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.Equal([TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100)], _time.Delays);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldDoubleTheWait_WhenTheBackoffIsExponential()
    {
        var inner = new FakeScriptedAgentExecutionService(FakeScriptedAgentExecutionService.Fail());

        await Decorate(inner).ExecuteTaskAsync(
            _agent, Task(new TaskRetryPolicy(3, 100, TaskRetryBackoff.Exponential)), Context(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.Equal([TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200)], _time.Delays);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldNotAskTheClockAnything_WhenTheBackoffIsZero()
    {
        var inner = new FakeScriptedAgentExecutionService(FakeScriptedAgentExecutionService.Fail());

        await Decorate(inner).ExecuteTaskAsync(
            _agent, Task(new TaskRetryPolicy(3)), Context(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.Empty(_time.Delays);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldStopWaiting_WhenTheRunIsCancelledBetweenAttempts()
    {
        _time.Hold = true;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var inner = new FakeScriptedAgentExecutionService(FakeScriptedAgentExecutionService.Fail());

        var running = Decorate(inner).ExecuteTaskAsync(
            _agent, Task(new TaskRetryPolicy(3, 60_000)), Context(cts.Token), cts.Token);
        await _time.TimerRequested.Task.WaitAsync(Orkeon.Tests.Shared.Timing.Polling.DefaultTimeout, TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldReportEachRetryAsATaskStart_WithItsAttempt()
    {
        var inner = new FakeScriptedAgentExecutionService(FakeScriptedAgentExecutionService.Fail());
        var task = Task(new TaskRetryPolicy(3));

        await Decorate(inner).ExecuteTaskAsync(_agent, task, Context(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.Equal([2, 3], _hook.StartedTasks.Select(s => s.Attempt));
        Assert.All(_hook.StartedTasks, s => Assert.Equal(task.Id.Value.ToString(), s.TaskId));
        Assert.All(_hook.StartedTasks, s => Assert.Equal(_agent.Role.Value, s.AgentRole));
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldRetryTheTypedOverloadToo()
    {
        var inner = new FakeScriptedAgentExecutionService { TypedFailuresLeft = 2 };

        var result = await Decorate(inner).ExecuteTaskAsync<string>(
            _agent, Task(new TaskRetryPolicy(3)), Context(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(3, inner.Calls);
        Assert.Equal(2, result.RetryCount);
    }
}
