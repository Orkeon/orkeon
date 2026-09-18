using Orkeon.Constants.Llm;
using Microsoft.Extensions.Logging;
using Polly;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// Mammouth AI LLM provider implementation — the French multi-model subscription whose
/// included API credits drive Orkeon, over its OpenAI-compatible endpoint
/// (<c>api.mammouth.ai/v1</c>, Bearer auth). Identifiers are the vendors' own bare strings
/// (<c>gpt-5.6-sol</c>, <c>claude-sonnet-5</c>, <c>gemini-3.7-flash</c>), which is why this
/// provider is reached by base URL or by <c>"Provider": "mammouth"</c> and never inferred
/// from a model name (D-02): the same string without a base URL keeps going to the vendor.
/// </summary>
/// <remarks>
/// <para>
/// Documentation-backed, not campaigned yet (LLM-09 §6). The quick-start documents
/// <c>messages</c>, <c>model</c>, <c>temperature</c> (0–2), <c>max_tokens</c>, <c>top_p</c>,
/// <c>stream</c> and the three roles — nothing else. Three concordant clues (the LiteLLM
/// error shape, the <c>0.0.0.0:4000</c> left in the quick-start, the OpenClaw page naming
/// "LiteLLM as the provider") say the server is a LiteLLM proxy on 2026-09-18, so what is
/// undocumented is probably passed through to the upstream — probably, not certainly.
/// Nothing here depends on it: no proxy-specific parsing, only the dated remark.
/// </para>
/// <para>
/// Treated as MiniMax was on 2026-08-30: what the documentation does not say is not
/// declared, the caller gets the structured warning, and the first campaign raises what
/// works — <c>tools</c> (M5), <c>response_format</c> (M7/M8), <c>reasoning_effort</c> and
/// the field the trace comes back in (LiteLLM often normalises to
/// <c>reasoning_content</c>, in which case <c>EffortOnly</c> is one declaration away), the
/// cache relay (M10), the 429 shape (M12) and the key format. No behaviour override.
/// </para>
/// </remarks>
public class MammouthLlmProvider : OpenAICompatibleProviderBase
{
    /// <inheritdoc />
    public override string Name => "mammouth";

    /// <inheritdoc />
    protected override Uri DefaultBaseUrl => new(LlmEndpoints.Mammouth);

    /// <inheritdoc />
    protected override string DefaultModel => LlmProviderDefaultModels.Mammouth;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "Mammouth";

    /// <summary>
    /// Declared from the vendor documentation on 2026-09-18 (D-04): <c>response_format</c>
    /// and thinking controls are undocumented, so they stay None until measured — the
    /// warning is the promise. Vision is declared because the vendor's OpenClaw page lists
    /// <c>text, image</c> input for several models; per provider here, per model in reality
    /// (D-03 of the test matrix).
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.None,
        Thinking = ThinkingSupport.None,
        Vision = true,
    };

    /// <summary>Initializes a new instance of <see cref="MammouthLlmProvider"/>.</summary>
    public MammouthLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<MammouthLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>Constructor overload that accepts an optional resilience policy for testing.</summary>
    public MammouthLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<MammouthLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Constructor overload that accepts a tool calling strategy.</summary>
    public MammouthLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<MammouthLlmProvider>? logger = null)
        : base(config, httpClientFactory, toolCallingStrategy, logger)
    {
    }
}
