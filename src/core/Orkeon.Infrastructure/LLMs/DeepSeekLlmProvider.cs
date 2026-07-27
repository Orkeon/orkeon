using Microsoft.Extensions.Logging;
using Polly;
using System.Text.Json;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// DeepSeek LLM provider implementation.
/// Supports the V4 generation (thinking and non-thinking modes, <c>reasoning_content</c>).
/// </summary>
public class DeepSeekLlmProvider : OpenAICompatibleProviderBase
{
    /// <inheritdoc />
    public override string Name => "deepseek";

    /// <inheritdoc />
    protected override Uri DefaultBaseUrl => new(LlmEndpoints.DeepSeek);

    /// <inheritdoc />
    protected override string DefaultModel => ProviderDefaults.DeepSeekDefaults.DefaultModel;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "DeepSeek";

    /// <summary>
    /// DeepSeek accepts <c>response_format: json_object</c> (no schema), exposes an explicit
    /// thinking toggle plus <c>reasoning_effort</c>, requires the word "json" in the prompt
    /// when constraining JSON, and — uniquely among the reasoning providers — requires the
    /// previous turn's <c>reasoning_content</c> to be replayed.
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonObject,
        Thinking = ThinkingSupport.Toggle,
        RequiresJsonKeywordInPrompt = true,
        ReplaysReasoningContent = true,
    };

    /// <summary>Initializes a new instance of <see cref="DeepSeekLlmProvider"/>.</summary>
    public DeepSeekLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<DeepSeekLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>Constructor overload that accepts an optional resilience policy for testing.</summary>
    public DeepSeekLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<DeepSeekLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Constructor overload that accepts a tool calling strategy.</summary>
    public DeepSeekLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<DeepSeekLlmProvider>? logger = null)
        : base(config, httpClientFactory, toolCallingStrategy, logger)
    {
    }

    /// <summary>
    /// Adds the DeepSeek usage breakdown on top of the base reasoning trace: the context
    /// cache is auto-populated by the API when a prompt prefix matches a recent request and
    /// is billed at a tenth of the input price, so both sides are surfaced.
    /// </summary>
    protected override void ExtractResponseMetadata(JsonDocument doc, LlmResponseMetadata.Builder metadata)
    {
        ArgumentNullException.ThrowIfNull(doc);
        base.ExtractResponseMetadata(doc, metadata);
        ExtractStandardUsageMetadata(doc.RootElement, metadata);
        ExtractCacheMetadata(doc.RootElement, metadata);
    }

    private static void ExtractCacheMetadata(JsonElement root, LlmResponseMetadata.Builder metadata)
    {
        if (!root.TryGetProperty("usage", out var usage))
            return;

        if (usage.TryGetProperty("prompt_cache_hit_tokens", out var cacheHit))
            metadata.Add("prompt_cache_hit_tokens", cacheHit.GetInt32());
        if (usage.TryGetProperty("prompt_cache_miss_tokens", out var cacheMiss))
            metadata.Add("prompt_cache_miss_tokens", cacheMiss.GetInt32());
    }
}
