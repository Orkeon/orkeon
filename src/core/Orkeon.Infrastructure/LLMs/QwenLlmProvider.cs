using Microsoft.Extensions.Logging;
using Polly;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// Qwen (Alibaba DashScope) LLM provider implementation.
/// Uses the OpenAI-compatible DashScope endpoint.
/// </summary>
public class QwenLlmProvider : OpenAICompatibleProviderBase
{
    /// <inheritdoc />
    public override string Name => "qwen";

    /// <inheritdoc />
    protected override Uri DefaultBaseUrl => new(LlmEndpoints.Qwen);

    /// <inheritdoc />
    protected override string DefaultModel => ProviderDefaults.QwenDefaults.DefaultModel;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "Qwen";

    /// <summary>Initializes a new instance of <see cref="QwenLlmProvider"/>.</summary>
    public QwenLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<QwenLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>Constructor overload that accepts an optional resilience policy for testing.</summary>
    public QwenLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<QwenLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Constructor overload that accepts a tool calling strategy.</summary>
    public QwenLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<QwenLlmProvider>? logger = null)
        : base(config, httpClientFactory, toolCallingStrategy, logger)
    {
    }
}
