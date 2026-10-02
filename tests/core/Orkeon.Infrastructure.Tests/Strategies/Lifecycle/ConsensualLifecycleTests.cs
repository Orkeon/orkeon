using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent.Events;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.Events;
using Orkeon.Infrastructure.Consensus;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Infrastructure.Tests.Strategies.Lifecycle;

/// <summary>
/// GAP-21 — a consensual task starts once, not once per candidate: every agent that answers it
/// starts it and ends it with its own answer, and the task completes under the author of the answer
/// the vote retained.
/// </summary>
public sealed class ConsensualLifecycleTests : IDisposable
{
    private readonly LifecycleFixture _fixture = new();
    private readonly FakeBallotCollector _ballots = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Each_task_is_started_and_ended_as_the_run_goes_and_the_repository_says_how_it_went()
    {
        var solo = _fixture.Agent("Solo");
        var draft = _fixture.Task("draft");
        var review = _fixture.Task("review");
        var publish = _fixture.Task("publish", review);
        _fixture.Fail("review", "rate limited");

        var output = await RunAsync([solo], draft, review, publish);

        Assert.False(output.Success);
        await _fixture.AssertTaskLifecycleAsync(draft, review, publish, "rate limited");
        _fixture.AssertAgentLifecycle(solo, draft, review, "rate limited");
        _fixture.AssertDispatchedAsTheRunWent(draft, review);
    }

    [Fact]
    public async Task A_voted_task_starts_once_and_completes_under_the_author_of_the_retained_answer()
    {
        var ann = _fixture.Agent("Ann");
        var bob = _fixture.Agent("Bob");
        var cid = _fixture.Agent("Cid");
        var draft = _fixture.Task("draft");
        _fixture.Execution.SetExecuteFunc((agent, _, _, _) =>
            new TaskResult(true, $"{agent.Role.Value}'s answer", null, [], TimeSpan.Zero));
        _ballots.Vote = request => FakeBallotCollector.Prefer(request, "Bob's answer");

        var output = await RunAsync([ann, bob, cid], draft);

        Assert.True(output.Success, output.Error);
        var started = Assert.Single(_fixture.Dispatched<TaskStartedEvent>());
        Assert.Null(started.AgentId);
        var completed = Assert.Single(_fixture.Dispatched<TaskCompletedEvent>());
        Assert.Equal(bob.Id, completed.AgentId);
        Assert.Equal("Bob's answer", completed.Output.Output);
        Assert.Equal(bob.Id, draft.AssignedAgent);
        Assert.Equal(TaskStatus.Completed, draft.Status);

        // Every agent answered: each started the task and completed it with its own answer.
        var agentStarts = _fixture.Dispatched<AgentStartedTaskEvent>();
        Assert.All(agentStarts, e => Assert.Equal(draft.Id, e.TaskId));
        Assert.Equal(
            new HashSet<Orkeon.Domain.Common.AgentId> { ann.Id, bob.Id, cid.Id },
            agentStarts.Select(e => e.AgentId).ToHashSet());
        Assert.Equal(3, agentStarts.Count);
        var answers = _fixture.Dispatched<AgentCompletedTaskEvent>().ToDictionary(e => e.AgentId, e => e.Output.Output);
        Assert.Equal("Ann's answer", answers[ann.Id]);
        Assert.Equal("Bob's answer", answers[bob.Id]);
        Assert.Equal("Cid's answer", answers[cid.Id]);
        Assert.All(new[] { ann, bob, cid }, agent => Assert.Empty(agent.CurrentTasks));
    }

    private async Task<Domain.Crew.CrewOutput> RunAsync(DomainAgent[] agents, params CrewTask[] tasks)
    {
        var crew = LifecycleFixture.Build(new CrewBuilder().Goal("Vote").Consensual(), agents, tasks);
        var strategy = new ConsensualProcessStrategy(
            new MajorityVotingStrategy(),
            _ballots,
            _fixture.Dependencies,
            new MockMemoryCoordinator(),
            NullLogger<ConsensualProcessStrategy>.Instance,
            Options.Create(new ConsensualProcessOptions()),
            _fixture.Hook);

        return await strategy.ExecuteConsensualAsync(
            crew, ct: TestContext.Current.CancellationToken);
    }
}
