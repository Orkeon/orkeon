using Orkeon.Constants.Llm;
using Microsoft.Extensions.Logging;
using Polly;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;
using Orkeon.Infrastructure.LLMs.Converters;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// Anthropic Claude LLM provider implementation using the Messages API.
/// </summary>
public partial class AnthropicLlmProvider : HttpLlmProviderBase
{
    internal const string DefaultBaseUrl = LlmEndpoints.Anthropic;

    /// <summary>
    /// Anthropic Messages API version sent via the "anthropic-version" header.
    /// See https://docs.anthropic.com/en/api/versioning for available versions.
    /// </summary>
    private const string ApiVersion = HttpDefaults.AnthropicApiVersion;

    private readonly IToolCallingStrategy? _toolCallingStrategy;

    /// <inheritdoc />
    public override string Name => "anthropic";

    /// <summary>
    /// Claude constrains output through <c>output_config.format</c> (schema included), takes
    /// an adaptive thinking block with an effort level, sees images, and is the one provider
    /// whose prompt cache must be requested explicitly with <c>cache_control</c> breakpoints.
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonSchema,
        Thinking = ThinkingSupport.Toggle,
        Vision = true,
        ExplicitPromptCaching = true,
    };

    /// <summary>Initializes a new instance of <see cref="AnthropicLlmProvider"/>.</summary>
    /// <param name="config">The LLM configuration.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">Optional logger.</param>
    public AnthropicLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<AnthropicLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>
    /// Constructor overload that accepts an optional resilience policy for testing.
    /// </summary>
    public AnthropicLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<AnthropicLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>
    /// Constructor overload that accepts an optional tool calling strategy. When provided
    /// and the effective config carries <see cref="LlmConfig.Tools"/>, the strategy's
    /// formatter injects the Anthropic-shaped <c>tools</c>/<c>tool_choice</c> keys into the
    /// request payload, and the raw response body is preserved for downstream tool-call
    /// parsing.
    /// </summary>
    public AnthropicLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<AnthropicLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
        _toolCallingStrategy = toolCallingStrategy;
    }

    /// <inheritdoc />
    protected override void ConfigureHttpClient(HttpClient client, LlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(config);
        // Anthropic uses x-api-key header instead of Bearer token
        client.DefaultRequestHeaders.Authorization = null;

#pragma warning disable CS0618 // Type or member is obsolete
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            client.DefaultRequestHeaders.Add(HttpDefaults.AnthropicApiKeyHeader, config.ApiKey);
        }
