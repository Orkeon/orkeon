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
