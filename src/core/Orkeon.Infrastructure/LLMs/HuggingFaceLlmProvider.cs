using Orkeon.Constants.Llm;
using Microsoft.Extensions.Logging;
using Polly;
using System.Text.Json;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// HuggingFace LLM provider implementation.
/// Supports Serverless Inference API and dedicated Inference Endpoints.
/// Uses OpenAI-compatible chat completions format.
/// </summary>
public partial class HuggingFaceLlmProvider : OpenAICompatibleProviderBase
{
    /// <inheritdoc />
    public override string Name => "huggingface";

    /// <inheritdoc />
    protected override Uri DefaultBaseUrl => new(LlmEndpoints.HuggingFace);

    /// <inheritdoc />
    protected override string DefaultModel => LlmProviderDefaultModels.HuggingFace;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "HuggingFace";

    /// <summary>
    /// Inference Providers proxies many back-ends behind one OpenAI-compatible surface, so
    /// the declaration is the intersection that holds across partners: JSON mode and, on the
    /// VLM models most partners serve, image input. Reasoning control is model- and
    /// partner-specific and is therefore not declared.
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonObject,
        Vision = true,
    };

    /// <summary>Initializes a new instance of <see cref="HuggingFaceLlmProvider"/>.</summary>
    public HuggingFaceLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<HuggingFaceLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>Constructor overload that accepts an optional resilience policy for testing.</summary>
    public HuggingFaceLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<HuggingFaceLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Constructor overload that accepts a tool calling strategy.</summary>
    public HuggingFaceLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<HuggingFaceLlmProvider>? logger = null)
        : base(config, httpClientFactory, toolCallingStrategy, logger)
    {
    }

    /// <summary>
    /// Extracts HuggingFace-specific usage metadata from the response.
    /// </summary>
    protected override void ExtractResponseMetadata(JsonDocument doc, LlmResponseMetadata.Builder metadata)
    {
        ArgumentNullException.ThrowIfNull(doc);
        base.ExtractResponseMetadata(doc, metadata);
        ExtractStandardUsageMetadata(doc.RootElement, metadata);
    }

    /// <inheritdoc />
    protected override Dictionary<string, object> BuildRequestPayload(
        string prompt,
        LlmConfig config,
        IReadOnlyList<ToolSchema>? tools = null,
        ToolCallMode toolMode = ToolCallMode.Auto)
    {
        var payload = base.BuildRequestPayload(prompt, config, tools, toolMode);
        ValidateRoutingSuffix(payload.TryGetValue("model", out var model) ? model as string : null);
        return payload;
    }

    /// <summary>
    /// Checks the routing suffix of a model identifier, if any, and reports one Orkeon does
    /// not recognise (G-23).
    /// </summary>
    /// <remarks>
    /// The suffix is the only cost and latency lever on Inference Providers, and it already
    /// travelled to the wire untouched — what was missing was any way to know it was wrong.
    /// An unknown suffix is still forwarded: the partner list changes faster than this
    /// framework releases, so a warning is the honest response, not a rejection.
    /// </remarks>
    private void ValidateRoutingSuffix(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return;

        var separator = model.LastIndexOf(':');
        if (separator <= 0 || separator == model.Length - 1)
            return;

        var suffix = model[(separator + 1)..];
        if (RoutingPolicies.Contains(suffix) || KnownPartners.Contains(suffix))
            return;

        LogUnknownRoutingSuffix(suffix, string.Join(", ", RoutingPolicies));
    }

    /// <summary>
    /// The server-side provider-selection policies. Absent any suffix, the router behaves as
    /// <c>:fastest</c>.
    /// </summary>
    private static readonly HashSet<string> RoutingPolicies =
        new(StringComparer.OrdinalIgnoreCase) { "fastest", "cheapest", "preferred" };

    /// <summary>
    /// Partner names accepted in place of a policy to pin execution to one back-end. Kept as a
    /// diagnostic aid only — an unlisted name is forwarded, not rejected.
    /// </summary>
    private static readonly HashSet<string> KnownPartners =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "cerebras", "cohere", "deepinfra", "fal-ai", "featherless-ai", "fireworks-ai",
            "groq", "hf-inference", "novita", "nscale", "ovhcloud", "publicai", "replicate",
            "scaleway", "together", "wavespeed", "zai-org",
        };

    /// <summary>
    /// Appends a provider-selection policy to a model identifier, e.g.
    /// <c>openai/gpt-oss-120b</c> + <see cref="HuggingFaceRoutingPolicy.Cheapest"/> →
    /// <c>openai/gpt-oss-120b:cheapest</c>.
    /// </summary>
    /// <param name="modelId">The bare model identifier.</param>
    /// <param name="policy">The routing policy to request.</param>
    /// <returns>The suffixed model identifier.</returns>
    public static string WithRoutingPolicy(string modelId, HuggingFaceRoutingPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
#pragma warning disable CA1308 // the wire form of the suffix is lowercase
        return $"{modelId}:{policy.ToString().ToLowerInvariant()}";
#pragma warning restore CA1308
    }

    /// <summary>
    /// Pins execution to a specific partner, e.g. <c>openai/gpt-oss-120b:groq</c>.
    /// </summary>
    /// <param name="modelId">The bare model identifier.</param>
    /// <param name="partner">The partner name (e.g. <c>groq</c>, <c>together</c>).</param>
    /// <returns>The suffixed model identifier.</returns>
    public static string WithPartner(string modelId, string partner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(partner);
        return $"{modelId}:{partner}";
    }

    [LoggerMessage(EventId = 120, Level = LogLevel.Warning,
        Message = "HuggingFace model suffix ':{Suffix}' is neither a routing policy ({Policies}) nor a partner Orkeon knows. It is forwarded as-is; check the Inference Providers documentation if the call fails.")]
    private partial void LogUnknownRoutingSuffix(string suffix, string policies);
}

/// <summary>
/// Server-side provider-selection policy for HuggingFace Inference Providers, appended to a
/// model identifier as a suffix.
/// </summary>
public enum HuggingFaceRoutingPolicy
{
    /// <summary>Highest throughput in tokens per second. The router's default behaviour.</summary>
    Fastest,

    /// <summary>Lowest price per output token.</summary>
    Cheapest,

    /// <summary>The first available provider in the account's configured preference order.</summary>
    Preferred,
}
