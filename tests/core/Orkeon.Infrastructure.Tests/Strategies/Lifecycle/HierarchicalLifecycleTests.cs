using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Agent.Events;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.Events;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Infrastructure.Tests.Strategies.Lifecycle;

/// <summary>
/// GAP-21 — in a hierarchical run the agent the manager assigns is the one that starts the task,
/// and its revisions are part of its work on it: one start, one end.
/// </summary>
public sealed class HierarchicalLifecycleTests : IDisposable
{
    private readonly LifecycleFixture _fixture = new();
    private readonly MockManagerAgent _manager = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Each_task_is_started_and_ended_as_the_run_goes_and_the_repository_says_how_it_went()
    {
        var lead = _fixture.Agent("Lead");
        var worker = _fixture.Agent("Worker");
        var draft = _fixture.Task("draft");
        var review = _fixture.Task("review");
        var publish = _fixture.Task("publish", review);
        _fixture.Fail("review", "rate limited");
        _manager.SetAssignResult(new TaskAssignment(draft.Id, worker.Id, "the only worker", DateTime.UtcNow));

        var output = await RunAsync(lead, [worker], draft, review, publish);

        Assert.False(output.Success);
        await _fixture.AssertTaskLifecycleAsync(draft, review, publish, "rate limited");
        _fixture.AssertAgentLifecycle(worker, draft, review, "rate limited");
        _fixture.AssertDispatchedAsTheRunWent(draft, review);
        Assert.DoesNotContain(_fixture.Dispatched<AgentStartedTaskEvent>(), e => e.AgentId == lead.Id);
    }

    [Fact]
    public async Task The_agent_the_manager_assigns_starts_the_task_even_when_another_was_declared()
    {
        var lead = _fixture.Agent("Lead");
        var declared = _fixture.Agent("Declared");
        var chosen = _fixture.Agent("Chosen");
        var draft = new CrewTaskBuilder().Description("draft").ExpectedOutput("draft").AssignTo(declared).Build();
        await _fixture.Tasks.AddAsync(draft, TestContext.Current.CancellationToken);
        _manager.SetAssignResult(new TaskAssignment(draft.Id, chosen.Id, "better fit", DateTime.UtcNow));
        _manager.SetReviewResults(false, true);

        var output = await RunAsync(lead, [declared, chosen], draft);

        Assert.True(output.Success, output.Error);
        Assert.Equal(chosen.Id, draft.AssignedAgent);
        Assert.Equal(chosen.Id, Assert.Single(_fixture.Dispatched<TaskStartedEvent>()).AgentId);
        Assert.Equal(chosen.Id, Assert.Single(_fixture.Dispatched<TaskCompletedEvent>()).AgentId);
        // A revision is the agent's work on its task, not a new start.
        Assert.Equal(chosen.Id, Assert.Single(_fixture.Dispatched<AgentStartedTaskEvent>()).AgentId);
        Assert.DoesNotContain(_fixture.Dispatched<AgentStartedTaskEvent>(), e => e.AgentId == declared.Id);
    }

    [Fact]
    public async Task A_task_the_manager_gives_to_an_agent_outside_the_crew_fails_without_starting()
    {
        var lead = _fixture.Agent("Lead");
        var worker = _fixture.Agent("Worker");
        var draft = _fixture.Task("draft");
        _manager.SetAssignResult(new TaskAssignment(draft.Id, AgentId.Create(), "a stranger", DateTime.UtcNow));

        var output = await RunAsync(lead, [worker], draft);

        Assert.False(output.Success);
        Assert.Equal(TaskStatus.Failed, draft.Status);
        Assert.Null(draft.StartedAt);
        Assert.Empty(_fixture.Dispatched<TaskStartedEvent>());
        Assert.Contains("not a worker of this crew", Assert.Single(_fixture.Dispatched<TaskFailedEvent>()).ErrorMessage, StringComparison.Ordinal);
    }

    private async Task<Domain.Crew.CrewOutput> RunAsync(DomainAgent lead, DomainAgent[] workers, params CrewTask[] tasks)
    {
        var crew = LifecycleFixture.Build(
            new CrewBuilder().Goal("Pipeline").Hierarchical(lead), [lead, .. workers], tasks);
        var strategy = new HierarchicalProcessStrategy(
            _fixture.Tasks,
            _fixture.Agents,
            NullLogger<HierarchicalProcessStrategy>.Instance,
            _manager,
            TestManagerLlm.Resolver(),
            _fixture.Execution,
            _fixture.MemoryScope,
            new MockMemoryCoordinator(),
            _fixture.Hook,
            _fixture.Events);

        return await strategy.ExecuteHierarchicalAsync(
            crew, lead.Id, cancellationToken: TestContext.Current.CancellationToken);
    }
}
