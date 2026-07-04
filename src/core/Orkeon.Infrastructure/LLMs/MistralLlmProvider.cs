using Microsoft.Extensions.Logging;
using Polly;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// Mistral AI LLM provider implementation.
/// Targets the Mistral cloud API (OpenAI-compatible endpoint).
/// </summary>
public class MistralLlmProvider : OpenAICompatibleProviderBase
{
    /// <inheritdoc />
    public override string Name => "mistral";

    /// <inheritdoc />
    protected override Uri DefaultBaseUrl => new(LlmEndpoints.Mistral);

    /// <inheritdoc />
    protected override string DefaultModel => ProviderDefaults.MistralDefaults.DefaultModel;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "Mistral AI";

    /// <summary>Initializes a new instance of <see cref="MistralLlmProvider"/>.</summary>
    public MistralLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<MistralLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>Constructor overload that accepts an optional resilience policy for testing.</summary>
    public MistralLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<MistralLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Constructor overload that accepts a tool calling strategy.</summary>
    public MistralLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<MistralLlmProvider>? logger = null)
        : base(config, httpClientFactory, toolCallingStrategy, logger)
    {
    }
}
