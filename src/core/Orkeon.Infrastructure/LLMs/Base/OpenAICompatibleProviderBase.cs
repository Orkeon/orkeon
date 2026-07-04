using Microsoft.Extensions.Logging;
using Polly;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Converters;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Infrastructure.LLMs.Base;

/// <summary>
/// Abstract base class for LLM providers that implement the OpenAI Chat Completions API format.
/// Consolidates shared logic (payload building, response parsing, streaming, error handling)
/// so that concrete providers only need to supply their endpoint/model/display-name.
/// </summary>
public abstract partial class OpenAICompatibleProviderBase : HttpLlmProviderBase
{
    /// <summary>Gets the default base URL for this provider (e.g. "https://api.openai.com/v1").</summary>
    protected abstract Uri DefaultBaseUrl { get; }

    /// <summary>Gets the default model identifier when none is specified in the config.</summary>
    protected abstract string DefaultModel { get; }

    /// <summary>Gets the display name used in user-facing error messages (e.g. "OpenAI", "Groq").</summary>
    protected abstract string ProviderDisplayName { get; }

    /// <summary>Gets the API endpoint path appended to the base URL. Default is "/chat/completions".</summary>
    protected virtual string ApiEndpointPath => "/chat/completions";

    /// <summary>
    /// Whether this provider composes OpenAI vision content parts (<c>text</c> +
    /// <c>image_url</c>) for messages carrying <see cref="LlmMessage.MultiModalContent"/>
    /// with non-text parts (R3.9). Default is <see langword="false"/>: providers that do
    /// not opt in keep their historical text-only payloads (multi-modal messages degrade
    /// to their <see cref="LlmMessage.Content"/> text fallback). <c>OpenAIProvider</c>
    /// overrides this to <see langword="true"/>.
    /// </summary>
    protected virtual bool SupportsVisionContent => false;

    private readonly IToolCallingStrategy? _toolCallingStrategy;

