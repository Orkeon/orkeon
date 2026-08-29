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
/// Groq LLM provider implementation.
/// Groq uses an OpenAI-compatible API format with additional timing metrics.
/// </summary>
public partial class GroqLlmProvider : OpenAICompatibleProviderBase
{
    /// <inheritdoc />
    public override string Name => "groq";

    /// <inheritdoc />
    protected override Uri DefaultBaseUrl => new(LlmEndpoints.Groq);

    /// <inheritdoc />
    protected override string DefaultModel => LlmProviderDefaultModels.Groq;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "Groq";

    /// <summary>
    /// Groq serves open-weight models behind the OpenAI dialect: structured outputs with a
    /// schema, <c>reasoning_effort</c> on the reasoning models (GPT-OSS, Qwen), and vision on
    /// its VLM models.
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonSchema,
        Thinking = ThinkingSupport.EffortOnly,
        Vision = true,
    };

    /// <summary>Initializes a new instance of <see cref="GroqLlmProvider"/>.</summary>
    public GroqLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<GroqLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>Constructor overload that accepts an optional resilience policy for testing.</summary>
    public GroqLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<GroqLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Constructor overload that accepts a tool calling strategy.</summary>
    public GroqLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<GroqLlmProvider>? logger = null)
        : base(config, httpClientFactory, toolCallingStrategy, logger)
    {
    }

    /// <summary>
    /// Extracts Groq-specific timing metadata from the usage section.
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
        if (usage.TryGetProperty("queue_time", out var queueTime))
            metadata.Add("queue_time", queueTime.GetDouble());
        if (usage.TryGetProperty("prompt_time", out var promptTime))
            metadata.Add("prompt_time", promptTime.GetDouble());
        if (usage.TryGetProperty("completion_time", out var completionTime))
            metadata.Add("completion_time", completionTime.GetDouble());
        if (usage.TryGetProperty("total_time", out var totalTime))
            metadata.Add("total_time", totalTime.GetDouble());
    }
}
