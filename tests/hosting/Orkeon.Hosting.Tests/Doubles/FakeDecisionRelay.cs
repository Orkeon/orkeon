using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>An <see cref="IDecisionRelay"/> that answers <see cref="Answer"/> with <see cref="Tokens"/> each, and counts the calls.</summary>
public sealed class FakeDecisionRelay : IDecisionRelay
{
    private int _calls;

    /// <summary>The text answered on every call.</summary>
    public string Answer { get; init; } = "{}";

    /// <summary>The prompt and completion tokens each call reports.</summary>
    public int Tokens { get; init; }

    /// <summary>How many calls were received.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <inheritdoc />
    public Task<DecisionRelayResponse> AskAsync(DecisionRelayRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(new DecisionRelayResponse { Text = Answer, InputTokens = Tokens, OutputTokens = Tokens });
    }
}