#pragma warning restore CS0618

        client.DefaultRequestHeaders.Add(HttpDefaults.AnthropicVersionHeader, ApiVersion);
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
            return new LlmResponse
            {
                Content = "",
                Metadata = LlmResponseMetadata.CreateBuilder()
                    .AddProvider(Name)
                    .AddError("Anthropic API key is required")
                    .Build()
                    .ToDictionary()
            };
        }

        try
        {
            var baseUrl = (effectiveConfig.BaseUrl is null
                    ? DefaultBaseUrl
                    : effectiveConfig.BaseUrl.ToString()).TrimEnd('/');
            var endpoint = new Uri($"{baseUrl}/v1/messages");

            // Build messages array - extract system message if present
            // Prefer typed SystemMessage property, fall back to CustomParameters
            string? systemMessage = effectiveConfig.SystemMessage;
            var messages = new List<object>();

            if (string.IsNullOrWhiteSpace(systemMessage) &&
                effectiveConfig.CustomParameters != null &&
                effectiveConfig.CustomParameters.TryGetValue("system_message", out var systemObj) &&
                systemObj is string sysMsg &&
                !string.IsNullOrWhiteSpace(sysMsg))
            {
                systemMessage = sysMsg;
            }

            messages.Add(new { role = LlmRoles.User, content = prompt });

            var requestPayload = BuildRequestPayload(effectiveConfig, messages, systemMessage);
            var json = JsonSerializer.Serialize(requestPayload, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, HttpDefaults.JsonContentType);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
            using var response = await ExecuteHttpRequestAsync(request, effectiveConfig, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                LogApiError(response.StatusCode, error);
                return new LlmResponse
                {
                    Content = "",
                    Metadata = LlmResponseMetadata.CreateBuilder()
                        .AddProvider(Name)
                        .AddError($"Anthropic API error: {response.StatusCode} - {error}")
                        .AddErrorType("APIError")
                        .Build()
                        .ToDictionary()
                };
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseResponse(responseJson, effectiveConfig);
        }
        catch (OperationCanceledException ex)
        {
            LogApiCallFailed(ex);
            return new LlmResponse
            {
                Content = "",
                Metadata = LlmResponseMetadata.CreateBuilder()
                    .AddProvider(Name)
                    .AddError($"Anthropic API call failed: {ex.Message}")
                    .AddErrorType(ex.GetType().Name)
                    .Build()
                    .ToDictionary()
            };
        }
        catch (HttpRequestException ex)
        {
            LogApiCallFailed(ex);
            return new LlmResponse
            {
                Content = "",
                Metadata = LlmResponseMetadata.CreateBuilder()
                    .AddProvider(Name)
                    .AddError($"Anthropic API call failed: {ex.Message}")
                    .AddErrorType(nameof(HttpRequestException))
                    .Build()
                    .ToDictionary()
            };
        }
        catch (KeyNotFoundException ex)
        {
            LogApiCallFailed(ex);
            return new LlmResponse
            {
                Content = "",
                Metadata = LlmResponseMetadata.CreateBuilder()
                    .AddProvider(Name)
                    .AddError($"Anthropic response parsing failed: {ex.Message}")
                    .AddErrorType(nameof(KeyNotFoundException))
                    .Build()
                    .ToDictionary()
            };
        }
        catch (JsonException ex)
        {
            LogApiCallFailed(ex);
            return new LlmResponse
            {
                Content = "",
                Metadata = LlmResponseMetadata.CreateBuilder()
                    .AddProvider(Name)
                    .AddError($"Anthropic response parsing failed: {ex.Message}")
                    .AddErrorType(nameof(JsonException))
                    .Build()
                    .ToDictionary()
            };
        }
    }

    /// <inheritdoc />
    public override async Task<LlmResponse> ChatAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        if (messages == null || messages.Length == 0)
        {
            return await GenerateAsync(string.Empty, config, cancellationToken).ConfigureAwait(false);
        }

        var effectiveConfig = config ?? Config;

#pragma warning disable CS0618 // Type or member is obsolete
        if (string.IsNullOrEmpty(effectiveConfig.ApiKey))
