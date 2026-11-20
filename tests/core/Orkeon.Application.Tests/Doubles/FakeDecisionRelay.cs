using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// An <see cref="IDecisionRelay"/> that answers the texts of <see cref="Answers"/> in turn (the last one
/// repeats) and keeps every request it receives. <see cref="Failure"/>, when set, is thrown instead.
/// </summary>
public sealed class FakeDecisionRelay : IDecisionRelay
{
    private readonly Lock _gate = new();
    private readonly List<DecisionRelayRequest> _requests = [];

    /// <summary>The texts answered, one per call; the last one repeats.</summary>
    public IReadOnlyList<string> Answers { get; set; } = ["{}"];

    /// <summary>The prompt tokens each call reports.</summary>
    public int InputTokens { get; set; }

    /// <summary>The completion tokens each call reports.</summary>
    public int OutputTokens { get; set; }

    /// <summary>Thrown by <see cref="AskAsync"/> when set.</summary>
    public Exception? Failure { get; set; }

    /// <summary>The requests received so far, oldest first.</summary>
    public IReadOnlyList<DecisionRelayRequest> Requests
    {
        get { lock (_gate) return [.. _requests]; }
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DecisionRelayResponse> AskAsync(DecisionRelayRequest request, CancellationToken cancellationToken = default)
    {
        int call;
        lock (_gate)
        {
            _requests.Add(request);
            call = _requests.Count;
        }

        if (Failure is not null)
            return System.Threading.Tasks.Task.FromException<DecisionRelayResponse>(Failure);
        var text = Answers[Math.Min(call, Answers.Count) - 1];
        return System.Threading.Tasks.Task.FromResult(new DecisionRelayResponse { Text = text, InputTokens = InputTokens, OutputTokens = OutputTokens });
    }
}
