using Microsoft.Extensions.Logging;
using Polly;
using System.Text.Json;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Base;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// HuggingFace LLM provider implementation.
/// Supports Serverless Inference API and dedicated Inference Endpoints.
/// Uses OpenAI-compatible chat completions format.
/// </summary>
public partial class HuggingFaceLlmProvider : OpenAICompatibleProviderBase
{
    /// <inheritdoc />
    public override string Name => "huggingface";

#pragma warning disable S1075
    /// <inheritdoc />
    protected override Uri DefaultBaseUrl => new("https://api-inference.huggingface.co/v1");
#pragma warning restore S1075

    /// <inheritdoc />
    protected override string DefaultModel => "meta-llama/Llama-3.1-8B-Instruct";

    /// <inheritdoc />
    protected override string ProviderDisplayName => "HuggingFace";

    /// <summary>Initializes a new instance of <see cref="HuggingFaceLlmProvider"/>.</summary>
    public HuggingFaceLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<HuggingFaceLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>Constructor overload that accepts an optional resilience policy for testing.</summary>
    public HuggingFaceLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<HuggingFaceLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Constructor overload that accepts a tool calling strategy.</summary>
    public HuggingFaceLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<HuggingFaceLlmProvider>? logger = null)
        : base(config, httpClientFactory, toolCallingStrategy, logger)
    {
    }

    /// <summary>
    /// Extracts HuggingFace-specific usage metadata from the response.
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