#pragma warning restore CS0618
        {
            return BuildApiKeyMissingResponse();
        }

        try
        {
            return await SendChatRequestAsync(messages, effectiveConfig, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            LogApiCallFailed(ex);
            return BuildErrorResponse($"Anthropic API call failed: {ex.Message}", ex.GetType().Name);
        }
        catch (HttpRequestException ex)
        {
            LogApiCallFailed(ex);
            return BuildErrorResponse($"Anthropic API call failed: {ex.Message}", nameof(HttpRequestException));
        }
        catch (KeyNotFoundException ex)
        {
            LogApiCallFailed(ex);
            return BuildErrorResponse($"Anthropic response parsing failed: {ex.Message}", nameof(KeyNotFoundException));
        }
        catch (JsonException ex)
        {
            LogApiCallFailed(ex);
            return BuildErrorResponse($"Anthropic response parsing failed: {ex.Message}", nameof(JsonException));
        }
    }

    private async Task<LlmResponse> SendChatRequestAsync(
        LlmMessage[] messages, LlmConfig effectiveConfig, CancellationToken cancellationToken)
    {
        var baseUrl = (effectiveConfig.BaseUrl is null
                ? DefaultBaseUrl
                : effectiveConfig.BaseUrl.ToString()).TrimEnd('/');
        var endpoint = new Uri($"{baseUrl}/v1/messages");

        var (conversationMessages, systemMessage) = SeparateSystemMessages(messages, effectiveConfig);

        var requestPayload = BuildRequestPayload(effectiveConfig, conversationMessages, systemMessage);
        var json = JsonSerializer.Serialize(requestPayload, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, HttpDefaults.JsonContentType);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        using var response = await ExecuteHttpRequestAsync(request, effectiveConfig, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            LogApiError(response.StatusCode, error);
            return BuildErrorResponse($"Anthropic API error: {response.StatusCode} - {error}", "APIError");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseResponse(responseJson, effectiveConfig);
    }

    internal static (List<object> ConversationMessages, string? SystemMessage) SeparateSystemMessages(
        LlmMessage[] messages, LlmConfig effectiveConfig)
    {
        string? systemMessage = null;
        var conversationMessages = new List<object>();
        var knownToolUseIds = CollectKnownToolUseIds(messages);

        for (int i = 0; i < messages.Length; i++)
        {
            var msg = messages[i];

            if (LlmRoles.IsSystem(msg.Role))
                AddSystemOrUserMessage(msg, ref systemMessage, conversationMessages);
            else if (LlmRoles.IsAssistant(msg.Role) && !string.IsNullOrEmpty(msg.RawToolCalls))
                AddAssistantWithToolCalls(msg, conversationMessages);
            else if (LlmRoles.IsTool(msg.Role) && !string.IsNullOrEmpty(msg.ToolCallId))
                AddToolResultMessages(messages, ref i, knownToolUseIds, conversationMessages);
            else
                conversationMessages.Add(BuildSimpleMessage(msg));
        }

        systemMessage = ResolveSystemMessageFallback(systemMessage, effectiveConfig);

        if (conversationMessages.Count == 0)
            conversationMessages.Add(new { role = LlmRoles.User, content = "" });

        return (conversationMessages, systemMessage);
    }

    private static HashSet<string> CollectKnownToolUseIds(LlmMessage[] messages)
    {
        var knownIds = new HashSet<string>();
        foreach (var m in messages)
        {
            if (!LlmRoles.IsAssistant(m.Role) || string.IsNullOrEmpty(m.RawToolCalls))
                continue;
            try
            {
                using var doc = JsonDocument.Parse(m.RawToolCalls);
                foreach (var tc in doc.RootElement.EnumerateArray())
                {
                    if (tc.TryGetProperty("id", out var idProp))
                        knownIds.Add(idProp.GetString() ?? "");
                }
            }
            catch (JsonException) { /* malformed — skip */ }
        }
        return knownIds;
    }

    private static void AddSystemOrUserMessage(
        LlmMessage msg, ref string? systemMessage, List<object> conversationMessages)
    {
        if (systemMessage == null)
            systemMessage = msg.Content;
        else
            conversationMessages.Add(new { role = LlmRoles.User, content = msg.Content });
    }

    /// <summary>
    /// Builds a plain user/assistant message. When the message carries multi-modal content
    /// with non-text parts (vision, R3.9), the content becomes an array of Anthropic content
    /// blocks (<c>text</c> + <c>image</c> with base64/url source) composed by
    /// <see cref="ContentConverter.ToAnthropicContentBlocks"/>; otherwise the historical
    /// plain-string content is emitted.
    /// </summary>
    private static object BuildSimpleMessage(LlmMessage msg) =>
        msg.MultiModalContent is { } content && content.Parts.Count > 0 && !content.IsTextOnly
            ? new { role = msg.Role, content = (object)ContentConverter.ToAnthropicContentBlocks(content) }
            : new { role = msg.Role, content = msg.Content };

    private static void AddAssistantWithToolCalls(LlmMessage msg, List<object> conversationMessages)
    {
        var blocks = new List<object>();

        if (!string.IsNullOrEmpty(msg.Content))
            blocks.Add(new { type = "text", text = msg.Content });

        using var toolCallsDoc = JsonDocument.Parse(msg.RawToolCalls!);
        foreach (var tc in toolCallsDoc.RootElement.EnumerateArray())
        {
            var id = tc.GetProperty("id").GetString() ?? "";
            var function = tc.GetProperty("function");
            var name = function.GetProperty("name").GetString() ?? "";
            var argumentsJson = function.GetProperty("arguments").GetString() ?? "{}";
            var input = JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson)
                        ?? new Dictionary<string, object>();

            blocks.Add(new { type = "tool_use", id, name, input });
        }

        conversationMessages.Add(new { role = LlmRoles.Assistant, content = (object)blocks });
    }

    private static void AddToolResultMessages(
        LlmMessage[] messages, ref int i, HashSet<string> knownToolUseIds, List<object> conversationMessages)
    {
        var toolResults = new List<object>();
        var firstMsg = messages[i];

        if (knownToolUseIds.Contains(firstMsg.ToolCallId!))
            toolResults.Add(new { type = "tool_result", tool_use_id = firstMsg.ToolCallId, content = firstMsg.Content });

        while (i + 1 < messages.Length
               && LlmRoles.IsTool(messages[i + 1].Role)
               && !string.IsNullOrEmpty(messages[i + 1].ToolCallId))
        {
            i++;
            if (messages[i].ToolCallId is { } peekId && knownToolUseIds.Contains(peekId))
                toolResults.Add(new { type = "tool_result", tool_use_id = peekId, content = messages[i].Content });
        }

        if (toolResults.Count > 0)
            conversationMessages.Add(new { role = LlmRoles.User, content = (object)toolResults });
    }

    private static string? ResolveSystemMessageFallback(string? systemMessage, LlmConfig effectiveConfig)
    {
        if (!string.IsNullOrWhiteSpace(systemMessage))
            return systemMessage;

        if (!string.IsNullOrWhiteSpace(effectiveConfig.SystemMessage))
            return effectiveConfig.SystemMessage;

        if (effectiveConfig.CustomParameters != null &&
            effectiveConfig.CustomParameters.TryGetValue("system_message", out var systemObj) &&
            systemObj is string sysMsg &&
            !string.IsNullOrWhiteSpace(sysMsg))
        {
            return sysMsg;
        }

        return null;
    }

    private LlmResponse BuildApiKeyMissingResponse() =>
        new()
        {
            Content = "",
            Metadata = LlmResponseMetadata.CreateBuilder()
                .AddProvider(Name)
                .AddError("Anthropic API key is required")
                .Build()
                .ToDictionary()
        };

    private LlmResponse BuildErrorResponse(string error, string errorType) =>
        new()
        {
            Content = "",
            Metadata = LlmResponseMetadata.CreateBuilder()
                .AddProvider(Name)
                .AddError(error)
                .AddErrorType(errorType)
                .Build()
                .ToDictionary()
        };

    private Dictionary<string, object> BuildRequestPayload(LlmConfig config, List<object> messages, string? systemMessage)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = config.Model ?? LlmProviderDefaultModels.Anthropic,
            ["messages"] = messages,
            ["max_tokens"] = config.MaxTokens > 0 ? config.MaxTokens : 4096,
            ["temperature"] = config.Temperature
        };

        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            payload["system"] = systemMessage;
        }

        if (config.TopP != 1.0)
        {
            payload["top_p"] = config.TopP;
        }

        if (config.StopSequences != null && config.StopSequences.Count > 0)
        {
            payload["stop_sequences"] = config.StopSequences;
        }

        // Inject tools if the configured strategy supports native tool calling
        if (config.Tools is { Count: > 0 } && _toolCallingStrategy?.SupportsNativeToolCalling == true)
        {
            var toolPayload = _toolCallingStrategy.Formatter.FormatToolsForPayload(
                config.Tools, config.ToolMode);
            foreach (var kvp in toolPayload)
                payload[kvp.Key] = kvp.Value;
        }

        ApplyThinking(payload, config.Thinking);
        ApplyResponseFormat(payload, config.ResponseFormat);
        ApplyCacheBreakpoints(payload, config.Cache);

        return payload;
    }

    /// <summary>
    /// Places <c>cache_control</c> breakpoints on the stable prefix of the request.
    /// </summary>
    /// <remarks>
    /// Anthropic's prompt cache is explicit: without a breakpoint nothing is cached and a long
    /// system prompt is re-billed at full price on every turn (audit gap G-17). Marking is
    /// opt-in — a breakpoint changes what the vendor stores and how the call is billed.
    /// The API caps a request at <see cref="LlmCacheConfig.MaxBreakpoints"/> breakpoints and
    /// rejects the call beyond that, so the two this method can place are inherently within
    /// budget; the guard below states the invariant rather than defending against it.
    /// </remarks>
    private void ApplyCacheBreakpoints(Dictionary<string, object> payload, LlmCacheConfig? cache)
    {
        if (cache is null || !cache.RequestsAnyBreakpoint)
            return;

        var placed = 0;

        if (cache.CacheSystemPrompt && payload.TryGetValue("system", out var system) && system is string systemText)
        {
            // A breakpoint can only be attached to a content block, so the plain string form
            // is promoted to the single-block array form the API also accepts.
            payload["system"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "text",
                    ["text"] = systemText,
                    ["cache_control"] = BuildCacheControl(cache),
                },
            };
            placed++;
        }

        if (cache.CacheTools && placed < LlmCacheConfig.MaxBreakpoints)
            placed += MarkLastToolAsCacheable(payload, cache) ? 1 : 0;

        if (placed == 0)
            LogCacheRequestedButNothingToMark();
    }

    /// <summary>
    /// Attaches the breakpoint to the <em>last</em> tool: the marker caches everything up to
    /// and including the block it sits on, so one breakpoint covers the whole catalogue.
    /// </summary>
    private static bool MarkLastToolAsCacheable(Dictionary<string, object> payload, LlmCacheConfig cache)
    {
        if (!payload.TryGetValue("tools", out var toolsObj) || toolsObj is not IEnumerable<object> tools)
            return false;

        var toolList = tools.ToList();
        if (toolList.Count == 0)
            return false;

        // The formatter emits plain dictionaries; anything else is left untouched rather than
        // reshaped blindly.
        if (toolList[^1] is not IDictionary<string, object?> lastTool)
            return false;

        lastTool["cache_control"] = BuildCacheControl(cache);
        payload["tools"] = toolList;
        return true;
    }

    private static Dictionary<string, object?> BuildCacheControl(LlmCacheConfig cache)
    {
        var control = new Dictionary<string, object?> { ["type"] = "ephemeral" };
        if (!string.IsNullOrWhiteSpace(cache.Ttl))
            control["ttl"] = cache.Ttl;
        return control;
    }

    /// <summary>
    /// Translates <see cref="LlmThinkingConfig"/> into the Messages API dialect: a
    /// <c>thinking</c> block plus the effort level carried by <c>output_config</c>.
    /// </summary>
    /// <remarks>
    /// The <c>budget_tokens</c> shape found in many older sources is <em>rejected with an
    /// HTTP 400</em> by the current Claude generation, which decides its own budget from
    /// <c>type: "adaptive"</c> and the effort level. <see cref="LlmThinkingConfig.BudgetTokens"/>
    /// is therefore deliberately not mapped here, and is reported instead.
    /// </remarks>
    private void ApplyThinking(Dictionary<string, object> payload, LlmThinkingConfig? thinking)
    {
        if (thinking is null)
            return;

        if (thinking.Enabled.HasValue)
        {
            // "adaptive" lets the model decide per request; "disabled" turns it off outright.
            // Note that some models reject "disabled" at the highest effort levels with a 400 —
            // that is the vendor's rule to report, not ours to second-guess.
            payload["thinking"] = new Dictionary<string, object?>
            {
                ["type"] = thinking.Enabled.Value ? "adaptive" : "disabled",
            };
        }

        if (!string.IsNullOrWhiteSpace(thinking.Effort))
            MergeOutputConfig(payload, "effort", thinking.Effort);

        if (thinking.BudgetTokens.HasValue)
        {
            LogUnsupportedOption("thinking.budgetTokens",
                "the current Claude generation rejects budget_tokens with a 400 — set the effort level instead");
        }
    }

    /// <summary>
    /// Translates <see cref="LlmResponseFormat"/> into <c>output_config.format</c>, Anthropic's
    /// equivalent of <c>response_format</c>.
    /// </summary>
    /// <remarks>
    /// The object takes exactly two keys — <c>type</c>, which must be <c>"json_schema"</c>, and
    /// <c>schema</c>. There is **no schema-less JSON mode**: unlike the OpenAI dialect, Anthropic
    /// has no equivalent of <c>json_object</c>, so a bare "give me JSON" request cannot be
    /// expressed at the API level and is reported instead of being sent in a shape the API
    /// would reject. (<c>strict</c> exists on Anthropic, but on individual tools, not here.)
    /// </remarks>
    private void ApplyResponseFormat(Dictionary<string, object> payload, LlmResponseFormat? responseFormat)
    {
        if (responseFormat is null
            || string.IsNullOrWhiteSpace(responseFormat.Type)
            || string.Equals(responseFormat.Type, "text", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (responseFormat.Schema is not { } schema)
        {
            LogUnsupportedOption("response_format without a schema",
                "Anthropic constrains output only through a JSON Schema — supply one, or state the shape in the prompt");
            return;
        }

        MergeOutputConfig(payload, "format", new Dictionary<string, object?>
        {
            ["type"] = "json_schema",
            ["schema"] = JsonSerializer.Deserialize<JsonElement>(schema.Schema),
        });
    }

    /// <summary>
    /// Adds one key to the shared <c>output_config</c> object without clobbering the keys the
    /// other translators put there.
    /// </summary>
    private static void MergeOutputConfig(Dictionary<string, object> payload, string key, object? value)
    {
        if (payload.TryGetValue("output_config", out var existing)
            && existing is Dictionary<string, object?> outputConfig)
        {
            outputConfig[key] = value;
            return;
        }

        payload["output_config"] = new Dictionary<string, object?> { [key] = value };
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

        var baseUrl = (effectiveConfig.BaseUrl is null
                ? DefaultBaseUrl
                : effectiveConfig.BaseUrl.ToString()).TrimEnd('/');
        var endpoint = new Uri($"{baseUrl}/v1/messages");

        var messages = new List<object> { new { role = LlmRoles.User, content = prompt } };
        var payload = BuildRequestPayload(effectiveConfig, messages, null);

        // Add stream flag
        payload["stream"] = true;

        var json = JsonSerializer.Serialize(payload, JsonOptions);

        HttpResponseMessage? response = null;
        try
        {
            response = await SendStreamingRequestAsync(client, endpoint, json, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogStreamingError(response.StatusCode);
                throw await StreamingRejectionAsync(response, "Anthropic", cancellationToken)
                    .ConfigureAwait(false);
            }

            // Anthropic SSE uses event types: content_block_delta with delta.text
            await foreach (var data in ReadSseStreamAsync(response, cancellationToken).ConfigureAwait(false))
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("type", out var typeEl))
                {
                    var eventType = typeEl.GetString();
                    if (eventType == "content_block_delta" &&
                        doc.RootElement.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("text", out var textEl))
                    {
                        var token = textEl.GetString();
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
    /// Streams a multi-message chat completion over the Messages API's native SSE feed
    /// (G-20): visible text as <see cref="LlmStreamEventKind.ContentDelta"/>, the extended
    /// thinking trace as <see cref="LlmStreamEventKind.ReasoningDelta"/>, and a terminal
    /// <see cref="LlmStreamEventKind.Completed"/> whose response carries the assembled content
    /// and the final usage — cache counters included.
    /// </summary>
    /// <remarks>
    /// Anthropic previously fell back to the buffered emulation in
    /// <c>HttpLlmProviderBase.ChatStreamingAsync</c>, which waits for the whole answer before
    /// emitting anything: no token ever arrived early. The SSE parsing already existed for the
    /// text-only path; this extends it to typed events.
    /// </remarks>
    public override async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var effectiveConfig = config ?? Config;

#pragma warning disable CS0618 // Type or member is obsolete
        if (string.IsNullOrEmpty(effectiveConfig.ApiKey))
#pragma warning restore CS0618
        {
            yield return LlmStreamEvent.Complete(BuildApiKeyMissingResponse());
            yield break;
        }

        // Do NOT use 'using' — factory-managed clients must not be disposed.
        var client = CreateHttpClient(effectiveConfig);
        var baseUrl = (effectiveConfig.BaseUrl is null
                ? DefaultBaseUrl
                : effectiveConfig.BaseUrl.ToString()).TrimEnd('/');
        var endpoint = new Uri($"{baseUrl}/v1/messages");

        var (conversationMessages, systemMessage) = SeparateSystemMessages(messages, effectiveConfig);
        var payload = BuildRequestPayload(effectiveConfig, conversationMessages, systemMessage);
        payload["stream"] = true;
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        var state = new AnthropicStreamState();
        HttpResponseMessage? response = null;
        try
        {
            response = await SendStreamingRequestAsync(client, endpoint, json, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                LogStreamingError(response.StatusCode);
                yield return LlmStreamEvent.Complete(
                    BuildErrorResponse($"Anthropic API error: {response.StatusCode} - {error}", "APIError"));
                yield break;
            }

            await foreach (var data in ReadSseStreamAsync(response, cancellationToken).ConfigureAwait(false))
            {
                foreach (var ev in ParseStreamEvent(data, state))
                    yield return ev;
            }
        }
        finally
        {
            response?.Dispose();
        }

        yield return LlmStreamEvent.Complete(BuildStreamedResponse(state, effectiveConfig));
    }

    /// <summary>Accumulation state of one streamed Messages API response.</summary>
    private sealed class AnthropicStreamState
    {
        public StringBuilder Content { get; } = new();
        public StringBuilder Reasoning { get; } = new();
        public AnthropicUsage Usage { get; set; }
    }

    /// <summary>
    /// Parses one SSE frame into the delta events to emit, mutating <paramref name="state"/>.
    /// A malformed frame is skipped: one bad frame must not kill the stream.
    /// </summary>
    private static List<LlmStreamEvent> ParseStreamEvent(string data, AnthropicStreamState state)
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
            if (!root.TryGetProperty("type", out var typeEl))
                return events;

            switch (typeEl.GetString())
            {
                // Input usage (and the cache counters) arrive up front, output usage at the end.
                case "message_start" when root.TryGetProperty("message", out var message):
                    state.Usage = ReadUsage(message);
                    break;

                case "message_delta":
                    var delta = ReadUsage(root);
                    state.Usage = state.Usage with
                    {
                        OutputTokens = delta.OutputTokens != 0 ? delta.OutputTokens : state.Usage.OutputTokens,
                    };
                    break;

                case "content_block_delta" when root.TryGetProperty("delta", out var blockDelta):
                    AccumulateBlockDelta(blockDelta, state, events);
                    break;

                default:
                    break;
            }
        }

        return events;
    }

    private static void AccumulateBlockDelta(
        JsonElement delta, AnthropicStreamState state, List<LlmStreamEvent> events)
    {
        if (delta.TryGetProperty("text", out var textEl)
            && textEl.GetString() is { Length: > 0 } text)
        {
            state.Content.Append(text);
            events.Add(LlmStreamEvent.Content(text));
        }

        if (delta.TryGetProperty("thinking", out var thinkingEl)
            && thinkingEl.GetString() is { Length: > 0 } thinking)
        {
            state.Reasoning.Append(thinking);
            events.Add(LlmStreamEvent.Reasoning(thinking));
        }
    }

    /// <summary>
    /// Builds the terminal response of a streamed completion — same shape as the buffered
    /// <see cref="ParseResponse"/> result, so callers cannot tell the two paths apart.
    /// </summary>
    private LlmResponse BuildStreamedResponse(AnthropicStreamState state, LlmConfig config)
    {
        var metadata = LlmResponseMetadata.CreateBuilder()
            .AddProvider(Name)
            .Add("input_tokens", state.Usage.InputTokens)
            .Add("output_tokens", state.Usage.OutputTokens);

        if (state.Reasoning.Length > 0)
            metadata.Add("reasoning_content", state.Reasoning.ToString());
        if (state.Usage.CacheCreationTokens is { } created)
            metadata.Add("cache_creation_input_tokens", created);
        if (state.Usage.CacheReadTokens is { } read)
            metadata.Add("cache_read_input_tokens", read);

        return new LlmResponse
        {
            Content = state.Content.ToString(),
            TokensUsed = state.Usage.TotalTokens,
            PromptTokens = state.Usage.PromptTokens,
            CompletionTokens = state.Usage.OutputTokens,
            CacheHitTokens = state.Usage.CacheReadTokens,
            CacheMissTokens = state.Usage.CacheMissTokens,
            Model = config.Model,
            Metadata = metadata.Build().ToDictionary(),
        };
    }

    private LlmResponse ParseResponse(string responseJson, LlmConfig config)
    {
        using var doc = JsonDocument.Parse(responseJson);

        // Extract text content from content array
        var contentArray = doc.RootElement.GetProperty("content");
        var textContent = "";

        foreach (var item in contentArray.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var typeElement) &&
                typeElement.GetString() == "text" &&
                item.TryGetProperty("text", out var textElement))
            {
                textContent = textElement.GetString() ?? "";
                break;
            }
        }

        var usage = ReadUsage(doc.RootElement);

        var metadata = LlmResponseMetadata.CreateBuilder()
            .AddProvider(Name)
            .Add("input_tokens", usage.InputTokens)
            .Add("output_tokens", usage.OutputTokens);

        if (usage.CacheCreationTokens is { } created)
            metadata.Add("cache_creation_input_tokens", created);
        if (usage.CacheReadTokens is { } read)
            metadata.Add("cache_read_input_tokens", read);

        return new LlmResponse
        {
            Content = textContent,
            TokensUsed = usage.TotalTokens,
            PromptTokens = usage.PromptTokens,
            CompletionTokens = usage.OutputTokens,
            CacheHitTokens = usage.CacheReadTokens,
            CacheMissTokens = usage.CacheMissTokens,
            Model = config.Model,
            Metadata = metadata.Build().ToDictionary(),
            RawResponseBody = _toolCallingStrategy?.SupportsNativeToolCalling == true
                ? responseJson
                : null
        };
    }

    /// <summary>The Messages API usage block, including the two cache counters (G-21).</summary>
    /// <remarks>
    /// Anthropic reports three input figures that do not overlap: <c>input_tokens</c> counts
    /// only what was processed fresh and uncached, <c>cache_creation_input_tokens</c> what was
    /// written to the cache, and <c>cache_read_input_tokens</c> what was served from it. The
    /// prompt total is therefore their sum, and the "miss" side of Orkeon's typed ratio is
    /// everything that was not a read.
    /// </remarks>
    private readonly record struct AnthropicUsage(
        int InputTokens, int OutputTokens, int? CacheCreationTokens, int? CacheReadTokens)
    {
        public int PromptTokens => InputTokens + (CacheCreationTokens ?? 0) + (CacheReadTokens ?? 0);

        public int TotalTokens => PromptTokens + OutputTokens;

        /// <summary>Null when the response carried no cache counters at all — unmeasured, not zero.</summary>
        public int? CacheMissTokens =>
            CacheCreationTokens is null && CacheReadTokens is null
                ? null
                : InputTokens + (CacheCreationTokens ?? 0);
    }

    private static AnthropicUsage ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage))
            return default;

        return new AnthropicUsage(
            InputTokens: ReadInt(usage, "input_tokens") ?? 0,
            OutputTokens: ReadInt(usage, "output_tokens") ?? 0,
            CacheCreationTokens: ReadInt(usage, "cache_creation_input_tokens"),
            CacheReadTokens: ReadInt(usage, "cache_read_input_tokens"));
    }

    private static int? ReadInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var prop)
        && prop.ValueKind == JsonValueKind.Number
        && prop.TryGetInt32(out var value)
            ? value
            : null;

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Anthropic API Error ({StatusCode}): {Error}")]
    private partial void LogApiError(System.Net.HttpStatusCode statusCode, string error);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Anthropic API call failed")]
    private partial void LogApiCallFailed(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Anthropic streaming error: {StatusCode}")]
    private partial void LogStreamingError(System.Net.HttpStatusCode statusCode);

    [LoggerMessage(EventId = 110, Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "Option '{Option}' was declared but Anthropic does not support it — it was not sent. {Remedy}.")]
    private partial void LogUnsupportedOption(string option, string remedy);

    [LoggerMessage(EventId = 111, Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "Prompt caching was requested but there was nothing to mark: the request carries no system prompt and no tools. No cache_control breakpoint was placed, so nothing will be cached.")]
    private partial void LogCacheRequestedButNothingToMark();
}
