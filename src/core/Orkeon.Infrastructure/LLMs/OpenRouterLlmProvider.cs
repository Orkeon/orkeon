using Orkeon.Constants.Llm;
using Microsoft.Extensions.Logging;
using Polly;
using System.Text.Json;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// OpenRouter LLM provider implementation — the model marketplace (445 models from 60
/// vendors in the public catalog on 2026-09-18) behind one key, over its OpenAI-compatible
/// endpoint (<c>openrouter.ai/api/v1</c>, Bearer auth). Identifiers are
/// <c>vendor/model</c> (<c>anthropic/claude-sonnet-5</c>), with the routing suffixes
/// <c>:free</c>, <c>:nitro</c> (throughput) and <c>:floor</c> (price) and the router slugs
/// <c>openrouter/auto</c> / <c>openrouter/free</c>.
/// </summary>
/// <remarks>
/// <para>
/// Documentation-backed, not campaigned yet (LLM-09 §6): the first live campaign settles the
/// reasoning field on a reasoning model, <c>usage.cost</c> on both paths, whether a
/// <c>json_schema</c> is honoured without <c>provider.require_parameters</c>, and the
/// <c>sk-or-v1-</c> key prefix — the key inference waits for that confirmation.
/// </para>
/// <para>
/// What OpenRouter reads and writes differently from the OpenAI dialect lives here and
/// nowhere else: the reasoning trace comes back as <c>reasoning</c> — never
/// <c>reasoning_content</c> (<see cref="ReasoningFieldName"/>); the thinking controls travel
/// in a <c>reasoning</c> request object (<see cref="ApplyProviderSpecificOptions"/>); the real
/// charge arrives in <c>usage.cost</c> (read by the base) with its breakdown
/// (<see cref="ExtractResponseMetadata"/>); and two attribution headers name Orkeon in the
/// public app ranking (<see cref="ConfigureHttpClient"/>). Keep-alive comment lines
/// (<c>: OPENROUTER PROCESSING</c>) and a mid-stream <c>error</c> chunk are handled by the
/// base SSE reader and parsers. The advanced routing body (<c>provider {…}</c>,
/// <c>models[]</c>, <c>plugins[]</c>) is deliberately not exposed (D-09).
/// </para>
/// </remarks>
public class OpenRouterLlmProvider : OpenAICompatibleProviderBase
{
    /// <inheritdoc />
    public override string Name => "openrouter";

    /// <inheritdoc />
    protected override Uri DefaultBaseUrl => new(LlmEndpoints.OpenRouter);

    /// <inheritdoc />
    protected override string DefaultModel => LlmProviderDefaultModels.OpenRouter;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "OpenRouter";

