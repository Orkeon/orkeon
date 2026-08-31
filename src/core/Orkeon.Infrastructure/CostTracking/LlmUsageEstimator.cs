using Microsoft.Extensions.Options;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.CostTracking;

/// <summary>
/// The fallback token count for a provider that reports none.
/// <para>
/// Several OpenAI-compatible endpoints answer without a <c>usage</c> block at all. Every
/// meter above them used to drop those calls on the floor — no usage event, no movement,
/// and a screen showing «0 tokens» through a whole session. Silence from the provider is
/// not evidence that nothing was spent, so the framework counts it itself.
/// </para>
/// <para>
/// The arithmetic is <see cref="EnhancedTokenCounter"/>'s, at its default settings — the
/// same approximation the container already registers as <c>ITokenCounter</c> (~3.5
/// characters per token, plus the per-message overhead a chat format pays). It is an
/// ESTIMATE, and every caller is expected to carry that fact forward: a figure a user
/// reads must be marked as approximate, and a figure a budget consumes is knowingly
/// biased high rather than missing.
/// </para>
/// </summary>
public static class LlmUsageEstimator
{
    private static readonly EnhancedTokenCounter Counter = new(Options.Create(new TokenCounterOptions()));

    /// <summary>
    /// Whether the provider said anything at all about what a call cost. «No usage» and
    /// «zero tokens» must never read the same way — only the first one is estimated.
    /// </summary>
    /// <param name="response">The provider's answer.</param>
    public static bool Reported(LlmResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.TokensUsed != 0 || response.PromptTokens is not null || response.CompletionTokens is not null;
    }

    /// <summary>The ascending side of a single-prompt call.</summary>
    /// <param name="prompt">What was sent; null or empty counts as nothing.</param>
    public static int Prompt(string? prompt) => prompt is { Length: > 0 } text ? Counter.CountTokens(text) : 0;

    /// <summary>
    /// The ascending side of a chat call, message overhead included — a conversation costs
    /// more than the concatenation of its texts.
    /// </summary>
    /// <param name="messages">The conversation sent; null or empty counts as nothing.</param>
    public static int Prompt(IReadOnlyList<LlmMessage>? messages) => messages is { Count: > 0 }
        ? Counter.CountTokensForMessages(messages.Select(m => (m.Role, m.Content)))
        : 0;

    /// <summary>The descending side: whatever the model actually sent back.</summary>
    /// <param name="response">The provider's answer.</param>
    public static int Completion(LlmResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return Counter.CountTokens(response.Content);
    }
}
