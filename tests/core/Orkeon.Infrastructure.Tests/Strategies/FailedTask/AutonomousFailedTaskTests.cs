using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Communication;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Strategies.FailedTask;

/// <summary>
/// GAP-03 — an autonomous crew with a failed task, or whose budget ran out, is a failed crew.
/// It used to report success in both cases, the hook saying <c>Canceled</c> on an exhausted
/// budget while the output said <c>Success = true</c>.
/// </summary>
public sealed class AutonomousFailedTaskTests : IDisposable
{
    private readonly FailedTaskFixture _fixture = new();
    private readonly MockManagerAgent _manager = new();
    private readonly InMemoryAgentChannel _channel = new(NullLogger<InMemoryAgentChannel>.Instance);

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_task_that_failed_fails_the_crew_and_names_the_reason()
    {
        var extract = _fixture.Task("extract");
        var consolidate = _fixture.Task("consolidate");
        _fixture.Fail("consolidate", "no final answer");

        var output = await RunAsync(AgentExecutionBudget.Default, [Worker()], extract, consolidate);

        _fixture.AssertFailed(output, consolidate);
        Assert.Contains("no final answer", output.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(extract.Id.ToString(), output.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_task_that_failed_with_no_peer_to_delegate_to_fails_the_crew_and_names_the_reason()
    {
        var solo = _fixture.Agent("Solo", allowDelegation: true);
        var draft = _fixture.Task("draft");
        _fixture.Fail("draft", "no final answer");

        // Used to throw "No delegation candidates available." out of the strategy.
        var output = await RunAsync(AgentExecutionBudget.Default, [solo], draft);

        _fixture.AssertFailed(output, draft);
        Assert.Contains("no final answer", output.Error, StringComparison.Ordinal);
        Assert.Contains("no peer", output.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_budget_exhausted_between_two_tasks_fails_the_crew_and_names_the_dimension()
    {
        var draft = _fixture.Task("draft");
        var review = _fixture.Task("review");

        var output = await RunAsync(new AgentExecutionBudget { MaxWallTime = TimeSpan.Zero }, [Worker()], draft, review);

        _fixture.AssertFailed(output, draft, review);
        Assert.StartsWith("Execution budget exhausted: WallTime", output.Error, StringComparison.Ordinal);
        Assert.Contains("not executed", output.Error, StringComparison.Ordinal);
        Assert.Empty(_fixture.Executed);
    }

    [Fact]
    public async Task A_budget_exhausted_inside_a_task_fails_the_crew_and_names_the_dimension()
    {
        var draft = _fixture.Task("draft");
        var review = _fixture.Task("review");

        // No tool call allowed: the first task's assignment call exhausts the budget.
        var output = await RunAsync(new AgentExecutionBudget { MaxToolCalls = 0 }, [Worker()], draft, review);

        _fixture.AssertFailed(output, draft, review);
        Assert.StartsWith("Execution budget exhausted: ToolCalls", output.Error, StringComparison.Ordinal);
        Assert.Contains("BUDGET EXHAUSTED", Assert.Single(output.TaskOutputs).Output, StringComparison.Ordinal);
        Assert.Empty(_fixture.Executed);
    }

    [Fact]
    public async Task A_task_whose_dependency_failed_is_skipped_and_fails_the_crew()
    {
        var extract = _fixture.Task("extract");
        var consolidate = _fixture.Task("consolidate", extract);
        var publish = _fixture.Task("publish", consolidate);
        var unrelated = _fixture.Task("unrelated");
        _fixture.Fail("extract", "source unreachable");

        var output = await RunAsync(AgentExecutionBudget.Default, [Worker()], extract, consolidate, publish, unrelated);

        _fixture.AssertFailed(output, extract, consolidate, publish);
        Assert.Equal(["extract", "unrelated"], _fixture.Executed);
        Assert.Contains("skipped", output.Error, StringComparison.Ordinal);
        Assert.Equal(2, _manager.AssignTaskCallCount);
    }

    [Fact]
    public async Task A_crew_whose_tasks_all_succeeded_still_completes()
    {
        var output = await RunAsync(AgentExecutionBudget.Default, [Worker()], _fixture.Task("a"), _fixture.Task("b"));

        _fixture.AssertCompleted(output);
    }

    private DomainAgent Worker() => _fixture.Agent("Worker");

    private async Task<Domain.Crew.CrewOutput> RunAsync(
        AgentExecutionBudget budget, DomainAgent[] agents, params CrewTask[] tasks)
    {
        _manager.SetAssignResult(new TaskAssignment(tasks[0].Id, agents[0].Id, "first agent", DateTime.UtcNow));
        var crew = FailedTaskFixture.Build(
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
