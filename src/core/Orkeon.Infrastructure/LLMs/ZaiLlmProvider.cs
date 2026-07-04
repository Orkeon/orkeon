using Microsoft.Extensions.Logging;
using Polly;
using System.Text.Json;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// Z.AI (Zhipu GLM) LLM provider implementation.
/// OpenAI-compatible endpoint; supports the GLM-5.x thinking models, which return
/// <c>reasoning_content</c> alongside <c>content</c> and expose implicit context-cache
/// metrics via <c>usage.prompt_tokens_details.cached_tokens</c>.
/// Unlike DeepSeek, Z.AI does not require replaying <c>reasoning_content</c> on
/// subsequent assistant turns — each request is independent.
/// </summary>
public class ZaiLlmProvider : OpenAICompatibleProviderBase
{
    /// <inheritdoc />
    public override string Name => "zai";

    /// <inheritdoc />
    protected override Uri DefaultBaseUrl => new(LlmEndpoints.Zai);

    /// <inheritdoc />
    protected override string DefaultModel => ProviderDefaults.ZaiDefaults.DefaultModel;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "Z.AI";

    /// <summary>Initializes a new instance of <see cref="ZaiLlmProvider"/>.</summary>
    public ZaiLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<ZaiLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>Constructor overload that accepts an optional resilience policy for testing.</summary>
    public ZaiLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<ZaiLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Constructor overload that accepts a tool calling strategy.</summary>
    public ZaiLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<ZaiLlmProvider>? logger = null)
        : base(config, httpClientFactory, toolCallingStrategy, logger)
    {
    }

    /// <summary>
    /// Attaches the Z.AI <c>thinking</c> block and <c>reasoning_effort</c> hint when the
    /// effective config declares them. Same wire shape as DeepSeek-V4: most GLM models
    /// auto-decide thinking; <c>reasoning_effort</c> (max/xhigh/high/medium/low/minimal/none)
    /// is honored by GLM-5.2+ only and ignored by older models.
    /// </summary>
    protected override void ApplyProviderSpecificOptions(Dictionary<string, object> payload, LlmConfig effectiveConfig)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(effectiveConfig);

        var thinking = effectiveConfig.Thinking;
        if (thinking is null) return;

        if (thinking.Enabled.HasValue)
        {
            payload["thinking"] = new Dictionary<string, object?>
            {
                ["type"] = thinking.Enabled.Value ? "enabled" : "disabled",
            };
        }

        if (!string.IsNullOrWhiteSpace(thinking.Effort))
        {
            payload["reasoning_effort"] = thinking.Effort;
        }
    }

    /// <summary>
    /// Extracts Z.AI-specific metadata: the <c>reasoning_content</c> thinking trace and the
    /// usage breakdown (<c>prompt_tokens_details.cached_tokens</c> for the implicit context
    /// cache, <c>completion_tokens_details.reasoning_tokens</c> for the thinking spend).
    /// The typed <see cref="LlmResponse.CacheHitTokens"/>/<see cref="LlmResponse.CacheMissTokens"/>
    /// fields are populated generically by the base parser from the same usage keys.
    /// </summary>
    protected override void ExtractResponseMetadata(JsonDocument doc, LlmResponseMetadata.Builder metadata)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(metadata);
        ExtractReasoningContent(doc.RootElement, metadata);
        ExtractUsageMetadata(doc.RootElement, metadata);
    }

    private static void ExtractReasoningContent(JsonElement root, LlmResponseMetadata.Builder metadata)
    {
        if (!root.TryGetProperty("choices", out var choices))
            return;

        var firstChoice = choices.EnumerateArray().FirstOrDefault();
        if (firstChoice.ValueKind == JsonValueKind.Undefined)
            return;

        if (!firstChoice.TryGetProperty("message", out var message))
            return;

        if (message.TryGetProperty("reasoning_content", out var reasoning)
            && reasoning.GetString() is { Length: > 0 } reasoningText)
        {
            metadata.Add("reasoning_content", reasoningText);
        }
    }

    private static void ExtractUsageMetadata(JsonElement root, LlmResponseMetadata.Builder metadata)
    {
        if (!root.TryGetProperty("usage", out var usage))
            return;

        if (usage.TryGetProperty("prompt_tokens", out var promptTokens))
            metadata.Add("prompt_tokens", promptTokens.GetInt32());
        if (usage.TryGetProperty("completion_tokens", out var completionTokens))
            metadata.Add("completion_tokens", completionTokens.GetInt32());

        // Implicit context cache reads — billed at a fraction of the standard input price.
        if (usage.TryGetProperty("prompt_tokens_details", out var promptDetails)
            && promptDetails.ValueKind == JsonValueKind.Object
            && promptDetails.TryGetProperty("cached_tokens", out var cachedTokens))
        {
            metadata.Add("cached_tokens", cachedTokens.GetInt32());
        }

        // Thinking spend — counts toward completion_tokens (and max_tokens), so callers
        // can monitor how much of the output budget the reasoning phase consumed.
        if (usage.TryGetProperty("completion_tokens_details", out var completionDetails)
            && completionDetails.ValueKind == JsonValueKind.Object
            && completionDetails.TryGetProperty("reasoning_tokens", out var reasoningTokens))
        {
            metadata.Add("reasoning_tokens", reasoningTokens.GetInt32());
        }
    }
}
