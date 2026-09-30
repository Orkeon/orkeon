using System.Collections.Immutable;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;

namespace Orkeon.Application.Interfaces;

/// <summary>
/// Collects one ballot in a consensual vote (GAP-04): a voter reads the answers other agents
/// gave to a task, anonymised under labels, and ranks them. The consensual strategy turns the
/// ballots into <see cref="Vote"/>s and tallies them with the configured
/// <see cref="IVotingStrategy"/>.
/// </summary>
/// <remarks>
/// The candidates a request carries never include the voter's own answer (an agent does not
/// vote for itself) nor an answer whose execution failed. An implementation that cannot read
/// the voter's reply returns an abstention; it never throws for an unreadable ballot.
/// </remarks>
public interface IBallotCollector
{
    /// <summary>Asks <see cref="BallotRequest.Voter"/> to rank <see cref="BallotRequest.Candidates"/>.</summary>
    /// <param name="request">The voter, the task, the anonymised candidates and the execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ballot: a ranking of candidate labels, or an abstention.</returns>
    System.Threading.Tasks.Task<Ballot> CollectAsync(BallotRequest request, CancellationToken cancellationToken = default);
}

/// <summary>One anonymised answer put to the vote.</summary>
public sealed record BallotCandidate
{
    /// <summary>The label the voter sees instead of the author (<c>A</c>, <c>B</c>, …).</summary>
    public required string Label { get; init; }

    /// <summary>The answer the author's execution produced.</summary>
    public required string Output { get; init; }
}

/// <summary>What a voter is asked to rank.</summary>
public sealed record BallotRequest
{
    /// <summary>The agent casting the ballot.</summary>
    public required Domain.Agent.Agent Voter { get; init; }

    /// <summary>The task the candidates answered.</summary>
    public required Domain.Task.CrewTask Task { get; init; }

    /// <summary>The anonymised answers, in the order the voter sees them.</summary>
    public required ImmutableList<BallotCandidate> Candidates { get; init; }

    /// <summary>The execution context of the ballot: the crew id, its input variables, the previous outputs.</summary>
    public required SimpleExecutionContext Context { get; init; }
}

/// <summary>A voter's ranking of the candidates, or its abstention.</summary>
public sealed record Ballot
{
    /// <summary>Candidate labels, best first. Empty for an abstention.</summary>
    public ImmutableList<string> Ranking { get; init; } = [];

    /// <summary>How sure the voter is of its ranking, from 0 to 1.</summary>
    public float Confidence { get; init; } = 1f;

    /// <summary>The voter's one-line reason, or why the ballot counts as an abstention.</summary>
    public string? Justification { get; init; }

    /// <summary>The execution that produced the ballot, so its tokens are counted. Null when none ran.</summary>
    public TaskResult? Execution { get; init; }

    /// <summary>True when the ballot ranks no candidate: a refusal, or a reply that could not be read.</summary>
    public bool Abstained => Ranking.IsEmpty;

    /// <summary>An abstention, with the reason and the execution that produced it.</summary>
    /// <param name="reason">Why the voter abstained, or why its reply could not be read.</param>
    /// <param name="execution">The execution that produced the ballot, if one ran.</param>
    public static Ballot Abstention(string reason, TaskResult? execution = null) =>
        new() { Justification = reason, Execution = execution };
}
