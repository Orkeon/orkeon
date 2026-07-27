using Microsoft.Extensions.Logging;
using Polly;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// Kimi (Moonshot AI) LLM provider implementation.
/// Specializes in long-context processing (128K to 2M tokens).
/// Uses the OpenAI-compatible Moonshot API endpoint.
/// </summary>
public class KimiLlmProvider : OpenAICompatibleProviderBase
{
    /// <inheritdoc />
    public override string Name => "kimi";

    /// <inheritdoc />
    protected override Uri DefaultBaseUrl => new(LlmEndpoints.Kimi);

    /// <inheritdoc />
    protected override string DefaultModel => ProviderDefaults.KimiDefaults.DefaultModel;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "Kimi";

    /// <summary>
    /// Moonshot's API is OpenAI-compatible. Thinking is switchable on K2.6 (and always on for
    /// K3), and the K2.6/K3 generation accepts image input. JSON mode guarantees well-formed
    /// output without validating a schema.
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonObject,
        Thinking = ThinkingSupport.Toggle,
        Vision = true,
    };

    /// <summary>Initializes a new instance of <see cref="KimiLlmProvider"/>.</summary>
    public KimiLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<KimiLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>Constructor overload that accepts an optional resilience policy for testing.</summary>
    public KimiLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<KimiLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Constructor overload that accepts a tool calling strategy.</summary>
    public KimiLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<KimiLlmProvider>? logger = null)
        : base(config, httpClientFactory, toolCallingStrategy, logger)
    {
    }
}
