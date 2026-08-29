using Orkeon.Constants.Llm;
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
    protected override string DefaultModel => LlmProviderDefaultModels.Zai;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "Z.AI";

    /// <summary>
    /// GLM exposes an explicit thinking toggle plus <c>reasoning_effort</c> (honoured by
    /// GLM-5.2+, ignored by older models) and caches prompt prefixes implicitly — nothing to
    /// declare on the wire, unlike Anthropic's explicit breakpoints.
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonObject,
        Thinking = ThinkingSupport.Toggle,
        Vision = true,
    };

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
    /// Adds the Z.AI usage breakdown on top of the base reasoning trace:
    /// <c>prompt_tokens_details.cached_tokens</c> for the implicit context cache and
    /// <c>completion_tokens_details.reasoning_tokens</c> for the thinking spend. The typed
    /// <see cref="LlmResponse.CacheHitTokens"/>/<see cref="LlmResponse.CacheMissTokens"/>
    /// fields are populated generically by the base parser from the same usage keys.
    /// </summary>
    protected override void ExtractResponseMetadata(JsonDocument doc, LlmResponseMetadata.Builder metadata)
    {
        ArgumentNullException.ThrowIfNull(doc);
        base.ExtractResponseMetadata(doc, metadata);
        ExtractStandardUsageMetadata(doc.RootElement, metadata);
        ExtractUsageDetails(doc.RootElement, metadata);
    }

    private static void ExtractUsageDetails(JsonElement root, LlmResponseMetadata.Builder metadata)
    {
        if (!root.TryGetProperty("usage", out var usage))
            return;

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
