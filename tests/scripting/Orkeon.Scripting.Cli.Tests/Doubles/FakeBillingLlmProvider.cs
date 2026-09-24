using Orkeon.Application.Common.DTOs;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="ILlmProvider"/> for a vendor that bills in its answers, the way
/// OpenRouter does: every call answers <see cref="Answer"/> with its usage split and, when
/// <see cref="ChargePerCall"/> is set, the charge and its currency under the metadata keys a
/// real provider writes them to. Counts the calls it answered.
/// </summary>
internal sealed class FakeBillingLlmProvider : ILlmProvider
{
    private int _calls;

    /// <inheritdoc />
    public string Name => "fake-billing";

    /// <summary>The text of every answer.</summary>
    public string Answer { get; init; } = "done";

    /// <summary>Prompt tokens reported per call.</summary>
    public int PromptTokens { get; init; } = 120;

    /// <summary>Completion tokens reported per call.</summary>
    public int CompletionTokens { get; init; } = 30;

    /// <summary>What the vendor bills per call, as the providers store it; null bills nothing.</summary>
    public double? ChargePerCall { get; init; }

    /// <summary>The currency the charge is stated in.</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>How many calls were answered.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        => Task.FromResult(Respond());

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
        => Task.FromResult(Respond());

    private LlmResponse Respond()
    {
        Interlocked.Increment(ref _calls);

        var metadata = new Dictionary<string, object> { ["provider"] = Name };
        if (ChargePerCall is { } charge)
        {
            metadata[LlmUsageMetadataKeys.Cost] = charge;
            metadata[LlmUsageMetadataKeys.CostCurrency] = Currency;
        }

        return new LlmResponse
        {
            Content = Answer,
            TokensUsed = PromptTokens + CompletionTokens,
            PromptTokens = PromptTokens,
            CompletionTokens = CompletionTokens,
            Model = "vendor/model-x",
            Metadata = metadata,
        };
    }
}
