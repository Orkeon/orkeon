using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Services.Checkpointing;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Infrastructure.Crew;
using Orkeon.Infrastructure.Orchestration;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Checkpointing;

/// <summary>
/// FWK-23: a run that stops on a question is not a failed run. The checkpoint decorator leaves its session waiting for
/// input, starts no more work, and the retry layer never replays the task that suspended.
/// </summary>
public sealed class SuspensionCheckpointTests : IDisposable
{
    private static readonly PendingInputRequest Question =
        new() { RequestId = "t#1.1", Kind = PendingInputKind.Text, Prompt = "Which one?", TaskKey = "t#1" };

    private readonly InMemoryStateStore _store = new();
    private readonly AsyncLocalRunScope _scope = new();
    private readonly DomainAgent _agent = new AgentBuilder().Role("Worker").Goal("Work").Build();
    private readonly MockMemoryScope _memory = new();
    private readonly CheckpointManager _manager;
    private readonly ExecutionId _id = ExecutionId.New();

    public SuspensionCheckpointTests() => _manager = new CheckpointManager(_store, NullLogger<CheckpointManager>.Instance);

    public void Dispose() => _memory.Dispose();

    private static ICrewTask Task(string key, TaskRetryPolicy? retry = null)
    {
        var builder = new CrewTaskBuilder().WithKey(key).Description($"do {key}").ExpectedOutput("done");
        return (retry is null ? builder : builder.Retry(retry)).Build();
    }

    private SimpleExecutionContext Context() =>
        new(CrewId.Create(), new Dictionary<string, string>(), _memory, [], CancellationToken.None);

    private CheckpointingAgentExecutionService Checkpointing(IAgentExecutionService inner) =>
        new(inner, _scope, _manager, NullLogger<CheckpointingAgentExecutionService>.Instance);

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldNotMarkTheSessionFailed_WhenTheTaskThrowsASuspension()
    {
        await _manager.StartSessionAsync(_id, "crew", TestContext.Current.CancellationToken);
        await _manager.AskAsync(_id.ToString(), Question, TestContext.Current.CancellationToken);
        var inner = new FakeScriptedAgentExecutionService(FakeScriptedAgentExecutionService.Throw(new RunSuspendedException("t#1.1")));
        var service = Checkpointing(inner);

        using (_scope.Enter(new RunScope(_id, "crew")))
            await Assert.ThrowsAsync<RunSuspendedException>(() => service.ExecuteTaskAsync(_agent, Task("t"), Context(), TestContext.Current.CancellationToken));

        var state = await _store.GetAsync(_id.ToString(), TestContext.Current.CancellationToken);
        Assert.Equal(SessionPhase.WaitingForInput, state!.Phase);
        Assert.Empty(state.TaskCheckpoints);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldThrowTheSuspension_WhenTheTaskEndedFailedBecauseTheRunSuspended()
    {
        await _manager.StartSessionAsync(_id, "crew", TestContext.Current.CancellationToken);
        var run = new RunScope(_id, "crew");
        var inner = new FakeScriptedAgentExecutionService(ct =>
        {
            run.Suspend(Question);
            return FakeScriptedAgentExecutionService.Fail("tool failed")(ct);
        });
        var service = Checkpointing(inner);

        using (_scope.Enter(run))
        {
            var thrown = await Assert.ThrowsAsync<RunSuspendedException>(
                () => service.ExecuteTaskAsync(_agent, Task("t"), Context(), TestContext.Current.CancellationToken));
            Assert.Equal("t#1.1", thrown.RequestId);
        }

        var state = await _store.GetAsync(_id.ToString(), TestContext.Current.CancellationToken);
        Assert.NotEqual(SessionPhase.Failed, state!.Phase);
        Assert.Empty(state.TaskCheckpoints);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldStartNoOtherTask_WhenTheRunIsSuspended()
    {
        await _manager.StartSessionAsync(_id, "crew", TestContext.Current.CancellationToken);
        var run = new RunScope(_id, "crew");
        run.Suspend(Question);
        var inner = new FakeScriptedAgentExecutionService(FakeScriptedAgentExecutionService.Ok());
        var service = Checkpointing(inner);

        using (_scope.Enter(run))
            await Assert.ThrowsAsync<RunSuspendedException>(() => service.ExecuteTaskAsync(_agent, Task("u"), Context(), TestContext.Current.CancellationToken));

        Assert.Equal(0, inner.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldRunTheTaskInItsInputScope_WhenItIsCheckpointed()
    {
        await _manager.StartSessionAsync(_id, "crew", TestContext.Current.CancellationToken);
        string? seen = null;
        var inner = new FakeScriptedAgentExecutionService(ct =>
        {
            seen = TaskInputScope.Current?.NextRequestId();
            return FakeScriptedAgentExecutionService.Ok()(ct);
        });
        var service = Checkpointing(inner);

        using (_scope.Enter(new RunScope(_id, "crew")))
        {
            await service.ExecuteTaskAsync(_agent, Task("t"), Context(), TestContext.Current.CancellationToken);
            Assert.Null(TaskInputScope.Current);
        }

        Assert.Equal("t#1.1", seen);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldNotRetry_WhenTheTaskThrowsASuspension()
    {
        var inner = new FakeScriptedAgentExecutionService(FakeScriptedAgentExecutionService.Throw(new RunSuspendedException("t#1.1")));
        var retrying = new RetryingAgentExecutionService(inner, NullLogger<RetryingAgentExecutionService>.Instance, new FakeImmediateTimeProvider());

        await Assert.ThrowsAsync<RunSuspendedException>(
            () => retrying.ExecuteTaskAsync(_agent, Task("t", new TaskRetryPolicy(3)), Context(), TestContext.Current.CancellationToken));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldNotRetry_WhenTheRunSuspendedAndTheTaskEndedFailed()
    {
        var run = new RunScope(_id, "crew");
        var inner = new FakeScriptedAgentExecutionService(ct =>
        {
            run.Suspend(Question);
            return FakeScriptedAgentExecutionService.Fail("tool failed")(ct);
        });
        var retrying = new RetryingAgentExecutionService(inner, NullLogger<RetryingAgentExecutionService>.Instance, new FakeImmediateTimeProvider());

        using (_scope.Enter(run))
            await retrying.ExecuteTaskAsync(_agent, Task("t", new TaskRetryPolicy(3)), Context(), TestContext.Current.CancellationToken);

        Assert.Equal(1, inner.Calls);
    }
}
