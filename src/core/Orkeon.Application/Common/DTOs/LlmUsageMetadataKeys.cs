namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Stable keys used under <c>Microsoft.Extensions.AI.UsageDetails.AdditionalCounts</c>
/// to carry provider-specific usage counts that don't fit the standard
/// InputTokenCount / OutputTokenCount / TotalTokenCount triple — and, for the vendor's
/// charge, under the provider's <c>LlmResponse.Metadata</c> and the
/// <c>ChatResponse.AdditionalProperties</c> it is carried onto.
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

    /// <summary>
    /// What the vendor billed for the call, from its own <c>usage.cost</c> (OpenRouter): a
    /// <see cref="double"/> in the provider's metadata, a <see cref="decimal"/> once the chat
    /// client adapter has carried it onto the <c>ChatResponse</c>. Absent when the vendor
    /// bills nothing in its answer; a present 0 is a free call.
    /// </summary>
    public const string Cost = "cost";

    /// <summary>
    /// The ISO 4217 code of <see cref="Cost"/>, written beside it by a provider that knows the
    /// currency its vendor bills in (<c>USD</c> for OpenRouter); absent otherwise.
    /// </summary>
    public const string CostCurrency = "cost_currency";
}
