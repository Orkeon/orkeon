using Orkeon.Application.Decision;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// An <see cref="IDecisionStage"/> that answers from <see cref="OnRequest"/> and
/// <see cref="OnResponse"/> (Continue by default) and counts its calls.
/// </summary>
public sealed class StubDecisionStage : IDecisionStage
{
    /// <summary>The verdict before the call.</summary>
    public Func<LlmMessage[], DecisionVerdict> OnRequest { get; set; } = _ => DecisionVerdict.Continue;

    /// <summary>The verdict after the call.</summary>
    public Func<LlmResponse, DecisionVerdict> OnResponse { get; set; } = _ => DecisionVerdict.Continue;

    /// <summary>How many times each phase ran.</summary>
    public int RequestCalls { get; private set; }

    /// <inheritdoc cref="RequestCalls" />
    public int ResponseCalls { get; private set; }

    /// <inheritdoc />
    public string Name { get; set; } = "stub";

    /// <inheritdoc />
    public Task<DecisionVerdict> OnRequestAsync(LlmMessage[] messages, CancellationToken cancellationToken = default)
    {
        RequestCalls++;
        return Task.FromResult(OnRequest(messages));
    }

    /// <inheritdoc />
    public Task<DecisionVerdict> OnResponseAsync(LlmMessage[] messages, LlmResponse response, CancellationToken cancellationToken = default)
    {
        ResponseCalls++;
        return Task.FromResult(OnResponse(response));
    }
}
