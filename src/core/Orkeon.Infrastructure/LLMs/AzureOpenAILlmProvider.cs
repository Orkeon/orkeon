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

    /// <inheritdoc />
    public override async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt,
        LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var effectiveConfig = config ?? Config;

#pragma warning disable CS0618 // Type or member is obsolete
        if (string.IsNullOrEmpty(effectiveConfig.ApiKey) || effectiveConfig.BaseUrl is null)
            yield break;
#pragma warning restore CS0618

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
                yield break;
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
    /// Builds the Azure deployment-based endpoint URL:
    /// <c>{baseUrl}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}</c>.
    /// </summary>
    /// <param name="config">The effective LLM configuration (BaseUrl is validated by callers).</param>
    protected override Uri BuildEndpoint(LlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var baseUrl = config.BaseUrl!.ToString().TrimEnd('/');
        var deployment = config.Model ?? LlmDefaults.DefaultModelName;

        // Prefer typed ApiVersion property, fall back to CustomParameters
        var apiVersion = config.ApiVersion;
        if (string.IsNullOrWhiteSpace(apiVersion) &&
            config.CustomParameters != null &&
            config.CustomParameters.TryGetValue("api_version", out var versionObj) &&
            versionObj is string version &&
            !string.IsNullOrWhiteSpace(version))
        {
            apiVersion = version;
        }
        apiVersion ??= DefaultApiVersion;

        return new Uri($"{baseUrl}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}");
    }

    /// <summary>
    /// Validates the Azure-specific configuration requirements (api-key and resource endpoint).
    /// Returns an error <see cref="LlmResponse"/> when a requirement is missing, otherwise null.
    /// </summary>
    private LlmResponse? ValidateRequiredConfig(LlmConfig effectiveConfig)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        if (string.IsNullOrEmpty(effectiveConfig.ApiKey))
#pragma warning restore CS0618
        {
            return CreateConfigErrorResponse("Azure OpenAI API key is required");
        }

        if (effectiveConfig.BaseUrl is null)
        {
            return CreateConfigErrorResponse("Azure OpenAI endpoint (BaseUrl) is required");
        }

        return null;
    }

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
