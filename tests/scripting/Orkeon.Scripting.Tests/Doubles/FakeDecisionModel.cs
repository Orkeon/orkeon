using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>
/// A decision model a test configures: <see cref="Respond"/> answers each request (null fails the call), and every
/// request it is asked is kept, so a test counts the calls and reads what the model was offered.
/// </summary>
internal sealed class FakeDecisionModel : IDecisionModel
{
    /// <summary>Builds the answer to a request; null makes the call fail.</summary>
    public Func<DecisionRequest, DecisionResponse>? Respond { get; set; }

    /// <summary>The requests asked so far, oldest first.</summary>
    public List<DecisionRequest> Requests { get; } = [];

    /// <summary>Run at each call before the token is read (a test cancels its run there).</summary>
    public Action? OnCall { get; set; }

    public string Name => "fake";

    public Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        OnCall?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        return Respond is { } respond
            ? Task.FromResult(respond(request))
            : throw new InvalidOperationException("the fake decision model has no answer");
    }
}
