using System.Text.Json.Serialization;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Token usage of one operation — an LLM call, a task, a crew run: prompt, completion and total tokens,
/// with the model, provider and estimated cost when known. The cache pair is a partition of
/// <see cref="PromptTokens"/> — tokens served from the provider's prompt cache versus computed — never an
/// addition to the totals; <see langword="null"/> means "not measured" (a provider without cache
/// telemetry), never a fabricated zero (W-08).
/// </summary>
public sealed record TokenUsage : ITokenUsage
{
    /// <summary>
    /// Gets the number of tokens in the prompt.
    /// </summary>
    public int PromptTokens { get; init; }

    /// <summary>
    /// Gets the number of tokens in the completion/response.
    /// </summary>
    public int CompletionTokens { get; init; }

    /// <summary>
    /// Gets the total number of tokens used (prompt + completion).
    /// </summary>
    public int TotalTokens { get; init; }

    /// <summary>
    /// Gets the estimated cost for this token usage.
    /// </summary>
    public decimal? EstimatedCost { get; init; }

    /// <summary>
    /// Gets the model used for this operation.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Gets the provider used (e.g., OpenAI, Anthropic).
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>Prompt tokens served from the provider's cache; null when unmeasured.</summary>
    public long? CacheHitTokens { get; init; }

    /// <summary>Prompt tokens the provider had to compute; null when unmeasured.</summary>
    public long? CacheMissTokens { get; init; }

    /// <summary>Cache hits over measured prompt tokens, in [0,1]; null when unmeasured.</summary>
    public double? CacheHitRatio =>
        CacheHitTokens is { } hit && CacheMissTokens is { } miss && hit + miss > 0
            ? (double)hit / (hit + miss)
            : null;
}
