using Microsoft.Extensions.Logging;
using Polly;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// Simplified Ollama LLM provider focused on HTTP adapter concerns only.
/// Business logic has been moved to domain services.
/// </summary>
public partial class OllamaLlmProvider : HttpLlmProviderBase
{
    /// <summary>
    /// Default Ollama base URL used when neither config nor environment variable is provided.
    /// </summary>
    private const string DefaultOllamaBaseUrl = LlmEndpoints.OllamaDefault;

    /// <summary>
    /// Environment variable name for overriding the Ollama base URL.
    /// </summary>
    private const string OllamaBaseUrlEnvVar = "OLLAMA_BASE_URL";

    private readonly string _baseUrl;

    /// <summary>
    /// Gets the resolved base URL used by this provider instance.
    /// Priority: config.BaseUrl > OLLAMA_BASE_URL env var > default (localhost:11434).
    /// </summary>
    public Uri BaseUrl => new(_baseUrl);

    /// <summary>
    /// Gets the provider name.
    /// </summary>
    public override string Name => "ollama";

    /// <summary>
    /// Ollama's <c>format</c> field accepts a full JSON Schema, and <c>think</c> takes a
    /// boolean or an effort level — both on <c>/api/generate</c>, the endpoint this provider
    /// targets. Vision is not declared: local vision models take a base64 <c>images</c> array
    /// rather than OpenAI-style content parts, a translation this provider does not do yet.
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonSchema,
        Thinking = ThinkingSupport.Toggle,
    };

    /// <summary>
    /// Initializes a new instance of the OllamaLlmProvider class.
    /// </summary>
    public OllamaLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<OllamaLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        _baseUrl = ResolveBaseUrl(config);
    }

    /// <summary>
    /// Constructor overload that accepts an optional resilience policy for testing.
    /// </summary>
    public OllamaLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<OllamaLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
        _baseUrl = ResolveBaseUrl(config);
    }

    /// <summary>
    /// Resolves the base URL using the priority chain:
    /// 1. config.BaseUrl (if provided and not empty)
    /// 2. OLLAMA_BASE_URL environment variable (if set)
    /// 3. Default fallback: http://localhost:11434
    /// </summary>
    private static string ResolveBaseUrl(LlmConfig config)
    {
        return config.BaseUrl?.ToString().TrimEnd('/')
            ?? Environment.GetEnvironmentVariable(OllamaBaseUrlEnvVar)?.TrimEnd('/')
            ?? DefaultOllamaBaseUrl;
    }

    /// <summary>
    /// Configures the HttpClient for Ollama-specific settings.
    /// Pure HTTP configuration - no business logic.
    /// </summary>
    protected override void ConfigureHttpClient(HttpClient client, LlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(client);
        // Ollama doesn't require API keys - remove auth header if present
        client.DefaultRequestHeaders.Authorization = null;

        // Set appropriate content type
        client.DefaultRequestHeaders.Add("Accept", HttpDefaults.JsonContentType);
    }

    /// <summary>
    /// Generates a response from Ollama.
    /// Pure HTTP adapter implementation.
    /// </summary>
    public override async Task<LlmResponse> GenerateAsync(
        string prompt,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var effectiveConfig = config ?? Config;
        var requestPayload = CreateRequestPayload(prompt, effectiveConfig);
        using var requestContent = new StringContent(
            SerializeToJson(requestPayload),
            Encoding.UTF8,
            HttpDefaults.JsonContentType);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/generate")
        {
            Content = requestContent
        };

        try
        {
            using var httpResponse = await ExecuteHttpRequestAsync(request, effectiveConfig, cancellationToken).ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                return await CreateErrorResponseAsync(httpResponse, cancellationToken).ConfigureAwait(false);
            }

            var responseContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseSuccessResponse(responseContent, effectiveConfig);
        }
        catch (HttpRequestException ex)
        {
            // SECURITY: Sanitize exception before logging to prevent credential exposure.
            var sanitizedEx = LogSanitizer.CreateSanitizedException(ex);
            LogHttpRequestFailed(sanitizedEx);
            return CreateExceptionResponse(ex, effectiveConfig);
        }
        catch (OperationCanceledException ex)
        {
            // SECURITY: Sanitize exception before logging to prevent credential exposure.
            // Catches both TaskCanceledException (timeout) and the bare
            // OperationCanceledException raised when a caller-supplied
            // CancellationToken trips before the HTTP send begins.
            var sanitizedEx = LogSanitizer.CreateSanitizedException(ex);
            LogRequestTimeout(sanitizedEx);
            return CreateExceptionResponse(ex, effectiveConfig);
        }
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt,
        LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            yield break;

        var effectiveConfig = config ?? Config;

        // Do NOT use 'using' — factory-managed clients must not be disposed.
        var client = CreateHttpClient(effectiveConfig);

        // Build payload with stream=true
        var options = OllamaRequestOptions.CreateBuilder()
            .AddTemperature(effectiveConfig.Temperature)
            .AddNumPredict(effectiveConfig.MaxTokens)
            .Build();

        var payload = OllamaRequestPayload.CreateBuilder()
            .AddModel(effectiveConfig.Model ?? "llama2")
            .AddPrompt(prompt)
            .AddStream(true)
            .AddOptions(options)
            .Build();

        var json = SerializeToJson(payload.ToDictionary());

        HttpResponseMessage? response = null;
        try
        {
            response = await SendStreamingRequestAsync(
                client, new Uri($"{_baseUrl}/api/generate"), json, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogStreamingError(response.StatusCode);
                yield break;
            }

            // Ollama uses NDJSON format: {"response":"token","done":false}
            await foreach (var line in ReadNdjsonStreamAsync(response, cancellationToken).ConfigureAwait(false))
            {
                using var doc = JsonDocument.Parse(line);

                if (doc.RootElement.TryGetProperty("done", out var doneEl) && doneEl.GetBoolean())
                    yield break;

                if (doc.RootElement.TryGetProperty("response", out var responseEl))
                {
                    var token = responseEl.GetString();
                    if (token is not null)
                        yield return token;
                }
            }
        }
        finally
        {
            response?.Dispose();
        }
    }

    /// <summary>
    /// Creates the request payload for Ollama API.
    /// Pure data transformation.
    /// </summary>
    private Dictionary<string, object> CreateRequestPayload(string prompt, LlmConfig config)
    {
        var options = OllamaRequestOptions.CreateBuilder()
            .AddTemperature(config.Temperature)
            .AddNumPredict(config.MaxTokens)
            .Build();

        // Handle system message: prefer typed property, fall back to CustomParameters
        var effectivePrompt = prompt;
        var systemMessage = config.SystemMessage;
        if (string.IsNullOrWhiteSpace(systemMessage) &&
            config.CustomParameters != null &&
            config.CustomParameters.TryGetValue("system_message", out var systemMessageObj) &&
            systemMessageObj is string sysMsg)
        {
            systemMessage = sysMsg;
        }
        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            effectivePrompt = $"{systemMessage}\n\n{prompt}";
        }

        var builder = OllamaRequestPayload.CreateBuilder()
            .AddModel(config.Model ?? "llama2")
            .AddPrompt(effectivePrompt)
            .AddStream(false)
            .AddOptions(options);

        if (!string.IsNullOrWhiteSpace(config.GrammarGbnf))
            builder.AddGrammar(config.GrammarGbnf);

        ApplyResponseFormat(builder, config.ResponseFormat);
        ApplyThinking(builder, config.Thinking);

        return builder.Build().ToDictionary();
    }

    /// <summary>
    /// Translates <see cref="LlmResponseFormat"/> into Ollama's <c>format</c> field, which
    /// accepts the literal <c>"json"</c> or a complete JSON Schema document.
    /// </summary>
    /// <remarks>
    /// Available on <c>/api/generate</c>, the endpoint this provider targets — no dependency
    /// on the <c>/api/chat</c> migration.
    /// </remarks>
    private static void ApplyResponseFormat(
        OllamaRequestPayload.Builder builder, LlmResponseFormat? responseFormat)
    {
        if (responseFormat is null
            || string.IsNullOrWhiteSpace(responseFormat.Type)
            || string.Equals(responseFormat.Type, "text", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (responseFormat.Schema is { } schema)
        {
            builder.AddFormat(JsonSerializer.Deserialize<JsonElement>(schema.Schema));
            return;
        }

        builder.AddFormat("json");
    }

    /// <summary>
    /// Translates <see cref="LlmThinkingConfig"/> into Ollama's <c>think</c> field, which
    /// takes either a boolean or one of <c>"low"</c>/<c>"medium"</c>/<c>"high"</c>. The effort
    /// level wins when both are set, since it is the more specific instruction.
    /// </summary>
    private void ApplyThinking(OllamaRequestPayload.Builder builder, LlmThinkingConfig? thinking)
    {
        if (thinking is null)
            return;

        if (!string.IsNullOrWhiteSpace(thinking.Effort))
            builder.AddThink(thinking.Effort);
        else if (thinking.Enabled.HasValue)
            builder.AddThink(thinking.Enabled.Value);

        if (thinking.BudgetTokens.HasValue)
        {
            LogUnsupportedOption("thinking.budgetTokens",
                "Ollama's think field takes a boolean or an effort level, not a token budget");
        }
    }

    /// <summary>
    /// Parses successful response from Ollama.
    /// Pure data transformation.
    /// </summary>
    private LlmResponse ParseSuccessResponse(string responseContent, LlmConfig config)
    {
        try
        {
            // Use direct JsonSerializer instead of base class method to catch JsonException properly
            var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseContent, JsonOptions);
            if (ollamaResponse == null)
            {
                // SECURITY: Sanitize response content before logging to prevent credential exposure.
                LogFailedParseResponse(LogSanitizer.SanitizeString(responseContent));
                return CreateEmptyResponse(config, "Failed to parse response");
            }

            return new LlmResponse
            {
                Content = ollamaResponse.Response ?? string.Empty,
                TokensUsed = 0, // Ollama doesn't provide token count in this format
                Model = config.Model ?? "llama2",
                Metadata = LlmResponseMetadata.CreateBuilder()
                    .AddProvider(Name)
                    .AddDone(ollamaResponse.Done)
                    .AddTotalDuration(ollamaResponse.TotalDuration)
                    .AddEvalDuration(ollamaResponse.EvalDuration)
                    .Build()
                    .ToDictionary()
            };
        }
        catch (JsonException ex)
        {
            // SECURITY: Sanitize exception and response content before logging.
            var sanitizedEx = LogSanitizer.CreateSanitizedException(ex);
            LogDeserializationFailed(sanitizedEx, LogSanitizer.SanitizeString(responseContent));
            return CreateEmptyResponse(config, "JSON deserialization failed");
        }
    }

    /// <summary>
    /// Creates a response from an exception.
    /// Pure data transformation.
    /// </summary>
    private LlmResponse CreateExceptionResponse(Exception ex, LlmConfig config)
    {
        return new LlmResponse
        {
            Content = string.Empty,
            TokensUsed = 0,
            Model = config.Model ?? "llama2",
            Metadata = LlmResponseMetadata.CreateBuilder()
                .AddProvider(Name)
                .AddError(LogSanitizer.SanitizeString(ex.Message))
                .AddErrorType(ex.GetType().Name)
                .Build()
                .ToDictionary()
        };
    }

    /// <summary>
    /// Creates an empty response with error information.
    /// Pure data transformation.
    /// </summary>
    private LlmResponse CreateEmptyResponse(LlmConfig config, string errorMessage)
    {
        return new LlmResponse
        {
            Content = string.Empty,
            TokensUsed = 0,
            Model = config.Model ?? "llama2",
            Metadata = LlmResponseMetadata.CreateBuilder()
                .AddProvider(Name)
                .AddError(errorMessage)
                .Build()
                .ToDictionary()
        };
    }

    /// <summary>
    /// Creates an error response from HTTP response, parsing Ollama-specific error format.
    /// </summary>
    protected override Task<LlmResponse> CreateErrorResponseAsync(
        HttpResponseMessage httpResponse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpResponse);
        return CreateErrorResponseCoreAsync();

        async Task<LlmResponse> CreateErrorResponseCoreAsync()
        {
            var rawErrorContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            // SECURITY: Sanitize error content before logging to prevent credential exposure.
            var sanitizedErrorContent = LogSanitizer.SanitizeString(rawErrorContent);
            LogLlmHttpError(httpResponse.StatusCode, sanitizedErrorContent);

            // Try to parse Ollama-specific error format
            string errorMessage = sanitizedErrorContent;
            try
            {
                var ollamaError = DeserializeFromJson<OllamaErrorResponse>(rawErrorContent);
                if (ollamaError?.Error != null)
                {
                    errorMessage = LogSanitizer.SanitizeString(ollamaError.Error);
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // If parsing fails, use the sanitized error content
            }

            return new LlmResponse
            {
                Content = string.Empty,
                TokensUsed = 0,
                Model = Config.Model ?? "llama2",
                Metadata = LlmResponseMetadata.CreateBuilder()
                    .AddProvider(Name)
                    .AddError(errorMessage)
                    .Build()
                    .ToDictionary()
            };
        }
    }

    /// <summary>
    /// Ollama API error response structure.
    /// </summary>
    private sealed class OllamaErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    /// <summary>
    /// Ollama API response structure.
    /// Pure data model.
    /// </summary>
    private sealed class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string? Response { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }

        [JsonPropertyName("total_duration")]
        public long TotalDuration { get; set; }

        [JsonPropertyName("eval_duration")]
        public long EvalDuration { get; set; }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "HTTP request failed for Ollama API")]
    private partial void LogHttpRequestFailed(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Request timeout for Ollama API")]
    private partial void LogRequestTimeout(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Ollama streaming error: {StatusCode}")]
    private partial void LogStreamingError(System.Net.HttpStatusCode statusCode);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Failed to parse Ollama response: {Response}")]
    private partial void LogFailedParseResponse(string response);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Failed to deserialize Ollama response: {Response}")]
    private partial void LogDeserializationFailed(Exception ex, string response);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "LLM HTTP error: {StatusCode} - {Error}")]
    private partial void LogLlmHttpError(System.Net.HttpStatusCode statusCode, string error);

    [LoggerMessage(EventId = 110, Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "Option '{Option}' was declared but Ollama does not support it — it was not sent. {Remedy}.")]
    private partial void LogUnsupportedOption(string option, string remedy);
}
