using Orkeon.Constants.Llm;
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
    protected override string DefaultModel => LlmProviderDefaultModels.Mistral;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "Mistral AI";

    /// <summary>
    /// Mistral validates greedy sampling against the explicit <c>top_p</c> field while its
    /// reasoning mode runs an internal default of its own, so the configured value must
    /// always reach the wire (see <see cref="Base.OpenAICompatibleProviderBase.AlwaysEmitTopP"/>).
    /// </summary>
    protected override bool AlwaysEmitTopP => true;

    /// <summary>
    /// Mistral's cloud API follows the OpenAI dialect: JSON mode with a custom structured
    /// output schema, <c>reasoning_effort</c> on the reasoning models, and vision on the
    /// multimodal ones.
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonSchema,
        Thinking = ThinkingSupport.EffortOnly,
        Vision = true,
    };

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
