using Orkeon.Constants.Llm;
using Microsoft.Extensions.Logging;
using Polly;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// x.AI (Grok) LLM provider implementation.
/// Uses x.AI's OpenAI-compatible endpoint (<c>api.x.ai/v1</c>, Bearer auth).
/// </summary>
/// <remarks>
/// The provider was preceded by its own proof: on 2026-08-30, before this class existed, a
/// full 12-mode campaign against <c>api.x.ai</c> passed through the generic OpenAI dialect
/// with nothing but a base-url override (archived under
/// <c>llmproviders-test/custom-endpoints/</c>). Every capability declared below is a
/// measurement from that campaign, not a reading of documentation.
/// </remarks>
public class GrokLlmProvider : OpenAICompatibleProviderBase
{
    /// <inheritdoc />
    public override string Name => "grok";

    /// <inheritdoc />
    protected override Uri DefaultBaseUrl => new(LlmEndpoints.Grok);

    /// <inheritdoc />
    protected override string DefaultModel => LlmProviderDefaultModels.Grok;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "Grok";

    /// <summary>
    /// Measured live on <c>grok-4.6</c> (2026-08-30): <c>json_schema</c> accepted and
    /// honoured (M8), <c>reasoning_effort</c> accepted with the trace replayed in the
    /// response (M7 — effort hint only, no documented toggle or budget), images read through
    /// <c>image_url</c> parts (M9). The implicit prompt cache reports the OpenAI-standard
    /// <c>prompt_tokens_details.cached_tokens</c> (M10), which the base reads generically —
    /// nothing to declare.
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonSchema,
        Thinking = ThinkingSupport.EffortOnly,
        Vision = true,
    };

    /// <summary>Initializes a new instance of <see cref="GrokLlmProvider"/>.</summary>
    public GrokLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<GrokLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>Constructor overload that accepts an optional resilience policy for testing.</summary>
    public GrokLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<GrokLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Constructor overload that accepts a tool calling strategy.</summary>
    public GrokLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<GrokLlmProvider>? logger = null)
        : base(config, httpClientFactory, toolCallingStrategy, logger)
    {
    }
}
