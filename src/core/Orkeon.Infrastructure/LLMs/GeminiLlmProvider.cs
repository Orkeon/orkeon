using Orkeon.Constants.Llm;
using Microsoft.Extensions.Logging;
using Polly;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// Google Gemini LLM provider implementation.
/// Uses the Gemini OpenAI-compatible endpoint
/// (<c>generativelanguage.googleapis.com/v1beta/openai</c>, Bearer auth).
/// </summary>
public class GeminiLlmProvider : OpenAICompatibleProviderBase
{
    /// <inheritdoc />
    public override string Name => "gemini";

    /// <inheritdoc />
    protected override Uri DefaultBaseUrl => new(LlmEndpoints.Gemini);

    /// <inheritdoc />
    protected override string DefaultModel => LlmProviderDefaultModels.Gemini;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "Gemini";

    /// <summary>
    /// Capabilities verified against Google's OpenAI-compatibility documentation
    /// (2026-08-18): <c>reasoning_effort</c> is supported and maps to Gemini's
    /// <c>thinking_level</c> (effort-only — no toggle, no token budget); vision flows
    /// through <c>image_url</c> with base64 data URIs. <c>response_format</c> was
    /// undocumented on the compatibility surface then and stayed undeclared; measured
    /// live on 2026-08-30, the surface accepts both <c>json_object</c> and
    /// <c>json_schema</c> and enforces the schema server-side
    /// (<c>additionalProperties</c> included), so the declaration follows the
    /// measurement — the warning it used to emit was refusing something that works.
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonSchema,
        Thinking = ThinkingSupport.EffortOnly,
        Vision = true,
    };

    /// <summary>Initializes a new instance of <see cref="GeminiLlmProvider"/>.</summary>
    public GeminiLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<GeminiLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>Constructor overload that accepts an optional resilience policy for testing.</summary>
    public GeminiLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<GeminiLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Constructor overload that accepts a tool calling strategy.</summary>
    public GeminiLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<GeminiLlmProvider>? logger = null)
        : base(config, httpClientFactory, toolCallingStrategy, logger)
    {
    }
}
