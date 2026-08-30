using Orkeon.Constants.Llm;
using Microsoft.Extensions.Logging;
using Polly;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// MiniMax LLM provider implementation.
/// Uses MiniMax's OpenAI-compatible endpoint (<c>api.minimax.io/v1</c> international,
/// <c>api.minimaxi.com</c> mainland China — Bearer auth).
/// </summary>
/// <remarks>
/// Unlike the rest of the fleet, this provider is NOT yet backed by a campaign: every
/// declaration below follows the vendor's platform documentation as read on 2026-08-30,
/// and the first campaign is the pending proof. Two open questions the campaign must
/// settle are recorded on <see cref="Capabilities"/>.
/// </remarks>
public class MiniMaxLlmProvider : OpenAICompatibleProviderBase
{
    /// <inheritdoc />
    public override string Name => "minimax";

    /// <inheritdoc />
    protected override Uri DefaultBaseUrl => new(LlmEndpoints.MiniMax);

    /// <inheritdoc />
    protected override string DefaultModel => LlmProviderDefaultModels.MiniMax;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "MiniMax";

    /// <summary>
    /// From the vendor's documentation (2026-08-30), campaign pending. Vision is declared
    /// because the VL model family accepts <c>image_url</c> content parts — per provider
    /// here, per model in reality (D-03): the text flagship answers an image with the
    /// vendor's own error. <c>response_format</c> is undocumented on the compatibility
    /// surface, so it stays undeclared — a caller asking for JSON gets the capability
    /// warning, never a silent drop (the Gemini precedent: measurement upgraded that
    /// declaration later, and can here too). Thinking stays undeclared for the same
    /// reason. Open questions for the first campaign: whether the reasoning flagship
    /// replays its thinking trace DeepSeek-style (M5/M7 would 400 on the second turn),
    /// and whether the implicit cache reports a token breakdown (M10).
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.None,
        Thinking = ThinkingSupport.None,
        Vision = true,
    };

    /// <summary>Initializes a new instance of <see cref="MiniMaxLlmProvider"/>.</summary>
    public MiniMaxLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<MiniMaxLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>Constructor overload that accepts an optional resilience policy for testing.</summary>
    public MiniMaxLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<MiniMaxLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Constructor overload that accepts a tool calling strategy.</summary>
    public MiniMaxLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<MiniMaxLlmProvider>? logger = null)
        : base(config, httpClientFactory, toolCallingStrategy, logger)
    {
    }
}
