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
/// Together AI LLM provider implementation.
/// Together AI uses a 100% OpenAI-compatible API for open-source models.
/// </summary>
public partial class TogetherAiLlmProvider : OpenAICompatibleProviderBase
{
    /// <inheritdoc />
    public override string Name => "together";

    /// <inheritdoc />
    protected override Uri DefaultBaseUrl => new(LlmEndpoints.Together);

    /// <inheritdoc />
    protected override string DefaultModel => LlmProviderDefaultModels.Together;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "Together AI";

    /// <summary>
    /// Together AI is fully OpenAI-compatible: JSON mode with a schema, and vision on its VLM
    /// models. It exposes no cross-model reasoning switch.
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonSchema,
        Vision = true,
    };

    /// <summary>
    /// Together documents no per-model output cap: the context window is the bound, and by
    /// default a request whose prompt plus <c>max_tokens</c> exceed it is refused
    /// (<c>context_length_exceeded_behavior: "error"</c>). <c>"truncate"</c> clamps
    /// <c>max_tokens</c> to <c>window − prompt</c> instead — the prompt is never cut — which is
    /// what lets the catalogue name the window itself as a model's cap (LLM-10).
    /// </summary>
    protected override void ApplyProviderSpecificOptions(Dictionary<string, object> payload, LlmConfig effectiveConfig)
    {
        ArgumentNullException.ThrowIfNull(payload);
        base.ApplyProviderSpecificOptions(payload, effectiveConfig);
        payload["context_length_exceeded_behavior"] = "truncate";
    }

    /// <summary>Initializes a new instance of <see cref="TogetherAiLlmProvider"/>.</summary>
    public TogetherAiLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<TogetherAiLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>Constructor overload that accepts an optional resilience policy for testing.</summary>
    public TogetherAiLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<TogetherAiLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Constructor overload that accepts a tool calling strategy.</summary>
    public TogetherAiLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<TogetherAiLlmProvider>? logger = null)
        : base(config, httpClientFactory, toolCallingStrategy, logger)
    {
    }

    /// <summary>
    /// Extracts Together AI usage metadata from the response.
    /// </summary>
    protected override void ExtractResponseMetadata(JsonDocument doc, LlmResponseMetadata.Builder metadata)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(metadata);
        if (!doc.RootElement.TryGetProperty("usage", out var usage))
            return;

        if (usage.TryGetProperty("prompt_tokens", out var promptTokens))
            metadata.Add("prompt_tokens", promptTokens.GetInt32());
        if (usage.TryGetProperty("completion_tokens", out var completionTokens))
            metadata.Add("completion_tokens", completionTokens.GetInt32());
    }
}
