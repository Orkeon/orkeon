namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Stable keys used under <c>Microsoft.Extensions.AI.UsageDetails.AdditionalCounts</c>
/// to carry provider-specific usage counts that don't fit the standard
/// InputTokenCount / OutputTokenCount / TotalTokenCount triple.
///
/// Centralised here so both the Infrastructure-side adapter (writer) and the
/// Application-side orchestrator (reader) reference the same constants without
/// taking a layering dependency on each other.
/// </summary>
public static class LlmUsageMetadataKeys
{
    /// <summary>Prompt tokens that hit the provider's prompt cache (DeepSeek <c>prompt_cache_hit_tokens</c>).</summary>
    public const string CacheHitTokens = "prompt_cache_hit_tokens";

    /// <summary>Prompt tokens that missed the provider's prompt cache (DeepSeek <c>prompt_cache_miss_tokens</c>).</summary>
    public const string CacheMissTokens = "prompt_cache_miss_tokens";
}
