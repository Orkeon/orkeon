using System.Collections.Immutable;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>
/// An <see cref="IDecisionModel"/> that answers every question of a pruning request with <see cref="Score"/>
/// (0.1 drops every result) and keeps the requests it receives. <see cref="OnDecide"/> runs before it answers.
/// </summary>
public sealed class StubPruneDecisionModel : IDecisionModel
{
    private readonly Lock _gate = new();
    private readonly List<DecisionRequest> _requests = [];

    /// <summary>The score of every answer: below one half, a result is not needed and is dropped.</summary>
    public double Score { get; set; } = 0.1;

    /// <summary>Run with the number of the request (1 for the first) before it answers.</summary>
    public Action<int>? OnDecide { get; set; }

    /// <summary>The requests received so far, oldest first.</summary>
    public IReadOnlyList<DecisionRequest> Requests
    {
        get { lock (_gate) return [.. _requests]; }
    }

    /// <inheritdoc />
    public string Name => "stub-prune";

    /// <inheritdoc />
    public Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        int count;
        lock (_gate)
        {
            _requests.Add(request);
            count = _requests.Count;
        }
        OnDecide?.Invoke(count);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DecisionResponse
        {
            Model = Name,
            Answers = request.Questions.Keys.ToImmutableDictionary(k => k, _ => (DecisionAnswer)new NoulAnswer { Noul = Score }),
        });
    }
}
