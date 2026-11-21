using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// An <see cref="IDecisionRelay"/> that answers <see cref="Answer"/> with <see cref="Tokens"/> each, counts the calls
/// and keeps the role of the agent each was asked through.
/// </summary>
public sealed class FakeDecisionRelay : IDecisionRelay
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _roles = new();
    private int _calls;

    /// <summary>The text answered on every call.</summary>
    public string Answer { get; init; } = "{}";

    /// <summary>The prompt and completion tokens each call reports.</summary>
    public int Tokens { get; init; }

    /// <summary>How many calls were received.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>The role of the agent of each call, oldest first.</summary>
    public IReadOnlyList<string> Roles => [.. _roles];

    /// <inheritdoc />
    public Task<DecisionRelayResponse> AskAsync(DecisionRelayRequest request, CancellationToken cancellationToken = default)
    {
        _roles.Enqueue(request.Agent.Role.Value);
        Interlocked.Increment(ref _calls);
        return Task.FromResult(new DecisionRelayResponse { Text = Answer, InputTokens = Tokens, OutputTokens = Tokens });
    }
}
