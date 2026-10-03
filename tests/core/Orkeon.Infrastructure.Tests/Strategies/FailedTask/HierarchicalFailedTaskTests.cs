using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Tests.Strategies.FailedTask;

/// <summary>
/// GAP-03 — a hierarchical crew with a failed task is a failed crew. It used to report
/// success whatever the manager decided: a task rejected three times ended "[NEEDS REVISION]",
/// and the runner still exited 0.
/// </summary>
public sealed class HierarchicalFailedTaskTests : IDisposable
{
    private readonly FailedTaskFixture _fixture = new();
    private readonly MockManagerAgent _manager = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_task_that_failed_fails_the_crew_and_names_the_reason()
    {
        var extract = _fixture.Task("extract");
        var consolidate = _fixture.Task("consolidate");
        _fixture.Fail("consolidate", "no final answer");

        var output = await RunAsync(extract, consolidate);

        _fixture.AssertFailed(output, consolidate);
        Assert.Contains("no final answer", output.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(extract.Id.ToString(), output.Error, StringComparison.Ordinal);
        Assert.Equal(2, output.TaskOutputs.Count);
    }

    [Fact]
    public async Task A_task_the_manager_keeps_rejecting_fails_the_crew_and_names_the_reason()
    {
        var draft = _fixture.Task("draft");
        _manager.SetReviewResult(false);

        var output = await RunAsync(draft);

        _fixture.AssertFailed(output, draft);
        Assert.Contains("rejected", output.Error, StringComparison.Ordinal);
        Assert.Contains("[NEEDS REVISION]", Assert.Single(output.TaskOutputs).Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_task_whose_dependency_failed_is_skipped_and_fails_the_crew()
    {
        var extract = _fixture.Task("extract");
        var consolidate = _fixture.Task("consolidate", extract);
        var publish = _fixture.Task("publish", consolidate);
        var unrelated = _fixture.Task("unrelated");
        _fixture.Fail("extract", "source unreachable");

        var output = await RunAsync(extract, consolidate, publish, unrelated);

        _fixture.AssertFailed(output, extract, consolidate, publish);
        Assert.Equal(["extract", "unrelated"], _fixture.Executed);
        Assert.Contains("skipped", output.Error, StringComparison.Ordinal);
        Assert.Equal(4, output.TaskOutputs.Count);
        Assert.Equal(
            [consolidate.Id.ToString(), publish.Id.ToString()],
            _fixture.Hook.CompletedTasks.Where(t => t.Skipped).Select(t => t.TaskId));
    }

    [Fact]
    public async Task A_crew_whose_tasks_all_succeeded_still_completes()
    {
        var output = await RunAsync(_fixture.Task("a"), _fixture.Task("b"));

        _fixture.AssertCompleted(output);
    }

    private async Task<Domain.Crew.CrewOutput> RunAsync(params CrewTask[] tasks)
    {
        var lead = _fixture.Agent("Lead");
        var worker = _fixture.Agent("Worker");
        _manager.SetAssignResult(new TaskAssignment(tasks[0].Id, worker.Id, "the only worker", DateTime.UtcNow));

        DomainCrew crew = FailedTaskFixture.Build(
            new CrewBuilder().Goal("Pipeline").Hierarchical(lead), [lead, worker], tasks);
        var strategy = new HierarchicalProcessStrategy(
            _fixture.Dependencies.TaskRepository,
            _fixture.Dependencies.AgentRepository,
            NullLogger<HierarchicalProcessStrategy>.Instance,
            _manager,
            TestManagerLlm.Resolver(),
            _fixture.Execution,
            _fixture.MemoryScope,
            new MockMemoryCoordinator(),
            _fixture.Hook);

        return await strategy.ExecuteHierarchicalAsync(
            crew, lead.Id, cancellationToken: TestContext.Current.CancellationToken);
    }
}
