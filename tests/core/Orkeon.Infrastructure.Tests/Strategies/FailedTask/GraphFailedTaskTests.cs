using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Tests.Strategies.FailedTask;

/// <summary>
/// GAP-03 — a graph crew with a task still failing after its retries is a failed crew (only
/// the circuit breaker used to fail one); and a healthy graph crew is no longer cut short by
/// the Strict preset's five visits — its bounds are computed from the crew.
/// </summary>
public sealed class GraphFailedTaskTests : IDisposable
{
    private readonly FailedTaskFixture _fixture = new();

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
        // The graph runs the task once: its attempts are its retry: policy, applied below the strategy (FWK-15).
        Assert.Equal(1, _fixture.Executed.Count(name => name == "consolidate"));
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
        Assert.DoesNotContain("consolidate", _fixture.Executed);
        Assert.DoesNotContain("publish", _fixture.Executed);
        Assert.Contains("unrelated", _fixture.Executed);
        Assert.Contains("skipped", output.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(30)]
    public async Task A_healthy_crew_without_graphConfig_completes_whatever_its_size(int taskCount)
    {
        var tasks = Enumerable.Range(1, taskCount).Select(i => _fixture.Task($"task {i}")).ToArray();

        var output = await RunAsync(tasks);

        _fixture.AssertCompleted(output);
        Assert.Equal(taskCount, output.TaskOutputs.Count);
    }

    [Fact]
    public async Task An_explicit_maxTransitions_still_wins_over_the_computed_bound()
    {
        // One node per task (ADR-018): the transitions are the node executions of the whole run, and an
        // explicit bound cuts a healthy crew short where the computed one would not.
        var tasks = new[] { _fixture.Task("a"), _fixture.Task("b"), _fixture.Task("c") };

        var output = await RunAsync(tasks, new GraphConfig { MaxTransitions = 2, MaxStateVisits = 10 });

        Assert.False(output.Success);
        Assert.Contains("circuit breaker", output.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_crew_whose_tasks_all_succeeded_still_completes()
    {
        var output = await RunAsync(_fixture.Task("a"), _fixture.Task("b"));

        _fixture.AssertCompleted(output);
    }

    private Task<Domain.Crew.CrewOutput> RunAsync(params CrewTask[] tasks) => RunAsync(tasks, graphConfig: null);

    private async Task<Domain.Crew.CrewOutput> RunAsync(CrewTask[] tasks, GraphConfig? graphConfig)
    {
        var worker = _fixture.Agent("Worker");
        var builder = new CrewBuilder().Goal("Graph").Process(ProcessType.Graph);
        if (graphConfig is not null)
            builder.WithGraphConfig(graphConfig);
        DomainCrew crew = FailedTaskFixture.Build(builder, [worker], tasks);

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
