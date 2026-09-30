using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Consensus;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Process.Consensus;

/// <summary>
/// GAP-04 — the default ballot port: the voter casts its ballot through its own execution,
/// asked for a JSON object; what it cannot read is an abstention, never a failed task.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "MockMemoryScope has a no-op Dispose.")]
public sealed class AgentBallotCollectorTests
{
    private static readonly IReadOnlySet<string> Offered = new HashSet<string>(StringComparer.Ordinal) { "A", "B", "C" };

    [Fact]
    public void A_ranking_of_offered_labels_is_read_best_first()
    {
        var ballot = AgentBallotCollector.Parse(
            """{"ranking": ["C", "A"], "abstain": false, "confidence": 0.8, "justification": "C cites the tables"}""", Offered);

        Assert.False(ballot.Abstained);
        Assert.Equal("C,A", string.Join(",", ballot.Ranking));
        Assert.Equal(0.8f, ballot.Confidence, 0.001f);
        Assert.Equal("C cites the tables", ballot.Justification);
    }

    [Fact]
    public void Labels_not_offered_and_repeats_are_dropped()
    {
        // "Z" is the voter's own answer, never offered to it.
        var ballot = AgentBallotCollector.Parse(
            "```json\n{\"ranking\": [\"Z\", \"B\", \"B\", \"A\"]}\n```", Offered);

        Assert.Equal("B,A", string.Join(",", ballot.Ranking));
        Assert.Equal(1f, ballot.Confidence);
    }

    [Theory]
    [InlineData("I prefer answer B.")]
    [InlineData("{\"ranking\": [\"B\", }")]
    [InlineData("{\"ranking\": [\"Z\"]}")]
    [InlineData("{\"ranking\": [\"A\"], \"abstain\": true}")]
    [InlineData("")]
    public void An_unreadable_or_refused_ballot_is_an_abstention(string reply)
    {
        var ballot = AgentBallotCollector.Parse(reply, Offered);

        Assert.True(ballot.Abstained);
        Assert.False(string.IsNullOrWhiteSpace(ballot.Justification));
    }

    [Fact]
    public void The_confidence_is_clamped()
    {
        Assert.Equal(1f, AgentBallotCollector.Parse("""{"ranking": ["A"], "confidence": 7}""", Offered).Confidence);
        Assert.Equal(0f, AgentBallotCollector.Parse("""{"ranking": ["A"], "confidence": -1}""", Offered).Confidence);
    }

    [Fact]
    public async Task The_voter_casts_its_ballot_through_its_own_execution_asked_for_json()
    {
        var execution = new MockAgentExecutionService();
        execution.SetExecuteResult(new TaskResult(true, """{"ranking": ["B", "A"]}""", null, [], TimeSpan.Zero, TokensUsed: 42));
        var collector = new AgentBallotCollector(execution, NullLogger<AgentBallotCollector>.Instance);
        var (request, voter) = Request();

        var ballot = await collector.CollectAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("B,A", string.Join(",", ballot.Ranking));
        Assert.Equal(42, ballot.Execution?.TokensUsed);
        Assert.Same(voter, execution.LastExecuteAgent);
        Assert.Same(request.Context, execution.LastExecuteContext);
        var ballotTask = Assert.IsType<CrewTask>(execution.LastExecuteTask);
        Assert.Equal("json_object", ballotTask.LlmOverride?.ResponseFormat?.Type);
        Assert.Contains("Candidate A", ballotTask.Description.Value, StringComparison.Ordinal);
        Assert.Contains("the Pacific answer", ballotTask.Description.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(voter.Role.Value, ballotTask.Description.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_ballot_execution_is_an_abstention_that_keeps_its_cost()
    {
        var execution = new MockAgentExecutionService();
        execution.SetExecuteResult(new TaskResult(false, "", null, [], TimeSpan.Zero, Error: "rate limited", TokensUsed: 7));
        var collector = new AgentBallotCollector(execution, NullLogger<AgentBallotCollector>.Instance);

        var ballot = await collector.CollectAsync(Request().Request, TestContext.Current.CancellationToken);

        Assert.True(ballot.Abstained);
        Assert.Contains("rate limited", ballot.Justification, StringComparison.Ordinal);
        Assert.Equal(7, ballot.Execution?.TokensUsed);
    }

    [Fact]
    public void A_long_answer_is_truncated_to_fit_the_task_description()
    {
        var (request, _) = Request(new string('x', 40_000));

        var prompt = AgentBallotCollector.BuildPrompt(request);

        Assert.True(prompt.Length < Orkeon.Domain.Constants.Task.TaskDefaults.TaskDescriptionMaxLength);
        Assert.Contains("truncated", prompt, StringComparison.Ordinal);
        Assert.NotNull(Orkeon.Domain.Task.ValueObjects.TaskDescription.From(prompt));
    }

    private static (BallotRequest Request, Orkeon.Domain.Agent.Agent Voter) Request(string secondAnswer = "the Pacific answer")
    {
        var voter = new AgentBuilder().Role("Senior Analyst").Goal("Judge").Backstory("Judges").Build();
        var task = new CrewTaskBuilder().Description("Which ocean is largest?").ExpectedOutput("An ocean").Build();
        var request = new BallotRequest
        {
            Voter = voter,
            Task = task,
            Candidates =
            [
                new BallotCandidate { Label = "A", Output = "the Atlantic answer" },
                new BallotCandidate { Label = "B", Output = secondAnswer },
            ],
            Context = new SimpleExecutionContext(CrewId.Create(), [], new MockMemoryScope(), []),
        };
        return (request, voter);
    }
}
