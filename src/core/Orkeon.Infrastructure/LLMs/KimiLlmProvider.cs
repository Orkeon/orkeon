using Orkeon.Constants.Llm;
using Microsoft.Extensions.Logging;
using Polly;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;
using System.Text.RegularExpressions;
using System.Net;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// Kimi (Moonshot AI) LLM provider implementation.
/// Specializes in long-context processing (128K to 2M tokens).
/// Uses the OpenAI-compatible Moonshot API endpoint.
/// </summary>
public partial class KimiLlmProvider : OpenAICompatibleProviderBase
{
    /// <inheritdoc />
    public override string Name => "kimi";

    /// <inheritdoc />
    protected override Uri DefaultBaseUrl => new(LlmEndpoints.Kimi);

    /// <inheritdoc />
    protected override string DefaultModel => LlmProviderDefaultModels.Kimi;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "Kimi";

    /// <summary>
    /// Moonshot's API is OpenAI-compatible. Thinking is switchable on K2.6 and the K3 family, and
    /// always on for <c>kimi-k2.7-code</c> (a table entry), and the K2.6/K3 generation accepts image input. JSON mode guarantees well-formed
    /// output without validating a schema.
    /// </summary>
    protected override LlmProviderCapabilities DeclaredCapabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonObject,
        Thinking = ThinkingSupport.Toggle,
        // Every level, `xhigh` and `max` included (campaign M18, 2026-11-13); the table narrows per model.
        EffortLevels = LlmThinkingConfig.Levels,
        Vision = true,
    };

    /// <summary>
    /// Matches Moonshot's temperature constraint, e.g.
    /// <c>invalid temperature: only 1 is allowed for this model</c>. Which models mandate a
    /// fixed temperature is decided server-side and changes with their lineup, so the
    /// constraint is read from the API's own rejection instead of a model list that drifts.
    /// </summary>
    [GeneratedRegex(@"invalid temperature: only ([0-9]+(?:\.[0-9]+)?) is allowed",
        RegexOptions.CultureInvariant)]
    private static partial Regex TemperatureConstraint();

    /// <summary>
    /// Self-heals the one rejection Moonshot answers with a hard constraint: when the API
    /// says only a specific temperature is allowed for the resolved model, the request is
    /// re-sent once with that value — and the substitution is logged as a warning, never
    /// applied silently (the capability doctrine). Only a temperature the request carried is
    /// replaced: one nothing sets is not sent (GAP-36), and leaves nothing to retry. Any other
    /// rejection is handed to the base, which knows one more: a catalogue output cap the
    /// endpoint refused (LLM-10 — Moonshot bounds the cap by <c>window − prompt</c>).
    /// </summary>
    protected override bool TryAdaptRejectedPayload(
        Dictionary<string, object> payload, HttpStatusCode statusCode, string errorBody, LlmConfig effectiveConfig)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (statusCode != HttpStatusCode.BadRequest || errorBody is null)
            return base.TryAdaptRejectedPayload(payload, statusCode, errorBody ?? string.Empty, effectiveConfig);

        var match = TemperatureConstraint().Match(errorBody);
        if (!match.Success
            || !double.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var mandated))
        {
            return base.TryAdaptRejectedPayload(payload, statusCode, errorBody, effectiveConfig);
        }

        // Nothing to replace when no temperature was sent; never loop: if the mandated value is
        // already what we sent, the rejection is about something else — the base may still know
        // it; otherwise it surfaces.
        if (!payload.TryGetValue("temperature", out var current)
            || (current is double sent && sent.Equals(mandated)))
        {
            return base.TryAdaptRejectedPayload(payload, statusCode, errorBody, effectiveConfig);
        }

        LogTemperatureMandated(mandated, payload.TryGetValue("model", out var model) ? model : ResolveModel(null));
        payload["temperature"] = mandated;
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message =
        "Kimi rejected the configured temperature: the API mandates {Temperature} for model {Model}; retrying once with that value.")]
    private partial void LogTemperatureMandated(double temperature, object model);

    /// <summary>
    /// The K3 family (<c>kimi-k3</c>, <c>kimi-k3.1-pro</c>, <c>kimi-k3.1-pro-highspeed</c>) fixes its
    /// own sampling: <c>temperature</c> (only 1 is allowed, measured on all three on 2026-11-11) and
    /// <c>top_p</c>. Those fields are removed before the request leaves, on the buffered and the
    /// streaming paths alike, and each removal is reported as a structured warning, never dropped
    /// in silence. Its reasoning is not fixed: <c>thinking.type: disabled</c> is accepted and stops
    /// the trace (campaign M18, 2026-11-13), so the switch is sent as on any other Kimi model. The reactive retry of <see cref="TryAdaptRejectedPayload"/> stays for the
    /// models outside this family, which the API alone can describe.
    /// </summary>
    protected override void ApplyProviderSpecificOptions(Dictionary<string, object> payload, LlmConfig effectiveConfig)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(effectiveConfig);

        base.ApplyProviderSpecificOptions(payload, effectiveConfig);

        var model = ResolveModel(effectiveConfig);
        if (!IsFixedSamplingModel(model))
            return;

        var remedy = $"the model '{model}' fixes its own sampling; remove the setting, or pick a kimi-k2 model to steer it";
        if (payload.Remove("temperature"))
            LogUnsupportedOption("temperature", ProviderDisplayName, remedy);
        if (payload.Remove("top_p"))
            LogUnsupportedOption("top_p", ProviderDisplayName, remedy);
    }

    /// <summary>
    /// Whether <paramref name="model"/> belongs to the K3 family: <c>kimi-k3</c> itself, or an id that
    /// continues it at a boundary (<c>kimi-k3.1-pro</c>, <c>kimi-k3-preview</c>), case-insensitive.
    /// </summary>
    internal static bool IsFixedSamplingModel(string? model)
    {
        const string family = "kimi-k3";
        if (model is null || !model.StartsWith(family, StringComparison.OrdinalIgnoreCase))
            return false;

        return model.Length == family.Length || model[family.Length] is '.' or '-';
    }

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
