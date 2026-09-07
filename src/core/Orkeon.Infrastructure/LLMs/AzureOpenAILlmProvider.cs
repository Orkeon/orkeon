using Microsoft.Extensions.Logging;
using Polly;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;
using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// Azure OpenAI LLM provider implementation.
/// Uses Azure-specific deployment-based endpoints with api-key authentication.
/// Azure OpenAI speaks the OpenAI Chat Completions dialect, so this provider builds on
/// <see cref="OpenAICompatibleProviderBase"/> (R10.7): with an <see cref="IToolCallingStrategy"/>
/// it injects <c>tools</c>/<c>tool_choice</c> into chat payloads and exposes the raw response
/// body so native <c>tool_calls</c> can be parsed, instead of the text fallback protocol.
/// </summary>
public partial class AzureOpenAILlmProvider : OpenAICompatibleProviderBase
{
    /// <summary>
    /// Default Azure OpenAI REST API version used when no "api_version" custom parameter is provided.
    /// Can be overridden per-request via <c>LlmConfig.CustomParameters["api_version"]</c>.
    /// See https://learn.microsoft.com/en-us/azure/ai-services/openai/reference for available versions.
    /// </summary>
    private const string DefaultApiVersion = HttpDefaults.AzureDefaults.DefaultApiVersion;

    /// <inheritdoc />
    public override string Name => "azure-openai";

    /// <summary>
    /// Azure has no provider-wide default endpoint: the resource URL must come from
    /// <see cref="LlmConfig.BaseUrl"/> (validated before every request — see
    /// <see cref="ValidateRequiredConfig"/>), so this is intentionally empty.
    /// </summary>
    protected override Uri DefaultBaseUrl => new("about:blank");

    /// <inheritdoc />
    protected override string DefaultModel => LlmDefaults.DefaultModelName;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "Azure OpenAI";

    /// <summary>
    /// Azure has no provider-wide endpoint: a resource URL is as mandatory as the key, and
    /// <c>ValidateRequiredConfig</c> refuses every call without one. The streaming capability
    /// must be withheld on the same terms, or the declaration is true about the credential and
    /// false about the endpoint — and a caller that trusts it gets a stream that ends with
    /// nothing in it (LLM-00 §8).
    /// </summary>
    protected override bool IsConfigured => base.IsConfigured && Config.BaseUrl is not null;

    /// <summary>
    /// Azure serves the OpenAI models through the OpenAI dialect, so it offers the same
    /// surface: Structured Outputs, a reasoning effort hint, and vision.
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonSchema,
        Thinking = ThinkingSupport.EffortOnly,
        Vision = true,
    };

    /// <summary>Initializes a new instance of <see cref="AzureOpenAILlmProvider"/>.</summary>
    /// <param name="config">The LLM configuration.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">Optional logger.</param>
    public AzureOpenAILlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<AzureOpenAILlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>
    /// Constructor overload that accepts an optional resilience policy for testing.
    /// </summary>
    public AzureOpenAILlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<AzureOpenAILlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Constructor overload that accepts a tool calling strategy (R10.7).</summary>
    /// <param name="config">The LLM configuration.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="toolCallingStrategy">Optional tool calling strategy for native tool support.</param>
    /// <param name="logger">Optional logger.</param>
    public AzureOpenAILlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<AzureOpenAILlmProvider>? logger = null)
        : base(config, httpClientFactory, toolCallingStrategy, logger)
    {
    }

    /// <inheritdoc />
    protected override void ConfigureHttpClient(HttpClient client, LlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(config);
        // Azure uses api-key header instead of Bearer token
        client.DefaultRequestHeaders.Authorization = null;

#pragma warning disable CS0618 // Type or member is obsolete
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            client.DefaultRequestHeaders.Add(HttpDefaults.AzureApiKeyHeader, config.ApiKey);
        }
