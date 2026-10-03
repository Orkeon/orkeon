using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent.Events;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.Events;
using Orkeon.Infrastructure.Communication;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Infrastructure.Tests.Strategies.Lifecycle;

/// <summary>
/// GAP-21 — in an autonomous run the agent that claims a task starts it; a failed task handed to a
/// peer is failed by the first agent and completed by the peer, the task itself starting once.
/// </summary>
public sealed class AutonomousLifecycleTests : IDisposable
{
    private readonly LifecycleFixture _fixture = new();
    private readonly MockManagerAgent _manager = new();
    private readonly InMemoryAgentChannel _channel = new(NullLogger<InMemoryAgentChannel>.Instance);

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Each_task_is_started_and_ended_as_the_run_goes_and_the_repository_says_how_it_went()
    {
        var worker = _fixture.Agent("Worker");
        var draft = _fixture.Task("draft");
        var review = _fixture.Task("review");
        var publish = _fixture.Task("publish", review);
        _fixture.Fail("review", "rate limited");

        var output = await RunAsync(AgentExecutionBudget.Default, [worker], draft, review, publish);

        Assert.False(output.Success);
        await _fixture.AssertTaskLifecycleAsync(draft, review, publish, "rate limited");
        _fixture.AssertAgentLifecycle(worker, draft, review, "rate limited");
        _fixture.AssertDispatchedAsTheRunWent(draft, review);
    }

    [Fact]
    public async Task A_task_delegated_to_a_peer_is_failed_by_its_agent_and_completed_by_the_peer()
    {
        var first = _fixture.Agent("First", allowDelegation: true);
        var peer = _fixture.Agent("Peer");
        var draft = _fixture.Task("draft");
        _fixture.Execution.SetExecuteFunc((agent, task, _, _) => agent.Id == first.Id
            ? new TaskResult(false, "", null, [], TimeSpan.Zero, Error: "no final answer")
            : new TaskResult(true, $"{task.Description.Value} by the peer", null, [], TimeSpan.Zero));

        var output = await RunAsync(AgentExecutionBudget.Default, [first, peer], draft);

        Assert.True(output.Success, output.Error);
        Assert.Equal(first.Id, Assert.Single(_fixture.Dispatched<TaskStartedEvent>()).AgentId);
        Assert.Contains(_fixture.Dispatched<TaskAssignedEvent>(), e => e.AgentId == peer.Id);
        Assert.Equal(peer.Id, Assert.Single(_fixture.Dispatched<TaskCompletedEvent>()).AgentId);
        var failed = Assert.Single(_fixture.Dispatched<AgentFailedTaskEvent>());
        Assert.Equal(first.Id, failed.AgentId);
        Assert.Equal("no final answer", failed.Reason);
        Assert.Equal(peer.Id, Assert.Single(_fixture.Dispatched<AgentCompletedTaskEvent>()).AgentId);
        Assert.Equal(TaskStatus.Completed, draft.Status);
        Assert.Equal(peer.Id, draft.AssignedAgent);
    }

    [Fact]
    public async Task Tasks_an_exhausted_budget_never_reached_are_cancelled_with_the_reason()
    {
        var worker = _fixture.Agent("Worker");
        var draft = _fixture.Task("draft");
        var review = _fixture.Task("review");

        var output = await RunAsync(new AgentExecutionBudget { MaxWallTime = TimeSpan.Zero }, [worker], draft, review);

        Assert.False(output.Success);
        Assert.Equal(TaskStatus.Cancelled, draft.Status);
        Assert.Equal(TaskStatus.Cancelled, review.Status);
        Assert.All(_fixture.Dispatched<TaskCancelledEvent>(), e => Assert.StartsWith("not executed:", e.Reason, StringComparison.Ordinal));
        Assert.Equal(2, _fixture.Dispatched<TaskCancelledEvent>().Count);
        Assert.Empty(_fixture.Dispatched<TaskStartedEvent>());
    }

    private async Task<Domain.Crew.CrewOutput> RunAsync(
        AgentExecutionBudget budget, DomainAgent[] agents, params CrewTask[] tasks)
    {
        _manager.SetAssignResult(new TaskAssignment(tasks[0].Id, agents[0].Id, "first agent", DateTime.UtcNow));
        var crew = LifecycleFixture.Build(
            new CrewBuilder().Goal("Autonomous").Process(ProcessType.Autonomous), agents, tasks);
        var strategy = new AutonomousProcessStrategy(
            _fixture.Dependencies,
            _channel,
            _manager,
            TestManagerLlm.Resolver(),
            NullLogger<AutonomousProcessStrategy>.Instance,
            _fixture.Hook);

        return await strategy.ExecuteAutonomousAsync(
            crew, budget, cancellationToken: TestContext.Current.CancellationToken);
    }
}
