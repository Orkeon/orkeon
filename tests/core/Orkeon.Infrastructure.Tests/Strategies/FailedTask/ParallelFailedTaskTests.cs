using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Crew.Strategies;

namespace Orkeon.Infrastructure.Tests.Strategies.FailedTask;

/// <summary>
/// GAP-03 — a parallel crew with a failed task is a failed crew. The execution barrier turns
/// an agent's error into a failed task result, so only an exception reaching the strategy used
/// to fail the crew; and the next wave ran on the failed output as if it were a deliverable.
/// </summary>
public sealed class ParallelFailedTaskTests : IDisposable
{
    private readonly FailedTaskFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_task_that_failed_fails_the_crew_and_names_the_reason()
    {
        var left = _fixture.Task("left");
        var right = _fixture.Task("right");
        _fixture.Fail("right", "rate limited");

        var output = await RunAsync(left, right);

        _fixture.AssertFailed(output, right);
        Assert.Contains("rate limited", output.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(left.Id.ToString(), output.Error, StringComparison.Ordinal);
        Assert.Equal(2, output.TaskOutputs.Count);
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
        Assert.Equal(["extract", "unrelated"], _fixture.Executed.Order());
        Assert.Contains("skipped", output.Error, StringComparison.Ordinal);
        Assert.Equal(4, output.TaskOutputs.Count);
        Assert.Equal(
            [consolidate.Id.ToString(), publish.Id.ToString()],
            _fixture.Hook.CompletedTasks.Where(t => t.Skipped).Select(t => t.TaskId));
    }

    [Fact]
    public async Task A_crew_whose_tasks_all_succeeded_still_completes()
    {
        var first = _fixture.Task("a");

        var output = await RunAsync(first, _fixture.Task("b", first));

        _fixture.AssertCompleted(output);
    }

    private async Task<Domain.Crew.CrewOutput> RunAsync(params CrewTask[] tasks)
    {
        var worker = _fixture.Agent("Worker");
        var crew = FailedTaskFixture.Build(new CrewBuilder().Goal("Fan-out").Parallel(), [worker], tasks);
        var dependencies = _fixture.Dependencies;
        var strategy = new ParallelProcessStrategy(
            dependencies.TaskRepository,
            dependencies.AgentRepository,
            _fixture.Execution,
            _fixture.MemoryScope,
            NullLogger<ParallelProcessStrategy>.Instance,
            _fixture.Hook);

        return await strategy.ExecuteParallelAsync(
            crew, cancellationToken: TestContext.Current.CancellationToken);
    }
}
