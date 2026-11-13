using Orkeon.Constants.Llm;
using Microsoft.Extensions.Logging;
using Polly;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;
using Orkeon.Infrastructure.LLMs.Capabilities;

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
    /// <see cref="WriteReasoningFields"/> override below. It is the only provider that
    /// accepts an explicit reasoning token budget. Vision lives on the VL / omni models.
    /// </summary>
    protected override LlmProviderCapabilities DeclaredCapabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonObject,
        Thinking = ThinkingSupport.Budget,
        // Every level; Qwen 3.7 Plus refuses `max`, which its table entry says (campaign M18, 2026-11-13).
        EffortLevels = LlmThinkingConfig.Levels,
        Vision = true,
    };

    /// <summary>
    /// Writes the DashScope thinking controls — <c>enable_thinking</c> and
    /// <c>thinking_budget</c> — instead of the OpenAI <c>thinking</c> block, beside the
    /// OpenAI-shaped <c>reasoning_effort</c>.
    /// </summary>
    private protected override void WriteReasoningFields(Dictionary<string, object> payload, ReasoningVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(verdict);

        if (verdict.Toggle is { } enabled)
            payload["enable_thinking"] = enabled;
        if (verdict.Effort is { } effort)
            payload["reasoning_effort"] = effort;
        if (verdict.BudgetTokens is { } budget)
            payload["thinking_budget"] = budget;
    }

    /// <summary>
    /// Kept for the 1.x public surface: the thinking fields are written by <see cref="WriteReasoningFields"/>
    /// from the judged verdict, so this override only runs the base options.
    /// </summary>
    protected override void ApplyProviderSpecificOptions(Dictionary<string, object> payload, LlmConfig effectiveConfig)
    {
        ArgumentNullException.ThrowIfNull(payload);
        base.ApplyProviderSpecificOptions(payload, effectiveConfig);
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