#pragma warning restore CS0618
    }

    /// <inheritdoc />
    public override async Task<LlmResponse> GenerateAsync(
        string prompt,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveConfig = config ?? Config;

        var configError = ValidateRequiredConfig(effectiveConfig);
        if (configError is not null)
            return configError;

        return await base.GenerateAsync(prompt, effectiveConfig, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<LlmResponse> ChatAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveConfig = config ?? Config;

        // Guard BaseUrl/ApiKey before the OpenAI-compatible chat pipeline runs:
        // BuildEndpoint requires a non-empty Azure resource URL.
        var configError = ValidateRequiredConfig(effectiveConfig);
        if (configError is not null)
            return configError;

        return await base.ChatAsync(messages, effectiveConfig, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Streams a multi-message chat completion, guarding the Azure-specific configuration
    /// first (D-01).
    /// </summary>
    /// <remarks>
    /// Without this override the inherited implementation checks only the API key and then
    /// calls <see cref="BuildEndpoint"/>, which dereferences <c>config.BaseUrl!</c> — an
    /// <see cref="NullReferenceException"/> where the three other entry points return a typed
    /// error response. An event stream <em>can</em> carry the error, so the invalid-config case
    /// delegates to the base class's buffered fallback: it emits the standard
    /// <see cref="LlmStreamEventKind.Completed"/> event whose response holds the same
    /// configuration error the non-streaming paths produce.
    /// </remarks>
    public override async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var effectiveConfig = config ?? Config;

        var configError = ValidateRequiredConfig(effectiveConfig);
        if (configError is not null)
        {
            yield return LlmStreamEvent.Complete(configError);
            yield break;
        }

        await foreach (var ev in base.ChatStreamingAsync(messages, effectiveConfig, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return ev;
        }
    }

    /// <summary>
    /// Streams the completion token by token, guarding the Azure-specific configuration first.
    /// </summary>
    /// <remarks>
    /// This path returns <c>IAsyncEnumerable&lt;string&gt;</c> and has no channel to carry an
    /// error, so an empty sequence must mean one thing only: the deployment had nothing to say.
    /// Both failures therefore throw, as they do in the three other native streaming
    /// implementations (D5-02) -- an unusable configuration before the request, the vendor's own
    /// words after it. Logging the abort and yielding nothing, which is what this did, put the
    /// reason where an operator may read it later and left the caller with silence.
    /// </remarks>
    public override async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt,
        LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var effectiveConfig = config ?? Config;

        var missingRequirement = MissingRequirement(effectiveConfig);
        if (missingRequirement is not null)
            throw NotConfiguredForStreaming(ProviderDisplayName, missingRequirement);

        // Do NOT use 'using' — factory-managed clients must not be disposed.
        var client = CreateHttpClient(effectiveConfig);
        var endpoint = BuildEndpoint(effectiveConfig);

        var requestPayload = new
        {
            messages = new[] { new { role = "user", content = prompt } },
            temperature = effectiveConfig.Temperature,
            max_tokens = effectiveConfig.MaxTokens,
            stream = true
        };

        var json = JsonSerializer.Serialize(requestPayload, JsonOptions);

        HttpResponseMessage? response = null;
        try
        {
            response = await SendStreamingRequestAsync(client, endpoint, json, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogAzureStreamingError(response.StatusCode);
                throw await StreamingRejectionAsync(response, ProviderDisplayName, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Azure OpenAI uses OpenAI-compatible SSE format
            await foreach (var data in ReadSseStreamAsync(response, cancellationToken).ConfigureAwait(false))
            {
                using var doc = JsonDocument.Parse(data);
                var choices = doc.RootElement.GetProperty("choices");
                foreach (var choice in choices.EnumerateArray())
                {
                    if (choice.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var content))
                    {
                        var token = content.GetString();
                        if (token is not null)
                            yield return token;
                    }
                }
            }
        }
        finally
        {
            response?.Dispose();
        }
    }

    /// <summary>
    /// The <c>api_version</c> value that selects the v1 GA API instead of a dated version.
    /// </summary>
    private const string V1ApiMode = "v1";

    /// <summary>
    /// Builds the endpoint URL in whichever of Azure's two API shapes is configured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The historical shape is deployment-based and dated:
    /// <c>{baseUrl}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}</c>.
    /// Since August 2025 Azure also serves a **v1 GA** surface,
    /// <c>{baseUrl}/openai/v1/chat/completions</c>, with no <c>api-version</c> at all — it is
    /// the path to the Responses API and to the non-OpenAI models Azure resells (DeepSeek,
    /// Grok), none of which the dated shape can reach (audit gaps G-06, G-25).
    /// </para>
    /// <para>
    /// Selected with <c>api_version: v1</c> (typed property or custom parameter). The dated
    /// shape stays the default on purpose: switching it would silently change the URL of
    /// every existing deployment-based configuration.
    /// </para>
    /// </remarks>
    /// <param name="config">The effective LLM configuration (BaseUrl is validated by callers).</param>
    protected override Uri BuildEndpoint(LlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var baseUrl = config.BaseUrl!.ToString().TrimEnd('/');
        var apiVersion = ResolveApiVersion(config);

        if (string.Equals(apiVersion, V1ApiMode, StringComparison.OrdinalIgnoreCase))
            return new Uri($"{baseUrl}/openai/v1{ApiEndpointPath}");

        var deployment = config.Model ?? LlmDefaults.DefaultModelName;
        return new Uri($"{baseUrl}/openai/deployments/{deployment}{ApiEndpointPath}?api-version={apiVersion}");
    }

    /// <summary>Prefers the typed property, falls back to the custom-parameter bag, then to the dated default.</summary>
    private static string ResolveApiVersion(LlmConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.ApiVersion))
            return config.ApiVersion;

        if (config.CustomParameters != null &&
            config.CustomParameters.TryGetValue("api_version", out var versionObj) &&
            versionObj is string version &&
            !string.IsNullOrWhiteSpace(version))
        {
            return version;
        }

        return DefaultApiVersion;
    }

    /// <summary>The api-key requirement, named the way a caller reads it.</summary>
    private const string RequirementApiKey = "API key";

    /// <summary>The resource-endpoint requirement, named the way a caller reads it.</summary>
    private const string RequirementEndpoint = "endpoint (BaseUrl)";

    /// <summary>
    /// Names the mandatory Azure setting that is missing, or null when the configuration is
    /// complete.
    /// </summary>
    /// <remarks>
    /// Split out of <see cref="ValidateRequiredConfig"/> because the token-streaming path cannot
    /// use the <see cref="LlmResponse"/> that method returns -- it has to throw -- yet must name
    /// the very same missing setting, or one provider would answer the same absence with two
    /// different words depending on which entry point the caller took.
    /// </remarks>
    private static string? MissingRequirement(LlmConfig effectiveConfig)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        if (string.IsNullOrEmpty(effectiveConfig.ApiKey))
#pragma warning restore CS0618
        {
            return RequirementApiKey;
        }

        return effectiveConfig.BaseUrl is null ? RequirementEndpoint : null;
    }

    /// <summary>
    /// Validates the Azure-specific configuration requirements (api-key and resource endpoint).
    /// Returns an error <see cref="LlmResponse"/> when a requirement is missing, otherwise null.
    /// </summary>
    private LlmResponse? ValidateRequiredConfig(LlmConfig effectiveConfig)
    {
        var missing = MissingRequirement(effectiveConfig);
        return missing is null
            ? null
            : CreateConfigErrorResponse($"{ProviderDisplayName} {missing} is required");
    }

    /// <summary>
    /// Builds the exception the token-streaming path fails with when Azure cannot reach its API.
    /// </summary>
    /// <remarks>
    /// Same contract as the base <see cref="HttpLlmProviderBase.NotConfiguredForStreaming(string)"/>:
    /// same sentence, and a status code left null on purpose, since nothing was sent and no
    /// vendor refused anything. It is spelled out here only because that helper always names the
    /// API key, while Azure has a second mandatory setting -- a caller holding a valid key and
    /// told "API key is required" would look in the wrong place.
    /// </remarks>
    /// <param name="providerDisplayName">Provider name, as the reader sees it.</param>
    /// <param name="missingRequirement">The setting the configuration does not carry.</param>
    /// <returns>The exception to throw.</returns>
    private static HttpRequestException NotConfiguredForStreaming(
        string providerDisplayName,
        string missingRequirement)
        => new($"{providerDisplayName} {missingRequirement} is required: the request was never sent, so the stream carries nothing. Configure the LLM (run `orkeon init`) before streaming.");

    /// <summary>Creates an error response for a missing configuration requirement.</summary>
    private LlmResponse CreateConfigErrorResponse(string error)
    {
        return new LlmResponse
        {
            Content = "",
            Metadata = LlmResponseMetadata.CreateBuilder()
                .AddProvider(Name)
                .AddError(error)
                .Build()
                .ToDictionary()
        };
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Azure OpenAI streaming error: {StatusCode}")]
    private partial void LogAzureStreamingError(System.Net.HttpStatusCode statusCode);
}
