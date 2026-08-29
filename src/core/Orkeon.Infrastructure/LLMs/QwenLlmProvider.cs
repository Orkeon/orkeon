using Orkeon.Constants.Llm;
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
    protected override string DefaultModel => LlmProviderDefaultModels.Qwen;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "Qwen";

    /// <summary>
    /// Qwen is a mixed case: OpenAI-compatible for transport and for <c>response_format</c>,
    /// but its thinking controls are DashScope's own — hence the
    /// <see cref="ApplyProviderSpecificOptions"/> override below. It is the only provider that
    /// accepts an explicit reasoning token budget. Vision lives on the VL / omni models.
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonObject,
        Thinking = ThinkingSupport.Budget,
        Vision = true,
    };

    /// <summary>
    /// Writes the DashScope thinking controls — <c>enable_thinking</c> and
    /// <c>thinking_budget</c> — instead of the OpenAI <c>thinking</c> block, then defers to
    /// the base for everything that <em>is</em> OpenAI-shaped (<c>response_format</c>,
    /// <c>reasoning_effort</c>).
    /// </summary>
    protected override void ApplyProviderSpecificOptions(Dictionary<string, object> payload, LlmConfig effectiveConfig)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(effectiveConfig);

        base.ApplyProviderSpecificOptions(payload, effectiveConfig);

        // The base wrote the OpenAI-dialect block; DashScope does not read it.
        payload.Remove("thinking");

        if (effectiveConfig.Thinking is not { } thinking)
            return;

        if (thinking.Enabled.HasValue)
            payload["enable_thinking"] = thinking.Enabled.Value;

        if (thinking.BudgetTokens is { } budget)
            payload["thinking_budget"] = budget;
    }

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
