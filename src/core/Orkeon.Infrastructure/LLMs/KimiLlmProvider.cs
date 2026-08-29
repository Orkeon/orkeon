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
    /// Moonshot's API is OpenAI-compatible. Thinking is switchable on K2.6 (and always on for
    /// K3), and the K2.6/K3 generation accepts image input. JSON mode guarantees well-formed
    /// output without validating a schema.
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonObject,
        Thinking = ThinkingSupport.Toggle,
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
    /// applied silently (the capability doctrine).
    /// </summary>
    protected override bool TryAdaptRejectedPayload(
        Dictionary<string, object> payload, HttpStatusCode statusCode, string errorBody)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (statusCode != HttpStatusCode.BadRequest || errorBody is null)
            return false;

        var match = TemperatureConstraint().Match(errorBody);
        if (!match.Success
            || !double.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var mandated))
        {
            return false;
        }

        // Never loop: if the mandated value is already what we sent, the rejection is
        // about something else — surface it.
        if (payload.TryGetValue("temperature", out var current)
            && current is double sent && sent.Equals(mandated))
        {
            return false;
        }

        LogTemperatureMandated(mandated, payload.TryGetValue("model", out var model) ? model : DefaultModel);
        payload["temperature"] = mandated;
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message =
        "Kimi rejected the configured temperature: the API mandates {Temperature} for model {Model}; retrying once with that value.")]
    private partial void LogTemperatureMandated(double temperature, object model);

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
