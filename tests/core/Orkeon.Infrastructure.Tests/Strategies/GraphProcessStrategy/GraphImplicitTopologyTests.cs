using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.Strategies.FailedTask;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Tests.Strategies;

/// <summary>
/// FWK-27 (ADR-018, point 7): a Graph-process crew with no <c>graph:</c> block runs one node per
/// task, its id the task's name, chained in execution order, the last one leading to END. The
/// queue-and-route loop of the single <c>execute_task</c> node is gone.
/// </summary>
public sealed class GraphImplicitTopologyTests : IDisposable
{
    private readonly FailedTaskFixture _fixture = new();
    private readonly MockGraphNodeObserver _observer = new();

    public void Dispose() => _fixture.Dispose();

    private CrewTask Keyed(string key, params CrewTask[] dependsOn)
    {
        var builder = new CrewTaskBuilder().Description(key).ExpectedOutput(key).WithKey(key);
        if (dependsOn.Length > 0)
            builder.DependsOn(dependsOn);
        var task = builder.Build();
        _fixture.Register(task);
        return task;
    }

    private async Task<Orkeon.Domain.Crew.CrewOutput> RunAsync(CrewTask[] tasks, GraphConfig? graphConfig = null)
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
            _fixture.Hook,
            graphObserver: _observer);

        return await strategy.ExecuteSequentialAsync(
            crew, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("BR", "FWK-27")]
    public async Task ShouldEnterOneNodePerTask_InExecutionOrder_NamedAfterTheTask()
    {
        var output = await RunAsync([Keyed("first"), Keyed("second"), Keyed("third")]);

        _fixture.AssertCompleted(output);
        Assert.Equal(["first", "second", "third"], _observer.Entered.Select(e => e.Node).ToArray());
        Assert.All(_observer.Entered, e => Assert.Equal(1, e.Visit));
        Assert.DoesNotContain(_observer.Entered, e => e.Node is "execute_task" or "route");
    }

    [Fact]
    [Trait("BR", "FWK-27")]
    public async Task ShouldNameTheTaskIdOnEachEntry()
    {
        var first = Keyed("first");
        var second = Keyed("second");

        await RunAsync([first, second]);

        Assert.Equal(
            [first.Id.Value.ToString(), second.Id.Value.ToString()],
            _observer.Entered.Select(e => e.Task).ToArray());
    }

    [Fact]
    [Trait("BR", "FWK-27")]
    public async Task ShouldEnterNothing_WhenTheCrewHasNoTask()
    {
        var output = await RunAsync([]);

        Assert.Empty(_observer.Entered);
        Assert.Empty(output.TaskOutputs);
    }

    [Fact]
    [Trait("BR", "FWK-27")]
    public async Task ShouldEnterTheNodeAgain_ForEachRetryOfAFailedTask()
    {
        // The retry the removed execute_task/route loop used to give stays, until FWK-15, as a
        // loop of the task's own node: one more visit, bounded by the breaker.
        var flaky = Keyed("flaky");
        var attempts = 0;
        _fixture.Execution.SetExecuteFunc((_, _, _, _) =>
            ++attempts < 3
                ? new Orkeon.Application.Interfaces.Services.TaskResult(false, "", null, [], TimeSpan.Zero, Error: "flaky")
                : new Orkeon.Application.Interfaces.Services.TaskResult(true, "ok", null, [], TimeSpan.Zero));

        var output = await RunAsync([flaky]);

        _fixture.AssertCompleted(output);
        Assert.Equal([("flaky", 1), ("flaky", 2), ("flaky", 3)], _observer.Entered.Select(e => (e.Node, e.Visit)).ToArray());
    }

    [Fact]
    [Trait("BR", "FWK-27")]
    public async Task ShouldCompleteThreeHealthyTasks_WhenMaxStateVisitsIsTwo()
    {
        // The count is per node: three tasks, one visit each, stay under a bound of two. The
        // single execute_task node used to trip it on the third task.
        var output = await RunAsync(
            [Keyed("a"), Keyed("b"), Keyed("c")], new GraphConfig { MaxStateVisits = 2 });

        _fixture.AssertCompleted(output);
        Assert.Equal(3, output.TaskOutputs.Count);
    }

    [Fact]
    [Trait("BR", "FWK-27")]
    public async Task ShouldTripTheBreaker_WhenANodeIsEnteredMoreOftenThanMaxStateVisits()
    {
        var doomed = Keyed("doomed");
        _fixture.Fail("doomed", "always");

        var output = await RunAsync([doomed], new GraphConfig { MaxStateVisits = 2, MaxRetryCycles = 10 });

        Assert.False(output.Success);
        Assert.Contains("circuit breaker", output.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'doomed'", output.Error, StringComparison.Ordinal);
    }
}
