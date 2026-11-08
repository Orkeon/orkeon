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
        // Failed once, and the graph does not run it again: started once, failed once.
        await _fixture.AssertTaskLifecycleAsync(draft, review, publish, "rate limited");
        _fixture.AssertAgentLifecycle(worker, draft, review, "rate limited");
        _fixture.AssertDispatchedAsTheRunWent(draft, review);
    }

    private Task<Domain.Crew.CrewOutput> RunAsync(DomainAgent[] agents, params CrewTask[] tasks) =>
        RunAsync(agents, tasks, new GraphConfig());

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
