using System.Collections.Immutable;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>An <see cref="IDecisionModel"/> that answers every question with 0.1 (a pruned result is dropped), and counts the requests.</summary>
internal sealed class StubPruneDecisionModel : IDecisionModel
{
    private int _requests;

    /// <summary>How many requests were received.</summary>
    public int Requests => Volatile.Read(ref _requests);

    /// <inheritdoc />
    public string Name => "stub-prune";

    /// <inheritdoc />
    public Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _requests);
        return Task.FromResult(new DecisionResponse
        {
            Model = Name,
            Answers = request.Questions.Keys.ToImmutableDictionary(k => k, _ => (DecisionAnswer)new NoulAnswer { Noul = 0.1 }),
        });
    }
}
