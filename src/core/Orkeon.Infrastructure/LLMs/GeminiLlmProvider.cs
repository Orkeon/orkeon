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
    protected override string DefaultModel => ProviderDefaults.GeminiDefaults.DefaultModel;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "Gemini";

    /// <summary>
    /// Capabilities verified against Google's OpenAI-compatibility documentation
    /// (2026-08-18): <c>reasoning_effort</c> is supported and maps to Gemini's
    /// <c>thinking_level</c> (effort-only — no toggle, no token budget); vision flows
    /// through <c>image_url</c> with base64 data URIs. <c>response_format</c> is NOT
    /// documented on the compatibility surface (structured outputs go through the
    /// vendor SDKs' parse helpers), so it stays undeclared: a caller requesting a JSON
    /// response format gets the structured capability warning instead of a silent drop.
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.None,
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