    /// <summary>
    /// Declared from the vendor documentation on 2026-09-18 (D-04). <c>response_format</c>
    /// is <c>json_schema</c>-capable, per endpoint — OpenRouter routes to endpoints that
    /// support it only when <c>provider.require_parameters</c> is set, which this provider
    /// does not send, so whether a schema is silently ignored elsewhere is a campaign
    /// question (§6.4). Thinking is <c>Budget</c> through the <c>reasoning</c> object
    /// (<c>enabled</c>, <c>effort</c>, <c>max_tokens</c>) — a declaration of the TRANSPORT,
    /// not of a model: OpenRouter converts a budget into an effort level on effort-only
    /// models and the reverse on budget-only ones (§9). Vision is per model behind a
    /// per-provider declaration (D-03 of the test matrix). The trace is not replayed and no
    /// explicit cache breakpoint exists on this dialect.
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonSchema,
        Thinking = ThinkingSupport.Budget,
        Vision = true,
    };

    /// <summary>
    /// OpenRouter writes the trace in <c>choices[].message.reasoning</c> (buffered) and
    /// <c>choices[].delta.reasoning</c> (streamed) — never <c>reasoning_content</c>. The
    /// structured <c>reasoning_details[]</c> is not read (D-05): the string is what
    /// <c>LlmStreamEvent.Reasoning</c> and the <c>reasoning_content</c> metadata carry.
    /// </summary>
    protected override string ReasoningFieldName => "reasoning";

    /// <summary>
    /// OpenRouter bills in credits, and one credit is one US dollar: the charge in
    /// <c>usage.cost</c> is a dollar amount, and this provider says so rather than leaving
    /// every reader to guess (STUDIO-29).
    /// </summary>
    protected override string? CostCurrency => "USD";

    /// <summary>
    /// Adds the two attribution headers OpenRouter documents for its public app ranking
    /// (D-06). Constant and always emitted: neither a secret nor a preference. The referer
    /// header is spelled <c>HTTP-Referer</c> as the vendor writes it — the framework's
    /// <c>Referrer</c> property would emit <c>Referer</c>, which OpenRouter does not read.
    /// </summary>
    protected override void ConfigureHttpClient(HttpClient client, LlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(client);
        base.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.Remove(HttpDefaults.OpenRouterRefererHeader);
        client.DefaultRequestHeaders.TryAddWithoutValidation(HttpDefaults.OpenRouterRefererHeader, HttpDefaults.OpenRouterReferer);
        client.DefaultRequestHeaders.Remove(HttpDefaults.OpenRouterTitleHeader);
        client.DefaultRequestHeaders.TryAddWithoutValidation(HttpDefaults.OpenRouterTitleHeader, HttpDefaults.OpenRouterTitle);
    }

    /// <summary>
    /// Writes the thinking controls as OpenRouter's <c>reasoning</c> object —
    /// <c>{ enabled, effort, max_tokens }</c> — after letting the base write everything that
    /// <em>is</em> OpenAI-shaped (<c>response_format</c>). The base's <c>thinking</c> block
    /// (the DeepSeek / GLM dialect) is removed because OpenRouter does not read it, and its
    /// first-level <c>reasoning_effort</c> is removed once the object carries the same effort,
    /// so one intention is not sent twice — the Qwen pattern.
    /// </summary>
    protected override void ApplyProviderSpecificOptions(Dictionary<string, object> payload, LlmConfig effectiveConfig)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(effectiveConfig);

        base.ApplyProviderSpecificOptions(payload, effectiveConfig);

        payload.Remove("thinking");

        if (effectiveConfig.Thinking is not { } thinking)
            return;

        var reasoning = new Dictionary<string, object?>();
        if (thinking.Enabled is { } enabled)
            reasoning["enabled"] = enabled;
        if (!string.IsNullOrWhiteSpace(thinking.Effort))
            reasoning["effort"] = thinking.Effort;
        if (thinking.BudgetTokens is { } budget)
            reasoning["max_tokens"] = budget;

        if (reasoning.Count == 0)
            return;

        payload.Remove("reasoning_effort");
        payload["reasoning"] = reasoning;
    }

    /// <summary>
    /// Adds the OpenRouter usage breakdown on top of the base reasoning trace and the
    /// generic <c>cost</c> (D-08): <c>cost_details.upstream_inference_cost</c> and
    /// <c>is_byok</c> (bring-your-own-key billing), <c>prompt_tokens_details.cache_write_tokens</c>
    /// (the read side lands in the typed cache fields generically) and
    /// <c>completion_tokens_details.reasoning_tokens</c> — the Z.AI pattern. The response's
    /// <c>model</c> is exposed as <c>served_model</c>: behind <c>openrouter/auto</c> it is
    /// the only record of who answered.
    /// </summary>
    protected override void ExtractResponseMetadata(JsonDocument doc, LlmResponseMetadata.Builder metadata)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(metadata);
        base.ExtractResponseMetadata(doc, metadata);
        ExtractStandardUsageMetadata(doc.RootElement, metadata);
        ExtractUsageDetails(doc.RootElement, metadata);

        if (doc.RootElement.TryGetProperty("model", out var model)
            && model.ValueKind == JsonValueKind.String
            && model.GetString() is { Length: > 0 } servedModel)
        {
            metadata.Add("served_model", servedModel);
        }
    }

    private static void ExtractUsageDetails(JsonElement root, LlmResponseMetadata.Builder metadata)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return;

        if (TryReadNested(usage, "cost_details", "upstream_inference_cost", JsonValueKind.Number, out var upstream))
            metadata.Add("upstream_inference_cost", upstream.GetDouble());

        if (usage.TryGetProperty("is_byok", out var byok) && byok.ValueKind is JsonValueKind.True or JsonValueKind.False)
            metadata.Add("is_byok", byok.GetBoolean());

        if (TryReadNested(usage, "prompt_tokens_details", "cache_write_tokens", JsonValueKind.Number, out var cacheWrite))
            metadata.Add("cache_write_tokens", cacheWrite.GetInt32());

        if (TryReadNested(usage, "completion_tokens_details", "reasoning_tokens", JsonValueKind.Number, out var reasoningTokens))
            metadata.Add("reasoning_tokens", reasoningTokens.GetInt32());
    }

    private static bool TryReadNested(
        JsonElement element, string objectName, string propertyName, JsonValueKind kind, out JsonElement value)
    {
        value = default;
        return element.TryGetProperty(objectName, out var nested)
            && nested.ValueKind == JsonValueKind.Object
            && nested.TryGetProperty(propertyName, out value)
            && value.ValueKind == kind;
    }

    /// <summary>Initializes a new instance of <see cref="OpenRouterLlmProvider"/>.</summary>
    public OpenRouterLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<OpenRouterLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>Constructor overload that accepts an optional resilience policy for testing.</summary>
    public OpenRouterLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<OpenRouterLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Constructor overload that accepts a tool calling strategy.</summary>
    public OpenRouterLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<OpenRouterLlmProvider>? logger = null)
        : base(config, httpClientFactory, toolCallingStrategy, logger)
    {
    }
}
