using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>An <see cref="IDecisionModel"/> that fails every request with <see cref="Fault"/>, and counts them.</summary>
internal sealed class StubFailingDecisionModel : IDecisionModel
{
    private int _requests;

    /// <summary>The message every request fails with (<see cref="InvalidOperationException"/>).</summary>
    public string Fault { get; init; } = "the decision model is down";

    /// <summary>How many requests were received.</summary>
    public int Requests => Volatile.Read(ref _requests);

    /// <inheritdoc />
    public string Name => "failing";

    /// <inheritdoc />
    public Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _requests);
        return Task.FromException<DecisionResponse>(new InvalidOperationException(Fault));
    }
}