    /// <summary>Initializes a new instance of <see cref="OpenAICompatibleProviderBase"/>.</summary>
    /// <param name="config">The LLM configuration.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">Optional logger.</param>
    protected OpenAICompatibleProviderBase(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>Initializes a new instance of <see cref="OpenAICompatibleProviderBase"/> with an explicit resilience policy.</summary>
    /// <param name="config">The LLM configuration.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="resiliencePolicy">Optional custom resilience policy.</param>
    /// <param name="logger">Optional logger.</param>
    protected OpenAICompatibleProviderBase(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Initializes a new instance of <see cref="OpenAICompatibleProviderBase"/> with a tool calling strategy.</summary>
    protected OpenAICompatibleProviderBase(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger? logger = null)
        : base(config, httpClientFactory, logger)
    {
        _toolCallingStrategy = toolCallingStrategy;
    }

    /// <summary>Initializes a new instance of <see cref="OpenAICompatibleProviderBase"/> with a tool calling strategy and resilience policy.</summary>
    protected OpenAICompatibleProviderBase(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
        _toolCallingStrategy = toolCallingStrategy;
    }

    /// <inheritdoc />
    public override async Task<LlmResponse> GenerateAsync(
        string prompt,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveConfig = config ?? Config;

#pragma warning disable CS0618 // Type or member is obsolete
        if (string.IsNullOrEmpty(effectiveConfig.ApiKey))
#pragma warning restore CS0618
        {
            return CreateMissingApiKeyResponse($"{ProviderDisplayName} API key is required");
        }

        try
        {
            return await SendGenerateRequestAsync(prompt, effectiveConfig, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex
            is OperationCanceledException
            or HttpRequestException
            // KeyNotFoundException: missing JSON properties (e.g. missing "usage" or empty "choices")
            or KeyNotFoundException
            // InvalidOperationException: GetProperty on default/undefined JsonElement (e.g. empty choices array)
            or InvalidOperationException
            or JsonException)
        {
            // SECURITY: HandleApiException logs a sanitized copy to prevent API key
            // exposure and builds the response from the original exception to preserve
            // the real exception type.
            return HandleApiException(ex);
        }
    }

    /// <summary>
    /// Sends the single-prompt completion request and parses the response.
    /// Extracted from <see cref="GenerateAsync"/> (R4.2 long-method decomposition).
    /// </summary>
    private async Task<LlmResponse> SendGenerateRequestAsync(
        string prompt,
        LlmConfig effectiveConfig,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint(effectiveConfig);
        var requestPayload = BuildRequestPayload(prompt, effectiveConfig);

        var json = JsonSerializer.Serialize(requestPayload, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, HttpDefaults.JsonContentType);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        using var response = await ExecuteHttpRequestAsync(request, effectiveConfig, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return await CreateApiErrorResponseAsync(response, cancellationToken).ConfigureAwait(false);
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseSuccessResponse(responseJson, effectiveConfig);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt,
        LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var effectiveConfig = config ?? Config;

#pragma warning disable CS0618 // Type or member is obsolete
        if (string.IsNullOrEmpty(effectiveConfig.ApiKey))
            yield break;
#pragma warning restore CS0618

        // Do NOT use 'using' — factory-managed clients must not be disposed.
        var client = CreateHttpClient(effectiveConfig);
        var endpoint = BuildEndpoint(effectiveConfig);

        var requestPayload = BuildRequestPayload(prompt, effectiveConfig);
        requestPayload["stream"] = true;

        var json = JsonSerializer.Serialize(requestPayload, JsonOptions);

        HttpResponseMessage? response = null;
        try
        {
            response = await SendStreamingRequestAsync(client, endpoint, json, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogStreamingError(response.StatusCode, ProviderDisplayName);
                yield break;
            }

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
    /// Streams a multi-message chat completion over SSE (exp07 F5 L2): emits
    /// <c>delta.content</c> as content deltas, <c>delta.reasoning_content</c> (DeepSeek
    /// thinking mode) as reasoning deltas, accumulates <c>delta.tool_calls</c> fragments by
    /// index, captures the final usage chunk (<c>stream_options.include_usage</c>), and
    /// terminates with a <see cref="LlmStreamEventKind.Completed"/> event whose response is
    /// equivalent to the non-streaming <see cref="ChatAsync"/> result — including a
    /// synthesized OpenAI-shaped <see cref="LlmResponse.RawResponseBody"/> when the model
    /// streamed tool calls, so tool-call parsers work unchanged. Providers that ignore
    /// <c>stream_options</c> yield a Completed event with zero usage (degraded but safe).
    /// </summary>
    public override async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var effectiveConfig = config ?? Config;

#pragma warning disable CS0618 // Type or member is obsolete
        var missingKey = string.IsNullOrEmpty(effectiveConfig.ApiKey);
#pragma warning restore CS0618
        if (missingKey)
        {
            // Buffered fallback produces the standard missing-key error response.
            await foreach (var ev in base.ChatStreamingAsync(messages, effectiveConfig, cancellationToken).ConfigureAwait(false))
                yield return ev;
            yield break;
        }

        // Do NOT use 'using' — factory-managed clients must not be disposed.
        var client = CreateHttpClient(effectiveConfig);
        var endpoint = BuildEndpoint(effectiveConfig);

        var payload = BuildChatRequestBody(messages, effectiveConfig);
        payload["stream"] = true;
        payload["stream_options"] = new Dictionary<string, object> { ["include_usage"] = true };
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        var state = new ChatStreamState();
        HttpResponseMessage? response = null;
        try
        {
            response = await SendStreamingRequestAsync(client, endpoint, json, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogStreamingError(response.StatusCode, ProviderDisplayName);
                yield return LlmStreamEvent.Complete(
                    await CreateApiErrorResponseAsync(response, cancellationToken).ConfigureAwait(false));
                yield break;
            }

            await foreach (var data in ReadSseStreamAsync(response, cancellationToken).ConfigureAwait(false))
            {
                foreach (var ev in ParseChatStreamChunk(data, state))
                    yield return ev;
            }
        }
        finally
        {
            response?.Dispose();
        }

        yield return LlmStreamEvent.Complete(BuildStreamedResponse(state, effectiveConfig));
    }

    /// <summary>Mutable accumulation state of one streamed chat completion.</summary>
    private sealed class ChatStreamState
    {
        public StringBuilder Content { get; } = new();
        public StringBuilder Reasoning { get; } = new();
        /// <summary>Tool-call fragments accumulated by stream index.</summary>
        public SortedDictionary<int, StreamedToolCall> ToolCalls { get; } = new();
        public int TotalTokens { get; set; }
        public int? PromptTokens { get; set; }
        public int? CompletionTokens { get; set; }
        public int? CacheHitTokens { get; set; }
        public int? CacheMissTokens { get; set; }
    }

    private sealed class StreamedToolCall
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public StringBuilder Arguments { get; } = new();
    }

    /// <summary>
    /// Parses one SSE data chunk, mutating <paramref name="state"/> and returning the
    /// delta events to emit. Malformed chunks are skipped (defensive: a single bad frame
    /// must not kill the stream).
    /// </summary>
    private static List<LlmStreamEvent> ParseChatStreamChunk(string data, ChatStreamState state)
    {
        var events = new List<LlmStreamEvent>(1);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(data);
        }
        catch (JsonException)
        {
            return events;
        }

        using (doc)
        {
            var root = doc.RootElement;

            // Usage arrives on a dedicated (often choices-less) final chunk with include_usage.
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                state.TotalTokens = usage.TryGetProperty("total_tokens", out var t) && t.TryGetInt32(out var tv) ? tv : state.TotalTokens;
                state.PromptTokens = TryReadInt(usage, "prompt_tokens") ?? state.PromptTokens;
                state.CompletionTokens = TryReadInt(usage, "completion_tokens") ?? state.CompletionTokens;
                state.CacheHitTokens = TryReadInt(usage, "prompt_cache_hit_tokens") ?? state.CacheHitTokens;
                state.CacheMissTokens = TryReadInt(usage, "prompt_cache_miss_tokens") ?? state.CacheMissTokens;
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
                return events;

            foreach (var choice in choices.EnumerateArray())
            {
                if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
                    continue;

                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    var token = content.GetString();
                    if (!string.IsNullOrEmpty(token))
                    {
                        state.Content.Append(token);
                        events.Add(LlmStreamEvent.Content(token));
                    }
                }

                if (delta.TryGetProperty("reasoning_content", out var reasoning) && reasoning.ValueKind == JsonValueKind.String)
                {
                    var token = reasoning.GetString();
                    if (!string.IsNullOrEmpty(token))
                    {
                        state.Reasoning.Append(token);
                        events.Add(LlmStreamEvent.Reasoning(token));
                    }
                }

                if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
                    AccumulateToolCallFragments(toolCalls, state);
            }
        }

        return events;
    }

    private static void AccumulateToolCallFragments(JsonElement toolCalls, ChatStreamState state)
    {
        foreach (var fragment in toolCalls.EnumerateArray())
        {
            var index = fragment.TryGetProperty("index", out var idx) && idx.TryGetInt32(out var iv) ? iv : 0;
            if (!state.ToolCalls.TryGetValue(index, out var call))
            {
                call = new StreamedToolCall();
                state.ToolCalls[index] = call;
            }

            if (fragment.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                call.Id = id.GetString() ?? call.Id;

            if (fragment.TryGetProperty("function", out var fn) && fn.ValueKind == JsonValueKind.Object)
            {
                if (fn.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                    call.Name += name.GetString();
                if (fn.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.String)
                    call.Arguments.Append(args.GetString());
            }
        }
    }

    /// <summary>
    /// Builds the terminal <see cref="LlmResponse"/> of a streamed chat completion —
    /// iso-shape with <see cref="ParseSuccessResponse"/>, including the synthesized
    /// tool-calls body when the model streamed tool calls.
    /// </summary>
    private LlmResponse BuildStreamedResponse(ChatStreamState state, LlmConfig effectiveConfig)
    {
        var metadata = LlmResponseMetadata.CreateBuilder().AddProvider(Name);
        if (state.Reasoning.Length > 0)
            metadata.Add("reasoning_content", state.Reasoning.ToString());

        return new LlmResponse
        {
            Content = state.Content.ToString(),
            TokensUsed = state.TotalTokens,
            PromptTokens = state.PromptTokens,
            CompletionTokens = state.CompletionTokens,
            CacheHitTokens = state.CacheHitTokens,
            CacheMissTokens = state.CacheMissTokens,
            Model = effectiveConfig.Model,
            Metadata = metadata.Build().ToDictionary(),
            RawResponseBody = state.ToolCalls.Count > 0 ? SynthesizeChatBody(state) : null,
        };
    }

    /// <summary>
    /// Reassembles the OpenAI chat response shape
    /// (<c>choices[0].message.tool_calls[]</c>) from accumulated stream fragments so
    /// downstream parsers (e.g. the scripted act loop's <c>TryParseToolCall</c>) consume
    /// streamed and buffered responses identically.
    /// </summary>
    private static string SynthesizeChatBody(ChatStreamState state)
    {
        var toolCalls = state.ToolCalls.Values.Select(c => new Dictionary<string, object?>
        {
            ["id"] = c.Id,
            ["type"] = "function",
            ["function"] = new Dictionary<string, object?>
            {
                ["name"] = c.Name,
                ["arguments"] = c.Arguments.ToString(),
            },
        }).ToList();

        var body = new Dictionary<string, object?>
        {
            ["choices"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["message"] = new Dictionary<string, object?>
                    {
                        ["role"] = "assistant",
                        ["content"] = state.Content.ToString(),
                        ["tool_calls"] = toolCalls,
                    },
                },
            },
        };
        return JsonSerializer.Serialize(body);
    }

    /// <summary>
    /// Overrides the base ChatAsync to build a proper multi-message request payload
    /// that supports native tool calling (tool role, tool_call_id, assistant tool_calls).
    /// When messages contain tool-specific fields (ToolCallId, RawToolCalls), they are
    /// serialized into OpenAI-format message objects for correct multi-turn conversation.
    /// </summary>
    public override async Task<LlmResponse> ChatAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveConfig = config ?? Config;

        // Use the full chat completions path when:
        // - messages already carry tool-call metadata (subsequent iterations), OR
        // - the config defines tools to send (first iteration with tools), OR
        // - a message carries multi-modal content and this provider supports vision (R3.9).
        // Otherwise, delegate to base (simple prompt concatenation).
        if (!HasToolMetadata(messages) && !HasToolSchemas(effectiveConfig) && !HasVisionPayload(messages))
        {
            return await base.ChatAsync(messages, effectiveConfig, cancellationToken).ConfigureAwait(false);
        }

#pragma warning disable CS0618 // Type or member is obsolete
        if (string.IsNullOrEmpty(effectiveConfig.ApiKey))
            return CreateMissingApiKeyResponse($"{ProviderDisplayName} API key is required");
#pragma warning restore CS0618

        try
        {
            return await SendChatRequestAsync(messages, effectiveConfig, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            return HandleApiException(ex);
        }
        catch (HttpRequestException ex)
        {
            return HandleApiException(ex);
        }
        catch (JsonException ex)
        {
            return HandleApiException(ex);
        }
    }

    private static bool HasToolMetadata(LlmMessage[] messages) =>
        messages.Any(m => m.ToolCallId != null || m.RawToolCalls != null);

    private static bool HasToolSchemas(LlmConfig config) =>
        config.Tools is { Count: > 0 };

    /// <summary>
    /// True when this provider supports vision content and at least one message
    /// carries multi-modal content with non-text parts.
    /// </summary>
    private bool HasVisionPayload(LlmMessage[] messages) =>
        SupportsVisionContent && messages.Any(ShouldComposeVisionContent);

    /// <summary>
    /// True when the message's multi-modal content should be emitted as structured
    /// OpenAI content parts instead of the plain text fallback.
    /// </summary>
    private bool ShouldComposeVisionContent(LlmMessage msg) =>
        SupportsVisionContent &&
        msg.MultiModalContent is { } content && content.Parts.Count > 0 && !content.IsTextOnly;

    private LlmResponse HandleApiException(Exception ex)
    {
        // Log a sanitized copy (secrets redacted) but build the response from the
        // original exception so the real type is preserved (mirrors OllamaLlmProvider).
        var sanitizedEx = LogSanitizer.CreateSanitizedException(ex);
        LogApiCallFailed(sanitizedEx, ProviderDisplayName);
        return CreateExceptionResponse(ex);
    }

    private async Task<LlmResponse> SendChatRequestAsync(
        LlmMessage[] messages,
        LlmConfig effectiveConfig,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint(effectiveConfig);

        var payload = BuildChatRequestBody(messages, effectiveConfig);

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, HttpDefaults.JsonContentType);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        using var response = await ExecuteHttpRequestAsync(request, effectiveConfig, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return await CreateApiErrorResponseAsync(response, cancellationToken).ConfigureAwait(false);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseSuccessResponse(responseJson, effectiveConfig);
    }

    /// <summary>
    /// Builds the full chat request body: messages list, core parameters,
    /// optional sampling options, and tool injection.
    /// </summary>
    private Dictionary<string, object> BuildChatRequestBody(LlmMessage[] messages, LlmConfig effectiveConfig)
    {
        var messagesList = BuildChatMessagesList(messages);

        var payload = new Dictionary<string, object>
        {
            ["model"] = effectiveConfig.Model ?? DefaultModel,
            ["messages"] = messagesList,
            ["temperature"] = effectiveConfig.Temperature,
            ["max_tokens"] = effectiveConfig.MaxTokens
        };

        ApplyOptions(payload, effectiveConfig);
        ApplyTools(payload, effectiveConfig);

        return payload;
    }

    /// <summary>
    /// Translates Orkeon <see cref="LlmMessage"/>s into the OpenAI chat completions
    /// messages array, including tool/assistant-with-tool-calls special cases.
    /// </summary>
    private List<Dictionary<string, object?>> BuildChatMessagesList(LlmMessage[] messages)
    {
        var messagesList = new List<Dictionary<string, object?>>(messages.Length);
        foreach (var msg in messages)
        {
            messagesList.Add(FormatChatMessage(msg));
        }
        return messagesList;
    }

    private Dictionary<string, object?> FormatChatMessage(LlmMessage msg)
    {
        if (LlmRoles.IsTool(msg.Role) && msg.ToolCallId != null)
        {
            return new Dictionary<string, object?>
            {
                ["role"] = LlmRoles.Tool,
                ["tool_call_id"] = msg.ToolCallId,
                ["content"] = msg.Content
            };
        }

        if (LlmRoles.IsAssistant(msg.Role) && msg.RawToolCalls != null)
        {
            var assistantMsg = new Dictionary<string, object?>
            {
                ["role"] = LlmRoles.Assistant,
                ["content"] = !string.IsNullOrEmpty(msg.Content) ? msg.Content : null
            };

            // Parse raw tool_calls JSON back into the message as a deserialized object
            // so JsonSerializer produces the correct nested structure
            var toolCallsElement = JsonSerializer.Deserialize<JsonElement>(msg.RawToolCalls);
            assistantMsg["tool_calls"] = toolCallsElement;

            EnrichAssistantMessage(assistantMsg, msg);
            return assistantMsg;
        }

        var basicMsg = new Dictionary<string, object?>
        {
            ["role"] = msg.Role,
            ["content"] = BuildMessageContent(msg)
        };

        if (LlmRoles.IsAssistant(msg.Role))
        {
            EnrichAssistantMessage(basicMsg, msg);
        }

        return basicMsg;
    }

    /// <summary>
    /// Builds the <c>content</c> value of an outgoing chat message. When the provider
    /// supports vision and the message carries multi-modal content with non-text parts,
    /// the content is the structured OpenAI parts array (<c>text</c> + <c>image_url</c>)
    /// composed by <see cref="ContentConverter.ToOpenAIContentParts"/>; otherwise it is
    /// the historical plain text string.
    /// </summary>
    private object? BuildMessageContent(LlmMessage msg) =>
        ShouldComposeVisionContent(msg) && msg.MultiModalContent is { } content
            ? ContentConverter.ToOpenAIContentParts(content)
            : msg.Content;

    /// <summary>
    /// Hook for provider-specific enrichment of an outgoing assistant message dictionary.
    /// Override in subclasses to attach provider-specific fields (e.g. DeepSeek's
    /// <c>reasoning_content</c>) that must be replayed in multi-turn conversations.
    /// Default implementation is a no-op.
    /// </summary>
    /// <param name="messageDict">The assistant message dictionary about to be serialized.</param>
    /// <param name="source">The source <see cref="LlmMessage"/>.</param>
    protected virtual void EnrichAssistantMessage(Dictionary<string, object?> messageDict, LlmMessage source)
    {
        // No enrichment by default.
    }

    /// <summary>
    /// Applies optional sampling/stop parameters that are only emitted when non-default,
    /// then lets the provider attach any provider-specific extensions.
    /// </summary>
    private void ApplyOptions(Dictionary<string, object> payload, LlmConfig effectiveConfig)
    {
        if (effectiveConfig.TopP != 1.0)
            payload["top_p"] = effectiveConfig.TopP;
        if (effectiveConfig.StopSequences is { Count: > 0 })
            payload["stop"] = effectiveConfig.StopSequences;
        ApplyProviderSpecificOptions(payload, effectiveConfig);
    }

    /// <summary>
    /// Hook for provider-specific request fields (e.g. DeepSeek's <c>thinking</c> block and
    /// <c>reasoning_effort</c>). Default implementation is a no-op so subclasses opt in.
    /// Runs after the standard sampling/stop parameters so it can also override them.
    /// </summary>
    /// <param name="payload">The payload dictionary about to be serialized.</param>
    /// <param name="effectiveConfig">The effective LLM configuration for this call.</param>
    protected virtual void ApplyProviderSpecificOptions(Dictionary<string, object> payload, LlmConfig effectiveConfig)
    {
        // No-op by default.
    }

    /// <summary>
    /// Injects the <c>tools</c>/<c>tool_choice</c> keys produced by the configured strategy
    /// when the effective config carries tool schemas and the strategy supports native
    /// tool calling.
    /// </summary>
    private void ApplyTools(Dictionary<string, object> payload, LlmConfig effectiveConfig)
    {
        if (effectiveConfig.Tools is not { Count: > 0 } ||
            _toolCallingStrategy?.SupportsNativeToolCalling != true)
        {
            return;
        }

        var toolPayload = _toolCallingStrategy.Formatter.FormatToolsForPayload(
            effectiveConfig.Tools, effectiveConfig.ToolMode);
        foreach (var kvp in toolPayload)
            payload[kvp.Key] = kvp.Value;
    }

    /// <summary>
    /// Builds the full endpoint URL. Default: "{baseUrl}{ApiEndpointPath}".
    /// </summary>
    /// <param name="config">The effective LLM configuration.</param>
    /// <returns>The full endpoint URL.</returns>
    protected virtual Uri BuildEndpoint(LlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var baseUrl = (config.BaseUrl is null
            ? DefaultBaseUrl.ToString()
            : config.BaseUrl.ToString()).TrimEnd('/');
        return new Uri($"{baseUrl}{ApiEndpointPath}");
    }

    /// <summary>
    /// Builds the request payload dictionary. Override to add provider-specific fields.
    /// </summary>
    /// <param name="prompt">The user prompt.</param>
    /// <param name="config">The effective LLM configuration.</param>
    /// <param name="tools">Optional tool schemas to include in the payload.</param>
    /// <param name="toolMode">The tool call mode (auto, required, none).</param>
    /// <returns>The payload dictionary ready for JSON serialization.</returns>
    protected virtual Dictionary<string, object> BuildRequestPayload(
        string prompt,
        LlmConfig config,
        IReadOnlyList<ToolSchema>? tools = null,
        ToolCallMode toolMode = ToolCallMode.Auto)
    {
        ArgumentNullException.ThrowIfNull(config);
        var messages = new List<object>();

        var systemMessage = ExtractSystemMessage(config);
        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            messages.Add(new { role = LlmRoles.System, content = systemMessage });
        }

        messages.Add(new { role = LlmRoles.User, content = prompt });

        var payload = new Dictionary<string, object>
        {
            ["model"] = config.Model ?? DefaultModel,
            ["messages"] = messages,
            ["temperature"] = config.Temperature,
            ["max_tokens"] = config.MaxTokens
        };

        if (config.TopP != 1.0)
        {
            payload["top_p"] = config.TopP;
        }

        if (config.StopSequences != null && config.StopSequences.Count > 0)
        {
            payload["stop"] = config.StopSequences;
        }

        if (!string.IsNullOrWhiteSpace(config.GrammarGbnf))
        {
            // llama.cpp-compatible backends (llama-server, vLLM with grammar plugin,
            // Ollama on /v1/chat/completions with grammar field) honour a top-level
            // `grammar` string. Providers that ignore it return free-form output,
            // which is caught by StructuredOutputResolver's JsonDocument.Parse safety check.
            payload["grammar"] = config.GrammarGbnf;
        }

        ApplyProviderSpecificOptions(payload, config);

        // Inject tools if available and strategy supports it
        if (tools is { Count: > 0 } && _toolCallingStrategy?.SupportsNativeToolCalling == true)
        {
            var toolPayload = _toolCallingStrategy.Formatter.FormatToolsForPayload(tools, toolMode);
            foreach (var kvp in toolPayload)
                payload[kvp.Key] = kvp.Value;
        }

        return payload;
    }

    /// <summary>
    /// Extracts the system message from config. Checks <see cref="LlmConfig.SystemMessage"/> first,
    /// then falls back to <c>CustomParameters["system_message"]</c>.
    /// </summary>
    /// <param name="config">The LLM configuration.</param>
    /// <returns>The system message, or null if none found.</returns>
    protected static string? ExtractSystemMessage(LlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var systemMessage = config.SystemMessage;
        if (string.IsNullOrWhiteSpace(systemMessage) &&
            config.CustomParameters != null &&
            config.CustomParameters.TryGetValue("system_message", out var systemObj) &&
            systemObj is string sysMsg)
        {
            systemMessage = sysMsg;
        }
        return systemMessage;
    }

    /// <summary>
    /// Parses a successful JSON response into an <see cref="LlmResponse"/>.
    /// </summary>
    /// <param name="responseJson">The raw JSON response body.</param>
    /// <param name="config">The effective LLM configuration.</param>
    /// <returns>The parsed LLM response.</returns>
    protected virtual LlmResponse ParseSuccessResponse(string responseJson, LlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        using var doc = JsonDocument.Parse(responseJson);

        // Content may be null when the response contains tool_calls instead
        var contentElement = doc.RootElement
            .GetProperty("choices")
            .EnumerateArray()
            .FirstOrDefault()
            .GetProperty("message");

        var messageContent = contentElement.TryGetProperty("content", out var contentProp)
            && contentProp.ValueKind == JsonValueKind.String
            ? contentProp.GetString() ?? ""
            : "";

        var usage = doc.RootElement.GetProperty("usage");
        var tokensUsed = usage.GetProperty("total_tokens").GetInt32();

        int? promptTokens = TryReadInt(usage, "prompt_tokens");
        int? completionTokens = TryReadInt(usage, "completion_tokens");
        // OpenAI-compatible providers that implement DeepSeek-style prompt caching expose
        // these as siblings of prompt_tokens. Honor them generically: any provider that
        // surfaces the keys gets the typed fields for free (others stay null = unmeasured).
        int? cacheHit = TryReadInt(usage, "prompt_cache_hit_tokens");
        int? cacheMiss = TryReadInt(usage, "prompt_cache_miss_tokens");

        var metadata = LlmResponseMetadata.CreateBuilder()
            .AddProvider(Name);

        ExtractResponseMetadata(doc, metadata);

        return new LlmResponse
        {
            Content = messageContent,
            TokensUsed = tokensUsed,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            CacheHitTokens = cacheHit,
            CacheMissTokens = cacheMiss,
            Model = config.Model,
            Metadata = metadata.Build().ToDictionary(),
            RawResponseBody = _toolCallingStrategy?.SupportsNativeToolCalling == true
                ? responseJson
                : null
        };
    }

    private static int? TryReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop)) return null;
        if (prop.ValueKind != JsonValueKind.Number) return null;
        return prop.TryGetInt32(out var value) ? value : null;
    }

    /// <summary>
    /// Extracts provider-specific metadata from the API response.
    /// Override in derived classes to add timing data, token breakdowns, etc.
    /// </summary>
    /// <param name="doc">The parsed JSON document.</param>
    /// <param name="metadata">The metadata builder to populate.</param>
    protected virtual void ExtractResponseMetadata(JsonDocument doc, LlmResponseMetadata.Builder metadata)
    {
        // Default: no extra metadata beyond provider name
    }

    private LlmResponse CreateMissingApiKeyResponse(string message)
    {
        return new LlmResponse
        {
            Content = "",
            Metadata = LlmResponseMetadata.CreateBuilder()
                .AddProvider(Name)
                .AddError(message)
                .Build()
                .ToDictionary()
        };
    }

    private async Task<LlmResponse> CreateApiErrorResponseAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var rawError = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var error = LogSanitizer.SanitizeString(rawError);
        LogApiError(response.StatusCode, error);
        return new LlmResponse
        {
            Content = "",
            Metadata = LlmResponseMetadata.CreateBuilder()
                .AddProvider(Name)
                .AddError($"{ProviderDisplayName} API error: {response.StatusCode} - {error}")
                .AddErrorType("APIError")
                .Build()
                .ToDictionary()
        };
    }

    private LlmResponse CreateExceptionResponse(Exception ex)
    {
        // Surface the real exception type (e.g. HttpRequestException, TaskCanceledException)
        // rather than collapsing everything to the sanitized wrapper, mirroring the
        // OllamaLlmProvider policy. The message is sanitized to avoid leaking secrets.
        return new LlmResponse
        {
            Content = "",
            Metadata = LlmResponseMetadata.CreateBuilder()
                .AddProvider(Name)
                .AddError($"{ProviderDisplayName} API call failed: {LogSanitizer.SanitizeString(ex.Message)}")
                .AddErrorType(ex.GetType().Name)
                .Build()
                .ToDictionary()
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "LLM API Error ({StatusCode}): {Error}")]
    private partial void LogApiError(HttpStatusCode statusCode, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "{ProviderName} API call failed")]
    private partial void LogApiCallFailed(Exception ex, string providerName);

    [LoggerMessage(Level = LogLevel.Error, Message = "{ProviderName} streaming error: {StatusCode}")]
    private partial void LogStreamingError(HttpStatusCode statusCode, string providerName);
}
