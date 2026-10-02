using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent.Events;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.Events;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Infrastructure.Tests.Strategies.Lifecycle;

/// <summary>
/// GAP-21 — a sequential run moves each task and its agent through their lifecycle and dispatches
/// their events as it goes. It used to leave every task <c>Pending</c>, without a date, and raise
/// none of <c>TaskStartedEvent</c>, <c>AgentCompletedTaskEvent</c> and their siblings.
/// </summary>
public sealed class SequentialLifecycleTests : IDisposable
{
    private readonly LifecycleFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Each_task_is_started_and_ended_as_the_run_goes_and_the_repository_says_how_it_went()
    {
        var worker = _fixture.Agent("Worker");
        var draft = _fixture.Task("draft");
        var review = _fixture.Task("review");
        var publish = _fixture.Task("publish", review);
        _fixture.Fail("review", "rate limited");

        var output = await RunAsync([worker], draft, review, publish);

        Assert.False(output.Success);
        await _fixture.AssertTaskLifecycleAsync(draft, review, publish, "rate limited");
        _fixture.AssertAgentLifecycle(worker, draft, review, "rate limited");
        _fixture.AssertDispatchedAsTheRunWent(draft, review);
    }

    [Fact]
    public async Task A_task_that_names_no_agent_is_assigned_to_the_agent_that_runs_it()
    {
        var first = _fixture.Agent("First");
        var second = _fixture.Agent("Second");
        var draft = _fixture.Task("draft");
        var review = _fixture.Task("review");

        await RunAsync([first, second], draft, review);

        Assert.Equal(first.Id, draft.AssignedAgent);
        Assert.Equal(second.Id, review.AssignedAgent);
        Assert.Contains(_fixture.Dispatched<TaskAssignedEvent>(), e => e.TaskId == review.Id && e.AgentId == second.Id);
        Assert.Contains(_fixture.Dispatched<AgentAssignedToTaskEvent>(), e => e.TaskId == review.Id && e.AgentId == second.Id);
    }

    [Fact]
    public async Task A_second_run_of_the_same_crew_reopens_its_tasks_and_moves_them_again()
    {
        var worker = _fixture.Agent("Worker");
        var draft = _fixture.Task("draft");
        var crew = LifecycleFixture.Build(new CrewBuilder().Goal("Pipeline").Sequential(), [worker], [draft]);

        var first = await StrategyFor().ExecuteSequentialAsync(crew, cancellationToken: TestContext.Current.CancellationToken);
        var startedFirst = draft.StartedAt;
        var second = await StrategyFor().ExecuteSequentialAsync(crew, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(first.Success, first.Error);
        Assert.True(second.Success, second.Error);
        Assert.Equal(TaskStatus.Completed, draft.Status);
        Assert.True(draft.StartedAt >= startedFirst);
        Assert.Equal(2, _fixture.Dispatched<TaskStartedEvent>().Count);
        Assert.Equal(2, _fixture.Dispatched<TaskCompletedEvent>().Count);
        Assert.Equal(2, _fixture.Dispatched<AgentCompletedTaskEvent>().Count);
        Assert.Contains(
            _fixture.Dispatched<TaskStatusChangedEvent>(),
            e => e.OldStatus == TaskStatus.Completed && e.NewStatus == TaskStatus.Pending);
    }

    [Fact]
    public async Task A_cancelled_run_cancels_the_task_it_was_running_and_its_agent_fails_it()
    {
        var worker = _fixture.Agent("Worker");
        var draft = _fixture.Task("draft");
        var review = _fixture.Task("review");
        _fixture.Execution.SetExecuteFunc((_, task, _, _) => task.Description.Value == "review"
            ? throw new OperationCanceledException()
            : new TaskResult(true, "draft done", null, [], TimeSpan.Zero));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync([worker], draft, review));

        Assert.Equal(TaskStatus.Completed, draft.Status);
        Assert.Equal(TaskStatus.Cancelled, review.Status);
        Assert.Equal("the run was cancelled", Assert.Single(_fixture.Dispatched<TaskCancelledEvent>()).Reason);
        var failed = Assert.Single(_fixture.Dispatched<AgentFailedTaskEvent>());
        Assert.Equal(review.Id, failed.TaskId);
        Assert.Empty(worker.CurrentTasks);
    }

    [Fact]
    public async Task A_handler_that_throws_neither_stops_the_run_nor_the_events_after_it()
    {
        var worker = _fixture.Agent("Worker");
        var draft = _fixture.Task("draft");
        var review = _fixture.Task("review");
        _fixture.Events.ThrowOn = typeof(TaskStartedEvent);

        var output = await RunAsync([worker], draft, review);

        Assert.True(output.Success, output.Error);
        Assert.Equal(2, _fixture.Dispatched<TaskCompletedEvent>().Count);
        Assert.Equal(TaskStatus.Completed, review.Status);
    }

    private SequentialProcessStrategy StrategyFor() =>
        new(
            _fixture.Dependencies,
            new AgentDelegationToolsProvider(
                new MockAgentCommunicationService(), _fixture.Execution,
                NullLogger<AgentDelegationToolsProvider>.Instance),
            NullLogger<SequentialProcessStrategy>.Instance,
            _fixture.Hook);

    private async Task<Domain.Crew.CrewOutput> RunAsync(DomainAgent[] agents, params CrewTask[] tasks)
    {
        var crew = LifecycleFixture.Build(new CrewBuilder().Goal("Pipeline").Sequential(), agents, tasks);
        return await StrategyFor().ExecuteSequentialAsync(
            crew, cancellationToken: TestContext.Current.CancellationToken);
    }
}
