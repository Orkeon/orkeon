using Orkeon.Constants.Llm;
using Microsoft.Extensions.Logging;
using Polly;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;
using Orkeon.Infrastructure.LLMs.Converters;
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
    /// Ollama's <c>format</c> field accepts a full JSON Schema and <c>think</c> takes a boolean
    /// or an effort level, both on <c>/api/generate</c>. Vision goes through
    /// <c>/api/chat</c>, which takes a base64 <c>images</c> array rather than OpenAI-style
    /// content parts.
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonSchema,
        Thinking = ThinkingSupport.Toggle,
        Vision = true,
    };

    private readonly IToolCallingStrategy? _toolCallingStrategy;

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
    /// Constructor overload that accepts a tool calling strategy, enabling the native
    /// <c>/api/chat</c> tool protocol (LLM-07).
    /// </summary>
    public OllamaLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<OllamaLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        _baseUrl = ResolveBaseUrl(config);
        _toolCallingStrategy = toolCallingStrategy;
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

    /// <summary>
    /// Runs a multi-message conversation on Ollama's <c>/api/chat</c> endpoint, which is the
    /// only one that accepts <c>tools</c> and returns <c>message.tool_calls</c> (G-10).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The provider's other paths target <c>/api/generate</c>, which has no tool support —
    /// hence the historical text-fallback protocol documented in
    /// <c>docs/reference/limitations.md</c>. This override takes <c>/api/chat</c> only when it
    /// is actually needed (the message list carries tool metadata, the config declares tools,
    /// or a message carries an image); everything else keeps the previous path, so no existing
    /// behaviour moves.
    /// </para>
    /// <para>
    /// The response shape differs from OpenAI's in two ways — no <c>choices</c> array, and
    /// <c>arguments</c> as a JSON object rather than a string — which is exactly why the
    /// OpenAI parser could not read it. <see cref="SynthesizeOpenAiBody"/> reshapes it once,
    /// so the rest of the framework consumes Ollama tool calls like anyone else's.
    /// </para>
    /// </remarks>
    public override async Task<LlmResponse> ChatAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var effectiveConfig = config ?? Config;

        if (!RequiresChatEndpoint(messages, effectiveConfig))
            return await base.ChatAsync(messages, effectiveConfig, cancellationToken).ConfigureAwait(false);

        var payload = BuildChatPayload(messages, effectiveConfig);
        using var requestContent = new StringContent(
            SerializeToJson(payload), Encoding.UTF8, HttpDefaults.JsonContentType);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
        {
            Content = requestContent,
        };

        try
        {
            using var httpResponse = await ExecuteHttpRequestAsync(request, effectiveConfig, cancellationToken).ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
                return await CreateErrorResponseAsync(httpResponse, cancellationToken).ConfigureAwait(false);

            var responseContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseChatResponse(responseContent, effectiveConfig);
        }
        catch (HttpRequestException ex)
        {
            LogHttpRequestFailed(LogSanitizer.CreateSanitizedException(ex));
            return CreateExceptionResponse(ex, effectiveConfig);
        }
        catch (OperationCanceledException ex)
        {
            LogRequestTimeout(LogSanitizer.CreateSanitizedException(ex));
            return CreateExceptionResponse(ex, effectiveConfig);
        }
    }

    /// <summary>
    /// True when the conversation needs a capability only <c>/api/chat</c> offers. Anything
    /// else stays on the historical prompt-completion path.
    /// </summary>
    private static bool RequiresChatEndpoint(LlmMessage[] messages, LlmConfig config) =>
        config.Tools is { Count: > 0 }
        || messages.Any(m => m.ToolCallId != null || m.RawToolCalls != null)
        || messages.Any(HasImages);

    private static bool HasImages(LlmMessage message) =>
        message.MultiModalContent is { } content && content.HasImages;

    private Dictionary<string, object> BuildChatPayload(LlmMessage[] messages, LlmConfig config)
    {
        var options = OllamaRequestOptions.CreateBuilder()
            .AddTemperature(config.Temperature)
            .AddNumPredict(config.MaxTokens)
            .Build();

        var payload = new Dictionary<string, object>
        {
            ["model"] = config.Model ?? LlmProviderDefaultModels.Ollama,
            ["messages"] = BuildChatMessages(messages, config),
            ["stream"] = false,
            ["options"] = options.ToDictionary(),
        };

        ApplyChatResponseFormat(payload, config.ResponseFormat);
        ApplyChatThinking(payload, config.Thinking);
        ApplyChatTools(payload, config);

        return payload;
    }

    /// <summary>
    /// Builds the message list, prepending the configured system message when the conversation
    /// does not already carry one.
    /// </summary>
    /// <remarks>
    /// The prompt-completion path prepends <see cref="LlmConfig.SystemMessage"/> to the prompt.
    /// Without this, switching to <c>/api/chat</c> for tools or images would silently drop the
    /// system prompt — precisely in the case where an agent needs it most.
    /// </remarks>
    private static List<Dictionary<string, object?>> BuildChatMessages(LlmMessage[] messages, LlmConfig config)
    {
        var list = new List<Dictionary<string, object?>>(messages.Length + 1);

        var systemMessage = ResolveSystemMessage(config);
        if (!string.IsNullOrWhiteSpace(systemMessage)
            && !messages.Any(m => LlmRoles.IsSystem(m.Role)))
        {
            list.Add(new Dictionary<string, object?>
            {
                ["role"] = LlmRoles.System,
                ["content"] = systemMessage,
            });
        }

        list.AddRange(messages.Select(BuildChatMessage));
        return list;
    }

    /// <summary>Reads the system message from the typed property, then from the parameter bag.</summary>
    private static string? ResolveSystemMessage(LlmConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.SystemMessage))
            return config.SystemMessage;

        return config.CustomParameters is not null
            && config.CustomParameters.TryGetValue("system_message", out var raw)
            && raw is string text
                ? text
                : null;
    }

    private static Dictionary<string, object?> BuildChatMessage(LlmMessage message)
    {
        var (text, images) = message.MultiModalContent is { } content && content.Parts.Count > 0
            ? ContentConverter.ToOllamaMessage(content)
            : (message.Content, []);

        var dict = new Dictionary<string, object?>
        {
            ["role"] = message.Role,
            ["content"] = text,
        };

        if (images.Count > 0)
            dict["images"] = images;

        // Ollama expects arguments as a JSON object; the framework stores the OpenAI shape,
        // where they are a string. Convert back so a multi-turn conversation replays cleanly.
        if (message.RawToolCalls is { Length: > 0 } rawToolCalls)
            dict["tool_calls"] = ToOllamaToolCalls(rawToolCalls);

        return dict;
    }

    private static List<object> ToOllamaToolCalls(string openAiToolCallsJson)
    {
        var calls = new List<object>();
        using var doc = JsonDocument.Parse(openAiToolCallsJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return calls;

        foreach (var call in doc.RootElement.EnumerateArray())
        {
            if (!call.TryGetProperty("function", out var fn))
                continue;

            var name = fn.TryGetProperty("name", out var n) ? n.GetString() : null;
            var arguments = fn.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String
                ? JsonSerializer.Deserialize<JsonElement>(a.GetString() ?? "{}")
                : a;

            calls.Add(new Dictionary<string, object?>
            {
                ["function"] = new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["arguments"] = arguments,
                },
            });
        }

        return calls;
    }

    private static void ApplyChatResponseFormat(
        Dictionary<string, object> payload, LlmResponseFormat? responseFormat)
    {
        if (responseFormat is null
            || string.IsNullOrWhiteSpace(responseFormat.Type)
            || string.Equals(responseFormat.Type, "text", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        payload["format"] = responseFormat.Schema is { } schema
            ? JsonSerializer.Deserialize<JsonElement>(schema.Schema)
            : "json";
    }

    private void ApplyChatThinking(Dictionary<string, object> payload, LlmThinkingConfig? thinking)
    {
        if (thinking is null)
            return;

        if (!string.IsNullOrWhiteSpace(thinking.Effort))
            payload["think"] = thinking.Effort;
        else if (thinking.Enabled.HasValue)
            payload["think"] = thinking.Enabled.Value;

        if (thinking.BudgetTokens.HasValue)
        {
            LogUnsupportedOption("thinking.budgetTokens",
                "Ollama's think field takes a boolean or an effort level, not a token budget");
        }
    }

    private void ApplyChatTools(Dictionary<string, object> payload, LlmConfig config)
    {
        if (config.Tools is not { Count: > 0 } || _toolCallingStrategy?.SupportsNativeToolCalling != true)
            return;

        foreach (var kvp in _toolCallingStrategy.Formatter.FormatToolsForPayload(config.Tools, config.ToolMode))
            payload[kvp.Key] = kvp.Value;
    }

    /// <summary>
    /// Parses an <c>/api/chat</c> response, reshaping any tool calls into the OpenAI body the
    /// rest of the framework already knows how to read.
    /// </summary>
    private LlmResponse ParseChatResponse(string responseContent, LlmConfig config)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(responseContent);
        }
        catch (JsonException ex)
        {
            LogDeserializationFailed(
                LogSanitizer.CreateSanitizedException(ex), LogSanitizer.SanitizeString(responseContent));
            return CreateEmptyResponse(config, "JSON deserialization failed");
        }

        using (doc)
        {
            var root = doc.RootElement;
            var message = root.TryGetProperty("message", out var m) ? m : default;

            var content = message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("content", out var c)
                && c.ValueKind == JsonValueKind.String
                    ? c.GetString() ?? string.Empty
                    : string.Empty;

            var promptTokens = ReadInt(root, "prompt_eval_count");
            var completionTokens = ReadInt(root, "eval_count");

            var metadata = LlmResponseMetadata.CreateBuilder().AddProvider(Name);
            if (message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("thinking", out var thinking)
                && thinking.GetString() is { Length: > 0 } thinkingText)
            {
                metadata.Add("reasoning_content", thinkingText);
            }

            return new LlmResponse
            {
                Content = content,
                TokensUsed = (promptTokens ?? 0) + (completionTokens ?? 0),
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                Model = config.Model ?? LlmProviderDefaultModels.Ollama,
                Metadata = metadata.Build().ToDictionary(),
                RawResponseBody = SynthesizeOpenAiBody(message, content),
            };
        }
    }

    /// <summary>
    /// Rebuilds the OpenAI chat response shape (<c>choices[0].message.tool_calls[]</c>, with
    /// <c>arguments</c> as a JSON <em>string</em>) from Ollama's own.
    /// </summary>
    /// <remarks>
    /// This single translation is what makes native Ollama tool calling usable: the framework
    /// has one tool-call parser, and it reads the OpenAI shape. Returns <see langword="null"/>
    /// when the model called no tool, so the text-fallback path stays in charge for models
    /// without tool support.
    /// </remarks>
    private static string? SynthesizeOpenAiBody(JsonElement message, string content)
    {
        if (message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("tool_calls", out var toolCalls)
            || toolCalls.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var calls = new List<object?>();
        var index = 0;
        foreach (var call in toolCalls.EnumerateArray())
        {
            if (!call.TryGetProperty("function", out var fn))
                continue;

            var name = fn.TryGetProperty("name", out var n) ? n.GetString() : null;
            var arguments = fn.TryGetProperty("arguments", out var a) ? a.GetRawText() : "{}";

            calls.Add(new Dictionary<string, object?>
            {
                // Ollama does not issue call ids; a stable positional id keeps the
                // tool-result correlation the OpenAI protocol relies on.
                ["id"] = $"ollama_call_{index++}",
                ["type"] = "function",
                ["function"] = new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["arguments"] = arguments,
                },
            });
        }

        if (calls.Count == 0)
            return null;

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["choices"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["message"] = new Dictionary<string, object?>
                    {
                        ["role"] = "assistant",
                        ["content"] = content,
                        ["tool_calls"] = calls,
                    },
                },
            },
        });
    }

    private static int? ReadInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var prop)
        && prop.ValueKind == JsonValueKind.Number
        && prop.TryGetInt32(out var value)
            ? value
            : null;

    /// <summary>
    /// Streams a chat completion over Ollama's NDJSON, on whichever endpoint
    /// <see cref="ChatAsync"/> would have used for the same conversation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this override Ollama inherited <see cref="HttpLlmProviderBase.ChatStreamingAsync"/>,
    /// the buffered fallback: one content event carrying the whole answer, then the terminal
    /// event. The <c>IStreamingLlmProvider</c> contract was formally honoured while a crew
    /// streaming from Ollama waited for the complete response and then received it in one piece —
    /// no error, no warning. Every other provider in the fleet had a native path; Ollama was the
    /// last one on the fallback. Surfaced by the M4 probe of the campaign of 2026-08-01, which
    /// only caught it once M4 was tightened to demand more than a single delta.
    /// </para>
    /// <para>
    /// Endpoint selection is delegated to <see cref="RequiresChatEndpoint"/> — the same predicate
    /// the non-streaming path uses. Streaming must not silently move a conversation from
    /// <c>/api/generate</c> to <c>/api/chat</c>: that would change the system-message handling
    /// and the tool dialect purely as a side effect of asking for deltas.
    /// </para>
    /// </remarks>
    public override async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var effectiveConfig = config ?? Config;

        var useChatEndpoint = RequiresChatEndpoint(messages, effectiveConfig);
        var payload = useChatEndpoint
            ? BuildChatPayload(messages, effectiveConfig)
            : CreateRequestPayload(ConvertMessagesToPrompt(messages), effectiveConfig);
        payload["stream"] = true;

        var endpoint = new Uri($"{_baseUrl}/api/{(useChatEndpoint ? "chat" : "generate")}");
        var json = SerializeToJson(payload);

        // Do NOT use 'using' — factory-managed clients must not be disposed.
        var client = CreateHttpClient(effectiveConfig);

        var state = new OllamaStreamState();
        HttpResponseMessage? response = null;
        try
        {
            response = await SendStreamingRequestAsync(client, endpoint, json, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogStreamingError(response.StatusCode);
                yield return LlmStreamEvent.Complete(
                    await CreateErrorResponseAsync(response, cancellationToken).ConfigureAwait(false));
                yield break;
            }

            await foreach (var line in ReadNdjsonStreamAsync(response, cancellationToken).ConfigureAwait(false))
            {
                foreach (var ev in ReadStreamLine(line, state, useChatEndpoint))
                    yield return ev;
            }
        }
        finally
        {
            response?.Dispose();
        }

        yield return LlmStreamEvent.Complete(BuildStreamedResponse(state, effectiveConfig));
    }

    /// <summary>Mutable accumulation state of one streamed Ollama completion.</summary>
    private sealed class OllamaStreamState
    {
        public StringBuilder Content { get; } = new();
        public StringBuilder Thinking { get; } = new();
        public int? PromptTokens { get; set; }
        public int? CompletionTokens { get; set; }
        /// <summary>Raw <c>tool_calls</c> of the frame that carried them, kept for the final response.</summary>
        public string? ToolCallsJson { get; set; }
    }

    /// <summary>
    /// Reads one NDJSON frame, mutating <paramref name="state"/> and returning the events to
    /// emit. A malformed frame is skipped rather than allowed to kill the stream.
    /// </summary>
    /// <remarks>
    /// The two endpoints differ only in where the text sits: <c>/api/generate</c> puts it in
    /// <c>response</c>, <c>/api/chat</c> in <c>message.content</c>. The terminal frame carries the
    /// token counts in both, which is why the loop reads it instead of breaking on <c>done</c>.
    /// </remarks>
    private static IEnumerable<LlmStreamEvent> ReadStreamLine(
        string line, OllamaStreamState state, bool chatEndpoint)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.TryGetProperty("done", out var done)
                && done.ValueKind is JsonValueKind.True or JsonValueKind.False
                && done.GetBoolean())
            {
                state.PromptTokens = ReadInt(root, "prompt_eval_count") ?? state.PromptTokens;
                state.CompletionTokens = ReadInt(root, "eval_count") ?? state.CompletionTokens;
            }

            var message = chatEndpoint && root.TryGetProperty("message", out var m) ? m : default;

            if (message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("thinking", out var thinking)
                && thinking.ValueKind == JsonValueKind.String
                && thinking.GetString() is { Length: > 0 } thinkingDelta)
            {
                state.Thinking.Append(thinkingDelta);
                yield return LlmStreamEvent.Reasoning(thinkingDelta);
            }

            // Ollama emits a tool call whole, in a single frame — there is nothing to accumulate
            // by index the way the OpenAI dialect requires.
            if (message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("tool_calls", out var toolCalls)
                && toolCalls.ValueKind == JsonValueKind.Array)
            {
                state.ToolCallsJson = toolCalls.GetRawText();
            }

            var text = chatEndpoint
                ? ReadStringProperty(message, "content")
                : ReadStringProperty(root, "response");

            if (!string.IsNullOrEmpty(text))
            {
                state.Content.Append(text);
                yield return LlmStreamEvent.Content(text);
            }
        }
    }

    private static string? ReadStringProperty(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    /// <summary>
    /// Assembles the terminal response, equivalent to what <see cref="ChatAsync"/> would have
    /// returned — including the synthesized OpenAI body, so a streamed tool call is readable by
    /// the same parser as a buffered one.
    /// </summary>
    private LlmResponse BuildStreamedResponse(OllamaStreamState state, LlmConfig config)
    {
        var content = state.Content.ToString();

        var metadata = LlmResponseMetadata.CreateBuilder().AddProvider(Name);
        if (state.Thinking.Length > 0)
            metadata.Add("reasoning_content", state.Thinking.ToString());

        string? rawBody = null;
        if (state.ToolCallsJson is { Length: > 0 } toolCallsJson)
        {
            using var doc = JsonDocument.Parse(
                $$"""{"tool_calls":{{toolCallsJson}}}""");
            rawBody = SynthesizeOpenAiBody(doc.RootElement, content);
        }

        return new LlmResponse
        {
            Content = content,
            TokensUsed = (state.PromptTokens ?? 0) + (state.CompletionTokens ?? 0),
            PromptTokens = state.PromptTokens,
            CompletionTokens = state.CompletionTokens,
            Model = config.Model ?? LlmProviderDefaultModels.Ollama,
            Metadata = metadata.Build().ToDictionary(),
            RawResponseBody = rawBody,
        };
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
                throw await StreamingRejectionAsync(response, "Ollama", cancellationToken)
                    .ConfigureAwait(false);
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

            var model = Config.Model ?? "llama2";

            // Ollama declares thinking and vision because some of its models have them; the
            // one actually loaded may not, and it says so plainly. Name whose assumption was
            // wrong, or the refusal reads as Orkeon asking for impossible things (D-03).
            errorMessage += Base.CapabilityMismatchHint.ForVendorError(errorMessage, "Ollama", model);

            return new LlmResponse
            {
                Content = string.Empty,
                TokensUsed = 0,
                Model = model,
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
