using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Agent.Events;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.Events;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Infrastructure.Tests.Strategies.Lifecycle;

/// <summary>
/// GAP-21 — a graph run starts a task once, keeps it running through its retries — under the agent
/// it was given — and ends it once, when it succeeds or gives up.
/// </summary>
public sealed class GraphLifecycleTests : IDisposable
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
        // Retried once, failed twice: started once, failed once.
        await _fixture.AssertTaskLifecycleAsync(draft, review, publish, "rate limited");
        _fixture.AssertAgentLifecycle(worker, draft, review, "rate limited");
        _fixture.AssertDispatchedAsTheRunWent(draft, review);
    }

    [Fact]
    public async Task A_task_that_recovers_on_retry_is_started_once_and_completed_once_by_the_same_agent()
    {
        var first = _fixture.Agent("First");
        var second = _fixture.Agent("Second");
        var flaky = _fixture.Task("flaky");
        _fixture.FailFirst("flaky", 1, "timeout");

        var output = await RunAsync([first, second], flaky);

        Assert.True(output.Success, output.Error);
        Assert.Single(_fixture.Dispatched<TaskStartedEvent>());
        Assert.Equal(first.Id, Assert.Single(_fixture.Dispatched<TaskCompletedEvent>()).AgentId);
        Assert.Empty(_fixture.Dispatched<TaskFailedEvent>());
        Assert.Equal(first.Id, Assert.Single(_fixture.Dispatched<AgentStartedTaskEvent>()).AgentId);
        Assert.Empty(_fixture.Dispatched<AgentFailedTaskEvent>());
        Assert.Equal(TaskStatus.Completed, flaky.Status);
    }

    [Fact]
    public async Task A_circuit_break_fails_the_task_it_caught_running()
    {
        var worker = _fixture.Agent("Worker");
        var stubborn = _fixture.Task("stubborn");
        _fixture.Fail("stubborn", "still broken");

        var output = await RunAsync([worker], [stubborn], new GraphConfig { MaxRetryCycles = 100, MaxStateVisits = 2 });

        Assert.False(output.Success);
        Assert.Contains("circuit breaker", output.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TaskStatus.Failed, stubborn.Status);
        Assert.StartsWith("the run stopped:", Assert.Single(_fixture.Dispatched<TaskFailedEvent>()).ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(worker.CurrentTasks);
    }

    private Task<Domain.Crew.CrewOutput> RunAsync(DomainAgent[] agents, params CrewTask[] tasks) =>
        RunAsync(agents, tasks, new GraphConfig { MaxRetryCycles = 1 });

    private async Task<Domain.Crew.CrewOutput> RunAsync(DomainAgent[] agents, CrewTask[] tasks, GraphConfig graphConfig)
    {
        var crew = LifecycleFixture.Build(
            new CrewBuilder().Goal("Graph").Process(ProcessType.Graph).WithGraphConfig(graphConfig), agents, tasks);
        var strategy = new GraphProcessStrategy(
            _fixture.Dependencies,
            new AgentDelegationToolsProvider(
                new MockAgentCommunicationService(), _fixture.Execution,
                NullLogger<AgentDelegationToolsProvider>.Instance),
            NullLogger<GraphProcessStrategy>.Instance,
            _fixture.Hook);

        return await strategy.ExecuteSequentialAsync(
            crew, cancellationToken: TestContext.Current.CancellationToken);
    }
}
