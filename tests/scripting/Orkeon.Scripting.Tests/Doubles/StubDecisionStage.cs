using Orkeon.Application.Decision;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>
/// An <see cref="IDecisionStage"/> that answers every request with <see cref="Verdict"/> (Continue by
/// default) and counts the requests it judged.
/// </summary>
public sealed class StubDecisionStage : IDecisionStage
{
    /// <summary>The verdict before each call.</summary>
    public DecisionVerdict Verdict { get; set; } = DecisionVerdict.Continue;

    /// <summary>How many requests the stage judged.</summary>
    public int RequestCalls { get; private set; }

    /// <inheritdoc />
    public string Name { get; set; } = "stub";

    /// <inheritdoc />
    public Task<DecisionVerdict> OnRequestAsync(LlmMessage[] messages, CancellationToken cancellationToken = default)
    {
        RequestCalls++;
        return Task.FromResult(Verdict);
    }

    /// <inheritdoc />
    public Task<DecisionVerdict> OnResponseAsync(LlmMessage[] messages, LlmResponse response, CancellationToken cancellationToken = default)
        => Task.FromResult(DecisionVerdict.Continue);
}
