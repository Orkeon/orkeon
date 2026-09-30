using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Consensus;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using CrewExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;

namespace Orkeon.Infrastructure.Tests.Strategies.FailedTask;

/// <summary>
/// GAP-03 — a consensual crew whose retained result failed is a failed crew. Only the
/// <c>Fail</c> fallback used to fail one, and it stopped the crew on the spot; a vote won by a
/// failed execution reported success.
/// </summary>
public sealed class ConsensualFailedTaskTests : IDisposable
{
    private readonly FailedTaskFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_task_that_failed_fails_the_crew_and_names_the_reason()
    {
        var extract = _fixture.Task("extract");
        var consolidate = _fixture.Task("consolidate");
        _fixture.Fail("consolidate", "no final answer");

        var output = await RunAsync(new ConsensualProcessOptions(), [_fixture.Agent("Solo")], extract, consolidate);

        _fixture.AssertFailed(output, consolidate);
        Assert.Contains("no final answer", output.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(extract.Id.ToString(), output.Error, StringComparison.Ordinal);
        Assert.Equal(2, output.TaskOutputs.Count);
    }

    [Fact]
    public async Task A_task_without_consensus_fails_the_crew_and_its_dependants_are_skipped()
    {
        var draft = _fixture.Task("draft");
        var review = _fixture.Task("review", draft);
        var options = new ConsensualProcessOptions
        {
            MaxVotingRounds = 1,
            EnableDiscussion = false,
            FallbackStrategy = ConsensusFallback.Fail,
            VotingOptions = new VotingOptions { ConsensusType = ConsensusType.Unanimity },
        };

        var output = await RunAsync(options, [_fixture.Agent("Alpha"), _fixture.Agent("Beta")], draft, review);

        _fixture.AssertFailed(output, draft, review);
        Assert.Contains("Consensus could not be reached", output.Error, StringComparison.Ordinal);
        Assert.Contains("skipped", output.Error, StringComparison.Ordinal);
        Assert.Equal(["draft", "draft"], _fixture.Executed);
    }

    [Fact]
    public async Task A_task_whose_dependency_failed_is_skipped_and_fails_the_crew()
    {
        var extract = _fixture.Task("extract");
        var consolidate = _fixture.Task("consolidate", extract);
        var publish = _fixture.Task("publish", consolidate);
        var unrelated = _fixture.Task("unrelated");
        _fixture.Fail("extract", "source unreachable");

        var output = await RunAsync(
            new ConsensualProcessOptions(), [_fixture.Agent("Solo")], extract, consolidate, publish, unrelated);

        _fixture.AssertFailed(output, extract, consolidate, publish);
        Assert.Equal(["extract", "unrelated"], _fixture.Executed);
        Assert.Equal(4, output.TaskOutputs.Count);
        Assert.Equal(
            [consolidate.Id.ToString(), publish.Id.ToString()],
            _fixture.Hook.CompletedTasks.Where(t => t.Skipped).Select(t => t.TaskId));
    }

    [Fact]
    public async Task A_crew_whose_tasks_all_succeeded_still_completes()
    {
        var output = await RunAsync(
            new ConsensualProcessOptions(), [_fixture.Agent("Solo")], _fixture.Task("a"), _fixture.Task("b"));

        _fixture.AssertCompleted(output);
    }

    private async Task<Domain.Crew.CrewOutput> RunAsync(
        ConsensualProcessOptions options, DomainAgent[] agents, params CrewTask[] tasks)
    {
        var crew = FailedTaskFixture.Build(new CrewBuilder().Goal("Vote").Consensual(), agents, tasks);
        var strategy = new ConsensualProcessStrategy(
            new MajorityVotingStrategy(),
            new FakeBallotCollector(),
            _fixture.Dependencies,
            NullLogger<ConsensualProcessStrategy>.Instance,
            Options.Create(options),
            _fixture.Hook);

        return await strategy.ExecuteConsensualAsync(
            crew, CrewExecutionPlan.Create(), ct: TestContext.Current.CancellationToken);
    }
}
