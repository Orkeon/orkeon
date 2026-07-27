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
    /// with non-text parts (R3.9). Providers that do not declare the capability keep their
    /// text-only payloads (multi-modal messages degrade to their
    /// <see cref="LlmMessage.Content"/> text fallback).
    /// </summary>
    /// <remarks>
    /// Reads the declared <see cref="LlmProviderCapabilities.Vision"/> rather than being
    /// overridden provider by provider (LLM-02).
    /// </remarks>
    protected virtual bool SupportsVisionContent => Capabilities.Vision;

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
        public StringBuilder Name { get; } = new();
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
                AccumulateUsage(usage, state);

            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
                return events;

            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
                    AccumulateDelta(delta, state, events);
            }
        }

        return events;
    }

    private static void AccumulateUsage(JsonElement usage, ChatStreamState state)
    {
        state.TotalTokens = usage.TryGetProperty("total_tokens", out var t) && t.TryGetInt32(out var tv) ? tv : state.TotalTokens;
        state.PromptTokens = TryReadInt(usage, "prompt_tokens") ?? state.PromptTokens;
        state.CompletionTokens = TryReadInt(usage, "completion_tokens") ?? state.CompletionTokens;
        state.CacheHitTokens = TryReadInt(usage, "prompt_cache_hit_tokens")
            ?? TryReadNestedInt(usage, "prompt_tokens_details", "cached_tokens")
            ?? state.CacheHitTokens;
        state.CacheMissTokens = TryReadInt(usage, "prompt_cache_miss_tokens") ?? state.CacheMissTokens;
    }

    private static void AccumulateDelta(JsonElement delta, ChatStreamState state, List<LlmStreamEvent> events)
    {
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

            MergeToolCallFragment(fragment, call);
        }
    }

    private static void MergeToolCallFragment(JsonElement fragment, StreamedToolCall call)
    {
        if (fragment.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            call.Id = id.GetString() ?? call.Id;

        if (!fragment.TryGetProperty("function", out var fn) || fn.ValueKind != JsonValueKind.Object)
            return;

        if (fn.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            call.Name.Append(name.GetString());
        if (fn.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.String)
            call.Arguments.Append(args.GetString());
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
            CacheMissTokens = state.CacheMissTokens
                ?? (state.CacheHitTokens is { } streamedHit ? DeriveCacheMiss(streamedHit, state.PromptTokens) : null),
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
                ["name"] = c.Name.ToString(),
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
    /// Enriches an outgoing assistant message with fields the provider requires to be replayed
    /// in multi-turn conversations.
    /// </summary>
    /// <remarks>
    /// Replaying <c>reasoning_content</c> is <em>not</em> a general property of thinking
    /// models — DeepSeek rejects a request without it
    /// (<c>HTTP 400: "The reasoning_content in the thinking mode must be passed back to the
    /// API."</c>) while Z.AI documents the opposite. It is therefore driven by the declared
    /// <see cref="LlmProviderCapabilities.ReplaysReasoningContent"/> rather than applied to
    /// every reasoning provider.
    /// </remarks>
    /// <param name="messageDict">The assistant message dictionary about to be serialized.</param>
    /// <param name="source">The source <see cref="LlmMessage"/>.</param>
    protected virtual void EnrichAssistantMessage(Dictionary<string, object?> messageDict, LlmMessage source)
    {
        ArgumentNullException.ThrowIfNull(messageDict);
        ArgumentNullException.ThrowIfNull(source);

        if (Capabilities.ReplaysReasoningContent && !string.IsNullOrEmpty(source.ReasoningContent))
            messageDict["reasoning_content"] = source.ReasoningContent;
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
    /// Writes the cross-cutting options (<c>thinking</c> / <c>reasoning_effort</c>,
    /// <c>response_format</c>) into the payload, in the OpenAI dialect, according to what
    /// this provider's <see cref="LlmProviderCapabilities"/> declare. Runs after the standard
    /// sampling/stop parameters so it can also override them.
    /// </summary>
    /// <remarks>
    /// The dialect is written <em>once</em>, here (LLM-02) — before, each provider that wanted
    /// one of these options copied the same block. Providers whose API speaks a different
    /// dialect (Anthropic, Ollama, Qwen's DashScope thinking fields) override this method;
    /// everyone else declares capabilities and gets the translation for free. An option the
    /// caller declared but the provider cannot honour is reported rather than dropped in
    /// silence.
    /// </remarks>
    /// <param name="payload">The payload dictionary about to be serialized.</param>
    /// <param name="effectiveConfig">The effective LLM configuration for this call.</param>
    protected virtual void ApplyProviderSpecificOptions(Dictionary<string, object> payload, LlmConfig effectiveConfig)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(effectiveConfig);

        ApplyThinkingOptions(payload, effectiveConfig.Thinking);
        ApplyResponseFormatOption(payload, effectiveConfig.ResponseFormat);
    }

    /// <summary>
    /// Translates <see cref="LlmThinkingConfig"/> into the OpenAI dialect, bounded by the
    /// declared <see cref="LlmProviderCapabilities.Thinking"/> level.
    /// </summary>
    private void ApplyThinkingOptions(Dictionary<string, object> payload, LlmThinkingConfig? thinking)
    {
        if (thinking is null)
            return;

        var support = Capabilities.Thinking;
        if (support == ThinkingSupport.None)
        {
            LogUnsupportedOption("thinking", ProviderDisplayName,
                "this API exposes no reasoning pass; remove the option or pick a provider that does");
            return;
        }

        // An explicit on/off switch needs more than an effort hint.
        if (thinking.Enabled.HasValue)
        {
            if (support >= ThinkingSupport.Toggle)
            {
                payload["thinking"] = new Dictionary<string, object?>
                {
                    ["type"] = thinking.Enabled.Value ? "enabled" : "disabled",
                };
            }
            else
            {
                LogUnsupportedOption("thinking.enabled", ProviderDisplayName,
                    "this API always decides on its own whether to reason; only the effort hint is honoured");
            }
        }

        if (!string.IsNullOrWhiteSpace(thinking.Effort))
            payload["reasoning_effort"] = thinking.Effort;

        // No OpenAI-dialect field carries a reasoning budget; only DashScope does, and Qwen
        // overrides this method to emit it.
        if (thinking.BudgetTokens.HasValue && support < ThinkingSupport.Budget)
        {
            LogUnsupportedOption("thinking.budgetTokens", ProviderDisplayName,
                "this API takes no reasoning token budget; use the effort hint instead");
        }
    }

    /// <summary>
    /// Translates <see cref="LlmResponseFormat"/> into the OpenAI <c>response_format</c>
    /// field, bounded by the declared <see cref="LlmProviderCapabilities.ResponseFormat"/>.
    /// </summary>
    private void ApplyResponseFormatOption(Dictionary<string, object> payload, LlmResponseFormat? responseFormat)
    {
        // "text" is the vendor default: writing nothing and writing text are equivalent, so
        // the historical payload is preserved.
        if (responseFormat is null
            || string.IsNullOrWhiteSpace(responseFormat.Type)
            || string.Equals(responseFormat.Type, "text", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (Capabilities.ResponseFormat == ResponseFormatSupport.None)
        {
            LogUnsupportedOption("response_format", ProviderDisplayName,
                "this API has no response-format field; constrain the output in the prompt instead");
            return;
        }

        payload["response_format"] = BuildResponseFormatValue(responseFormat);

        if (Capabilities.RequiresJsonKeywordInPrompt)
            WarnIfPromptDoesNotMentionJson(payload);
    }

    /// <summary>
    /// Builds the <c>response_format</c> value, degrading a schema to a plain JSON guarantee
    /// — loudly — on providers that only offer <c>json_object</c>.
    /// </summary>
    private Dictionary<string, object?> BuildResponseFormatValue(LlmResponseFormat responseFormat)
    {
        if (responseFormat.Schema is not { } schema)
            return new Dictionary<string, object?> { ["type"] = responseFormat.Type };

        if (Capabilities.ResponseFormat < ResponseFormatSupport.JsonSchema)
        {
            LogUnsupportedOption("response_format.schema", ProviderDisplayName,
                "this API only guarantees well-formed JSON — the request was downgraded to json_object, so describe the shape in the prompt");
            return new Dictionary<string, object?> { ["type"] = "json_object" };
        }

        return new Dictionary<string, object?>
        {
            ["type"] = "json_schema",
            ["json_schema"] = new Dictionary<string, object?>
            {
                ["name"] = schema.Name,
                ["strict"] = schema.Strict,
                // The schema travels as an authored JSON document; parsing it here keeps the
                // serializer from emitting it as an escaped string.
                ["schema"] = JsonSerializer.Deserialize<JsonElement>(schema.Schema),
            },
        };
    }

    /// <summary>
    /// Providers whose <c>json_object</c> mode requires the word "json" somewhere in the
    /// prompt (DeepSeek) can otherwise emit an unbounded whitespace stream until
    /// <c>max_tokens</c>. The prompt is never mutated — the contract is the caller's to fix,
    /// so a structured warning is surfaced instead.
    /// </summary>
    private void WarnIfPromptDoesNotMentionJson(Dictionary<string, object> payload)
    {
        if (!payload.TryGetValue("messages", out var messagesObj) || messagesObj is null)
            return;

        if (MessagesMentionJson(messagesObj))
            return;

        LogMissingJsonKeyword(ProviderDisplayName);
    }

    private static bool MessagesMentionJson(object messagesObj) =>
        messagesObj is System.Collections.IEnumerable messages
        && messages.Cast<object?>().Any(MessageMentionsJson);

    private static bool MessageMentionsJson(object? msg)
    {
        if (msg is null)
            return false;

        // Chat path: Dictionary<string, object?> with "role" / "content" keys.
        if (msg is IDictionary<string, object?> dict)
        {
            return dict.TryGetValue("role", out var roleObj) && roleObj is string role
                && IsSystemOrUser(role)
                && dict.TryGetValue("content", out var contentObj) && contentObj is string content
                && content.Contains("json", StringComparison.OrdinalIgnoreCase);
        }

        // Generate path: anonymous type { role, content } read via reflection.
        var type = msg.GetType();
        return type.GetProperty("role")?.GetValue(msg) is string anonRole
            && IsSystemOrUser(anonRole)
            && type.GetProperty("content")?.GetValue(msg) is string anonContent
            && anonContent.Contains("json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSystemOrUser(string role) =>
        role.Equals(LlmRoles.System, StringComparison.OrdinalIgnoreCase)
        || role.Equals(LlmRoles.User, StringComparison.OrdinalIgnoreCase);

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
        // OpenAI-standard variant (also Z.AI GLM): usage.prompt_tokens_details.cached_tokens
        // reports cache reads only — map it onto the same typed fields, deriving the miss
        // side from prompt_tokens so CacheHitRatio stays computable.
        if (cacheHit is null && TryReadNestedInt(usage, "prompt_tokens_details", "cached_tokens") is { } cachedTokens)
        {
            cacheHit = cachedTokens;
            cacheMiss ??= DeriveCacheMiss(cachedTokens, promptTokens);
        }

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

    private static int? TryReadNestedInt(JsonElement element, string objectName, string propertyName)
    {
        if (!element.TryGetProperty(objectName, out var nested)) return null;
        if (nested.ValueKind != JsonValueKind.Object) return null;
        return TryReadInt(nested, propertyName);
    }

    /// <summary>
    /// Derives the cache-miss side from a read-only cache metric: providers that report only
    /// <c>cached_tokens</c> (OpenAI, Z.AI) imply <c>miss = prompt_tokens - cached_tokens</c>.
    /// </summary>
    private static int? DeriveCacheMiss(int cacheHit, int? promptTokens)
        => promptTokens is { } prompt && prompt >= cacheHit ? prompt - cacheHit : null;

    /// <summary>
    /// Extracts provider-specific metadata from the API response. The default implementation
    /// pulls the reasoning trace out of providers that declare a thinking capability — that
    /// extraction was duplicated verbatim in DeepSeek and Z.AI before LLM-02. Override to add
    /// timing data or vendor-specific token breakdowns, calling <c>base</c> to keep the
    /// reasoning trace.
    /// </summary>
    /// <param name="doc">The parsed JSON document.</param>
    /// <param name="metadata">The metadata builder to populate.</param>
    protected virtual void ExtractResponseMetadata(JsonDocument doc, LlmResponseMetadata.Builder metadata)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (Capabilities.Thinking != ThinkingSupport.None)
            ExtractReasoningContent(doc.RootElement, metadata);
    }

    /// <summary>
    /// Reads <c>choices[0].message.reasoning_content</c> — the visible thinking trace of
    /// reasoning models — into the metadata bag when present.
    /// </summary>
    protected static void ExtractReasoningContent(JsonElement root, LlmResponseMetadata.Builder metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!root.TryGetProperty("choices", out var choices))
            return;

        var firstChoice = choices.EnumerateArray().FirstOrDefault();
        if (firstChoice.ValueKind == JsonValueKind.Undefined)
            return;

        if (!firstChoice.TryGetProperty("message", out var message))
            return;

        if (message.TryGetProperty("reasoning_content", out var reasoning)
            && reasoning.GetString() is { Length: > 0 } reasoningText)
        {
            metadata.Add("reasoning_content", reasoningText);
        }
    }

    /// <summary>
    /// Reads the two usage counters every OpenAI-compatible API reports. Shared so providers
    /// stop re-implementing it (LLM-02).
    /// </summary>
    protected static void ExtractStandardUsageMetadata(JsonElement root, LlmResponseMetadata.Builder metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!root.TryGetProperty("usage", out var usage))
            return;

        if (usage.TryGetProperty("prompt_tokens", out var promptTokens))
            metadata.Add("prompt_tokens", promptTokens.GetInt32());
        if (usage.TryGetProperty("completion_tokens", out var completionTokens))
            metadata.Add("completion_tokens", completionTokens.GetInt32());
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

    /// <summary>
    /// The end of the silence (LLM-02): an option the caller declared that this provider
    /// cannot honour is reported, with what to do about it, instead of being dropped.
    /// </summary>
    [LoggerMessage(EventId = 110, Level = LogLevel.Warning,
        Message = "Option '{Option}' was declared but {ProviderName} does not support it — it was not sent. {Remedy}.")]
    private partial void LogUnsupportedOption(string option, string providerName, string remedy);

    [LoggerMessage(EventId = 100, Level = LogLevel.Warning,
        Message = "{ProviderName} was asked for a JSON response format but no system/user message contains the word 'json'. This API may then emit an unbounded whitespace stream until max_tokens. Add 'json' to the prompt to be safe.")]
    private partial void LogMissingJsonKeyword(string providerName);
}
