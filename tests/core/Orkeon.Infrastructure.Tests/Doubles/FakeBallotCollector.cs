using System.Collections.Concurrent;
using System.Collections.Immutable;
using Orkeon.Application.Interfaces;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IBallotCollector"/> (GAP-04): records every request and casts the
/// ballot <see cref="Vote"/> computes. By default a voter ranks the candidates in the order it
/// was shown them.
/// </summary>
public sealed class FakeBallotCollector : IBallotCollector
{
    private readonly ConcurrentQueue<BallotRequest> _requests = new();

    /// <summary>How a ballot is cast from its request.</summary>
    public Func<BallotRequest, Ballot> Vote { get; set; } = InShownOrder;

    /// <summary>Every request received, in arrival order.</summary>
    public IReadOnlyList<BallotRequest> Requests => [.. _requests];

    public System.Threading.Tasks.Task<Ballot> CollectAsync(BallotRequest request, CancellationToken cancellationToken = default)
    {
        _requests.Enqueue(request);
        return System.Threading.Tasks.Task.FromResult(Vote(request));
    }

    /// <summary>Ranks the candidates in the order they were shown.</summary>
    public static Ballot InShownOrder(BallotRequest request) =>
        new() { Ranking = request.Candidates.Select(c => c.Label).ToImmutableList() };

    /// <summary>Ranks the candidates by a score read from their answer, best first.</summary>
    public static Func<BallotRequest, Ballot> RankBy(Func<string, int> score) => request => new Ballot
    {
        Ranking = request.Candidates.OrderByDescending(c => score(c.Output)).Select(c => c.Label).ToImmutableList(),
    };

    /// <summary>Puts the candidate whose answer is <paramref name="output"/> first.</summary>
    public static Ballot Prefer(BallotRequest request, string output) => new()
    {
        Ranking = request.Candidates.OrderByDescending(c => c.Output == output).Select(c => c.Label).ToImmutableList(),
    };
}
