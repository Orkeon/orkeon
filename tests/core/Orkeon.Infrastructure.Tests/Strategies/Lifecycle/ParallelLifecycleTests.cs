using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Agent.Events;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.Events;
using Orkeon.Infrastructure.Crew.Strategies;
using CrewExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Strategies.Lifecycle;

/// <summary>
/// GAP-21 — a parallel run starts each task of a wave as it launches it and ends them once the wave
/// has joined; one agent given two tasks of a wave runs both at once.
/// </summary>
public sealed class ParallelLifecycleTests : IDisposable
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
        // One agent, two tasks in one wave: it ran both at once, and ended both.
        _fixture.AssertAgentLifecycle(worker, draft, review, "rate limited");
    }

    [Fact]
    public async Task A_task_of_a_wave_is_started_before_its_agent_runs_it_and_a_wave_ends_before_the_next_starts()
    {
        var worker = _fixture.Agent("Worker");
        var extract = _fixture.Task("extract");
        var consolidate = _fixture.Task("consolidate", extract);

        var output = await RunAsync([worker], extract, consolidate);

        Assert.True(output.Success, output.Error);
        var started = Assert.Single(_fixture.Dispatched<TaskStartedEvent>(), e => e.TaskId == extract.Id);
        Assert.True(_fixture.IndexOf(started) < _fixture.DispatchedBefore("extract"));
        _fixture.AssertDispatchedAsTheRunWent(extract, consolidate);
        Assert.Equal(2, _fixture.Dispatched<AgentCompletedTaskEvent>().Count);
    }

    private async Task<Domain.Crew.CrewOutput> RunAsync(DomainAgent[] agents, params CrewTask[] tasks)
    {
        var crew = LifecycleFixture.Build(new CrewBuilder().Goal("Fan-out").Parallel(), agents, tasks);
        var dependencies = _fixture.Dependencies;
        var strategy = new ParallelProcessStrategy(
            dependencies.TaskRepository,
            dependencies.AgentRepository,
            _fixture.Execution,
            _fixture.MemoryScope,
            NullLogger<ParallelProcessStrategy>.Instance,
            _fixture.Hook,
            domainEvents: _fixture.Events);

        return await strategy.ExecuteParallelAsync(
            crew, CrewExecutionPlan.Create(), cancellationToken: TestContext.Current.CancellationToken);
    }
}
