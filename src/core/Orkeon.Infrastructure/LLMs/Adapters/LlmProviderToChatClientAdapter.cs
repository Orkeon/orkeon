using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Orkeon.Application.Common.DTOs;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Infrastructure.LLMs.Adapters;

/// <summary>Adapts <see cref="ILlmProvider"/> to the <see cref="IChatClient"/> interface from Microsoft.Extensions.AI.</summary>
public sealed class LlmProviderToChatClientAdapter : IChatClient
{
    private readonly ILlmProvider _provider;
    private readonly LlmConfig? _baseLlmConfig;
    private readonly IToolCallParser? _textFallbackParser;
    private readonly IToolCallParser? _nativeToolCallParser;

    /// <summary>Initializes a new instance of <see cref="LlmProviderToChatClientAdapter"/>.</summary>
    /// <param name="provider">The underlying LLM provider.</param>
    /// <param name="baseLlmConfig">
    /// Optional base LLM configuration. When provided, <see cref="MapOptions"/> merges tool schemas
    /// into a copy of this config instead of creating a new one from scratch — preserving ApiKey,
    /// BaseUrl, and all other provider-specific settings.
    /// </param>
    /// <param name="textFallbackParser">
    /// Optional text-based tool call parser. When the LLM does not return native <c>tool_calls</c>
    /// in its response, this parser extracts tool invocations from the text content
    /// (e.g. <c>[TOOL_CALL]</c> blocks or XML <c>&lt;invoke&gt;</c> elements).
    /// </param>
    /// <param name="nativeToolCallParser">
    /// Optional provider-specific tool call parser. When set, <see cref="TryBuildNativeToolCallResponse"/>
    /// delegates to this parser instead of the built-in OpenAI-compatible <c>choices[0].message.tool_calls</c>
    /// extraction. Used for providers like Anthropic whose response format differs from OpenAI.
    /// </param>
    public LlmProviderToChatClientAdapter(
        ILlmProvider provider,
        LlmConfig? baseLlmConfig = null,
        IToolCallParser? textFallbackParser = null,
        IToolCallParser? nativeToolCallParser = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _baseLlmConfig = baseLlmConfig;
        _textFallbackParser = textFallbackParser;
        _nativeToolCallParser = nativeToolCallParser;
    }

    /// <inheritdoc />
    public ChatClientMetadata Metadata => new(nameof(LlmProviderToChatClientAdapter));

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return GetResponseCoreAsync();

        async Task<ChatResponse> GetResponseCoreAsync()
        {
            var llmMessages = MapMessages(messages);
            var config = MapOptions(options);

            var response = await _provider.ChatAsync(llmMessages, config, cancellationToken).ConfigureAwait(false);

            return TryBuildNativeToolCallResponse(response)
                ?? TryBuildTextFallbackToolCallResponse(response)
                ?? BuildPlainTextResponse(response);
        }
    }

    /// <summary>
    /// Attempts to build a <see cref="ChatResponse"/> from native tool calls
    /// present in the raw response body. When a provider-specific <see cref="_nativeToolCallParser"/>
    /// is configured (e.g. Anthropic), it is used instead of the built-in OpenAI-compatible parser.
    /// Returns <c>null</c> when no native tool calls are found.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Tolerant tool-call parsing: any failure parsing the provider-native (Anthropic) format is swallowed so the method falls through to OpenAI-format parsing.")]
    private ChatResponse? TryBuildNativeToolCallResponse(LlmResponse response)
    {
        if (string.IsNullOrEmpty(response.RawResponseBody))
            return null;

        // If a provider-specific parser was injected, use it
        if (_nativeToolCallParser != null)
        {
            return TryBuildNativeToolCallResponseViaParser(response);
        }

        // Auto-detect response format: Anthropic (content[].type) vs OpenAI (choices[].message.tool_calls)
        try
        {
            using var doc = JsonDocument.Parse(response.RawResponseBody);
            var root = doc.RootElement;

            // Anthropic format: root has "content" array (no "choices")
            if (root.TryGetProperty("content", out var contentArray) &&
                contentArray.ValueKind == JsonValueKind.Array &&
                !root.TryGetProperty("choices", out _))
            {
                var anthropicParser = new ToolCalling.AnthropicToolCallParser();
                var parsed = anthropicParser.ParseToolCalls(root);
                if (parsed.Count == 0)
                    return null;

                return BuildToolCallResponse(
                    response,
                    parsed.Select(tc => new FunctionCallContent(tc.Id, tc.ToolName, tc.Arguments)));
            }
        }
        catch
        {
            // Fall through to OpenAI parsing
        }

        // OpenAI format: choices[0].message.tool_calls
        var toolCalls = ParseToolCallsFromRawResponse(response.RawResponseBody);
        if (toolCalls.Count == 0)
            return null;

        return BuildToolCallResponse(
            response,
            toolCalls.Select(tc => new FunctionCallContent(tc.CallId, tc.Name, tc.Arguments)));
    }

    /// <summary>
    /// Parses native tool calls using the injected <see cref="_nativeToolCallParser"/>
    /// (provider-specific format, e.g. Anthropic <c>content[].type==tool_use</c>).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Tolerant tool-call parsing: any failure in the injected native parser is swallowed and treated as 'no tool calls' (null) rather than propagated.")]
    private ChatResponse? TryBuildNativeToolCallResponseViaParser(LlmResponse response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response.RawResponseBody!);
            var parsed = _nativeToolCallParser!.ParseToolCalls(doc.RootElement);
            if (parsed.Count == 0)
                return null;

            return BuildToolCallResponse(
                response,
                parsed.Select(tc => new FunctionCallContent(tc.Id, tc.ToolName, tc.Arguments)));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Attempts to build a <see cref="ChatResponse"/> from text-based tool call patterns
    /// (<c>[TOOL_CALL]</c> / <c>&lt;invoke&gt;</c>) using the fallback parser.
    /// Returns <c>null</c> when the fallback parser is not configured or yields no calls.
    /// </summary>
    private ChatResponse? TryBuildTextFallbackToolCallResponse(LlmResponse response)
    {
        if (_textFallbackParser == null || string.IsNullOrEmpty(response.RawResponseBody))
            return null;

        var textToolCalls = ParseTextFallbackToolCalls(response.RawResponseBody);
        if (textToolCalls.Count == 0)
            return null;

        return BuildToolCallResponse(
            response,
            textToolCalls.Select(tc => new FunctionCallContent(tc.Id, tc.ToolName, tc.Arguments)));
    }

    /// <summary>
    /// Builds a <see cref="ChatResponse"/> that combines optional text content with the
    /// provided function-call contents.
    /// </summary>
    private static ChatResponse BuildToolCallResponse(
        LlmResponse response,
        IEnumerable<FunctionCallContent> functionCalls)
    {
        var contents = new List<AIContent>();
        if (!string.IsNullOrEmpty(response.Content))
            contents.Add(new TextContent(response.Content));
        contents.AddRange(functionCalls);

        var message = new ChatMessage(ChatRole.Assistant, contents);
        AttachReasoningContent(message, response);

        return new ChatResponse(message)
        {
            ModelId = response.Model,
            Usage = BuildUsageDetails(response)
        };
    }

    /// <summary>Builds a plain text <see cref="ChatResponse"/> when no tool calls are present.</summary>
    private static ChatResponse BuildPlainTextResponse(LlmResponse response)
    {
        var message = new ChatMessage(ChatRole.Assistant, response.Content);
        AttachReasoningContent(message, response);

        return new ChatResponse(message)
        {
            ModelId = response.Model,
            Usage = BuildUsageDetails(response)
        };
    }

    /// <summary>
    /// Builds <see cref="UsageDetails"/> from an <see cref="LlmResponse"/>, mapping prompt /
    /// completion token counts onto the M.E.AI typed fields and stashing DeepSeek-style prompt
    /// cache hit/miss counts under <see cref="UsageDetails.AdditionalCounts"/> so the
    /// orchestrator can surface them in AUTO_SUMMARY. Experiment 07 friction #5.
    /// </summary>
    private static UsageDetails BuildUsageDetails(LlmResponse response)
    {
        var usage = new UsageDetails
        {
            TotalTokenCount = response.TokensUsed,
            InputTokenCount = response.PromptTokens,
            OutputTokenCount = response.CompletionTokens,
        };

        if (response.CacheHitTokens.HasValue || response.CacheMissTokens.HasValue)
        {
            usage.AdditionalCounts ??= new AdditionalPropertiesDictionary<long>();
            if (response.CacheHitTokens.HasValue)
                usage.AdditionalCounts[LlmUsageMetadataKeys.CacheHitTokens] = response.CacheHitTokens.Value;
            if (response.CacheMissTokens.HasValue)
                usage.AdditionalCounts[LlmUsageMetadataKeys.CacheMissTokens] = response.CacheMissTokens.Value;
        }

        return usage;
    }

    /// <summary>
    /// Propagates a thinking-mode <c>reasoning_content</c> trace from the provider response
    /// into the M.E.AI <see cref="ChatMessage.AdditionalProperties"/> bag so it survives the
    /// next round-trip through <see cref="MapSingleMessage"/>. Required by DeepSeek (and any
    /// provider that demands the reasoning trace be replayed on subsequent turns).
    /// </summary>
    private static void AttachReasoningContent(ChatMessage message, LlmResponse response)
    {
        if (response.Metadata is null) return;
        if (!response.Metadata.TryGetValue(ReasoningContentMetadataKey, out var raw)) return;
        if (raw is not string s || string.IsNullOrEmpty(s)) return;

        message.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        message.AdditionalProperties[ReasoningContentMetadataKey] = s;
    }

    private const string ReasoningContentMetadataKey = "reasoning_content";

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // If the underlying provider supports streaming, use it
        if (_provider is IStreamingLlmProvider streamingProvider && streamingProvider.SupportsStreaming)
        {
            var prompt = string.Join("\n", messages.Select(m =>
                string.Join("", m.Contents.OfType<TextContent>().Select(t => t.Text))));
            var config = MapOptions(options);

            await foreach (var chunk in streamingProvider.GenerateStreamingAsync(prompt, config, cancellationToken).ConfigureAwait(false))
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            }
        }
        else
        {
            // Fallback: execute non-streaming and return complete result as single chunk
            var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text ?? string.Empty)
            {
                ModelId = response.ModelId
            };
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(IChatClient)) return this;
        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        // No resources to dispose — underlying ILlmProvider manages its own lifecycle
    }

    // ─────────────────────────────────────────────────────────────
    //  Message conversion: M.E.AI ChatMessage ↔ Domain LlmMessage
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts M.E.AI ChatMessages to LlmMessages, handling FunctionCallContent
    /// (assistant tool calls) and FunctionResultContent (tool results with call IDs).
    /// </summary>
    private static LlmMessage[] MapMessages(IEnumerable<ChatMessage> messages)
    {
        var result = new List<LlmMessage>();
        foreach (var m in messages)
        {
            // A single ChatMessage may contain multiple FunctionResultContent items
            // (one per tool result). Each must become a separate LlmMessage with role "tool".
            var functionResults = m.Contents.OfType<FunctionResultContent>().ToList();
            if (functionResults.Count > 0)
            {
                foreach (var fr in functionResults)
                {
                    result.Add(new LlmMessage
                    {
                        Role = "tool",
                        Content = fr.Result?.ToString() ?? string.Empty,
                        ToolCallId = fr.CallId ?? string.Empty
                    });
                }
                continue;
            }

            result.Add(MapSingleMessage(m));
        }
        return result.ToArray();
    }

    private static LlmMessage MapSingleMessage(ChatMessage m)
    {
        // Pull the reasoning trace (if any) out of the M.E.AI AdditionalProperties bag so
        // thinking-mode providers (DeepSeek) can replay it on subsequent turns.
        var reasoningContent = ExtractReasoningContent(m);

        // Handle assistant messages that contain FunctionCallContent (tool calls from the LLM)
        var functionCalls = m.Contents.OfType<FunctionCallContent>().ToList();
        if (functionCalls.Count > 0)
        {
            // Serialize tool calls back to OpenAI-compatible JSON so the provider can
            // include them in the conversation history on subsequent turns.
            var toolCallsJson = JsonSerializer.Serialize(
                functionCalls.Select(fc => new
                {
                    id = fc.CallId,
                    type = "function",
                    function = new
                    {
                        name = fc.Name,
                        arguments = fc.Arguments != null
                            ? JsonSerializer.Serialize(fc.Arguments)
                            : "{}"
                    }
                }));

            var textContent = string.Join("", m.Contents.OfType<TextContent>().Select(t => t.Text));

            return new LlmMessage
            {
                Role = "assistant",
                Content = textContent,
                RawToolCalls = toolCallsJson,
                ReasoningContent = reasoningContent
            };
        }

        // Standard text messages
        var text = string.Join("", m.Contents.OfType<TextContent>().Select(t => t.Text));
        var built = BuildRoleMessage(m.Role, text);
        return reasoningContent is null ? built : built with { ReasoningContent = reasoningContent };
    }

    private static string? ExtractReasoningContent(ChatMessage m)
    {
        if (m.AdditionalProperties is null) return null;
        if (!m.AdditionalProperties.TryGetValue(ReasoningContentMetadataKey, out var raw)) return null;
        return raw as string;
    }

    /// <summary>Creates an <see cref="LlmMessage"/> with the role factory matching the ChatRole.</summary>
    private static LlmMessage BuildRoleMessage(ChatRole role, string text) => role switch
    {
        var r when r == ChatRole.System => LlmMessage.System(text),
        var r when r == ChatRole.User => LlmMessage.User(text),
        _ => LlmMessage.Assistant(text)
    };

    // ─────────────────────────────────────────────────────────────
    //  Options conversion: ChatOptions → LlmConfig
    //  FIX P0-TC-FIX-01: propagate tools from ChatOptions to LlmConfig
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts M.E.AI <see cref="ChatOptions"/> to a domain <see cref="LlmConfig"/>,
    /// merging tool schemas and sampling parameters into the base config (preserving ApiKey, BaseUrl, etc.).
    /// Returns <c>null</c> when no meaningful overrides exist — the provider then uses its own config.
    /// </summary>
    private LlmConfig? MapOptions(ChatOptions? options)
    {
        if (options == null) return null;

        var (tools, toolMode) = ExtractToolsFromOptions(options);

        // If we have a base config, merge sampling + tools into it (preserves ApiKey, BaseUrl, etc.)
        if (_baseLlmConfig != null)
        {
            return ApplyOptionsOverrides(_baseLlmConfig, options, tools, toolMode);
        }

        // No base config: build a fresh LlmConfig only when ChatOptions has meaningful overrides.
        if (!HasMeaningfulOverrides(options, tools))
            return null;

        var baseConfig = LlmConfig.Create(options.ModelId ?? LlmDefaults.DefaultModelName);
        return ApplyOptionsOverrides(baseConfig, options, tools, toolMode);
    }

    /// <summary>
    /// Extracts the Orkeon tool schemas and tool mode from <see cref="ChatOptions.AdditionalProperties"/>,
    /// which are populated by <c>ExecutionOrchestrator.BuildChatOptions</c>.
    /// </summary>
    private static (IReadOnlyList<ToolSchema>? Tools, ToolCallMode Mode) ExtractToolsFromOptions(ChatOptions options)
    {
        if (options.AdditionalProperties == null)
            return (null, ToolCallMode.Auto);

        IReadOnlyList<ToolSchema>? tools = null;
        var toolMode = ToolCallMode.Auto;

        if (options.AdditionalProperties.TryGetValue("orkeon:tool_schemas", out var toolsObj)
            && toolsObj is IReadOnlyList<ToolSchema> schemas)
        {
            tools = schemas;
        }

        if (options.AdditionalProperties.TryGetValue("orkeon:tool_mode", out var modeObj)
            && modeObj is ToolCallMode mode)
        {
            toolMode = mode;
        }

        return (tools, toolMode);
    }

    /// <summary>
    /// Reads the optional GBNF grammar from <see cref="ChatOptions.AdditionalProperties"/>
    /// (populated by <c>ExecutionOrchestrator</c> when the task declares a
    /// <c>structured_output</c> deliverable).
    /// </summary>
    private static string? ExtractGrammarFromOptions(ChatOptions options)
    {
        if (options.AdditionalProperties == null) return null;
        if (options.AdditionalProperties.TryGetValue("orkeon:grammar_gbnf", out var g) && g is string gs && !string.IsNullOrWhiteSpace(gs))
            return gs;
        return null;
    }

    /// <summary>
    /// Returns <c>true</c> when the <see cref="ChatOptions"/> instance carries at least one override
    /// worth propagating to a fresh <see cref="LlmConfig"/>.
    /// </summary>
    private static bool HasMeaningfulOverrides(ChatOptions options, IReadOnlyList<ToolSchema>? tools)
    {
        return options.ModelId != null
            || options.Temperature.HasValue
            || options.MaxOutputTokens.HasValue
            || options.TopP.HasValue
            || options.FrequencyPenalty.HasValue
            || options.PresencePenalty.HasValue
            || options.Seed.HasValue
            || (tools != null && tools.Count > 0)
            || ExtractGrammarFromOptions(options) != null
            || ExtractThinkingFromOptions(options) != null
            || ExtractResponseFormatFromOptions(options) != null;
    }

    /// <summary>
    /// Returns a copy of <paramref name="baseConfig"/> with any explicit overrides from
    /// <paramref name="options"/> (Model, Temperature, MaxTokens, …) and the extracted tool
    /// schemas applied. Unset values in <paramref name="options"/> leave the base config
    /// field untouched.
    /// </summary>
    private static LlmConfig ApplyOptionsOverrides(
        LlmConfig baseConfig,
        ChatOptions options,
        IReadOnlyList<ToolSchema>? tools,
        ToolCallMode toolMode)
    {
        return baseConfig with
        {
            // Experiment 07 friction #7: per-agent llm.model override was previously dropped
            // because this method ignored options.ModelId. Apply it now so the YAML override
            // reaches the provider.
            Model = !string.IsNullOrWhiteSpace(options.ModelId) ? options.ModelId : baseConfig.Model,
            Temperature = options.Temperature ?? baseConfig.Temperature,
            MaxTokens = options.MaxOutputTokens ?? baseConfig.MaxTokens,
            TopP = options.TopP ?? baseConfig.TopP,
            FrequencyPenalty = options.FrequencyPenalty ?? baseConfig.FrequencyPenalty,
            PresencePenalty = options.PresencePenalty ?? baseConfig.PresencePenalty,
            Seed = options.Seed.HasValue ? (int)options.Seed.Value : baseConfig.Seed,
            Tools = tools ?? baseConfig.Tools,
            ToolMode = toolMode,
            GrammarGbnf = ExtractGrammarFromOptions(options) ?? baseConfig.GrammarGbnf,
            Thinking = ExtractThinkingFromOptions(options) ?? baseConfig.Thinking,
            ResponseFormat = ExtractResponseFormatFromOptions(options) ?? baseConfig.ResponseFormat
        };
    }

    /// <summary>
    /// Reads the optional <see cref="LlmThinkingConfig"/> stashed by
    /// <c>ExecutionOrchestrator.ApplyAgentLlmOverrides</c> under
    /// <see cref="LlmChatOptionsKeys.Thinking"/>.
    /// </summary>
    private static LlmThinkingConfig? ExtractThinkingFromOptions(ChatOptions options)
    {
        if (options.AdditionalProperties == null) return null;
        if (!options.AdditionalProperties.TryGetValue(LlmChatOptionsKeys.Thinking, out var raw)) return null;
        return raw as LlmThinkingConfig;
    }

    /// <summary>
    /// Reads the optional <see cref="LlmResponseFormat"/> stashed by
    /// <c>ExecutionOrchestrator.ApplyAgentLlmOverrides</c> / <c>ApplyTaskLlmOverrides</c>
    /// under <see cref="LlmChatOptionsKeys.ResponseFormat"/>.
    /// </summary>
    private static LlmResponseFormat? ExtractResponseFormatFromOptions(ChatOptions options)
    {
        if (options.AdditionalProperties == null) return null;
        if (!options.AdditionalProperties.TryGetValue(LlmChatOptionsKeys.ResponseFormat, out var raw)) return null;
        return raw as LlmResponseFormat;
    }

    // ─────────────────────────────────────────────────────────────
    //  Text-based fallback: parse [TOOL_CALL] / <invoke> patterns
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses text-based tool call patterns from the raw JSON response body
    /// using the injected <see cref="IToolCallParser"/> fallback.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Tolerant tool-call parsing: a response that is not valid JSON or that the fallback parser rejects yields an empty list (no fallback) rather than propagating.")]
    private IReadOnlyList<Application.Interfaces.LLM.ParsedToolCall> ParseTextFallbackToolCalls(string rawResponseBody)
    {
        if (_textFallbackParser == null)
            return [];

        try
        {
            using var doc = JsonDocument.Parse(rawResponseBody);
            return _textFallbackParser.ParseToolCalls(doc.RootElement);
        }
        catch
        {
            // If raw response is not valid JSON, return empty — no fallback possible.
            return [];
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Tool call parsing from raw OpenAI-compatible JSON response
    //  FIX P0-TC-FIX-02: convert tool_calls → FunctionCallContent
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses tool calls from a raw OpenAI-compatible JSON response body.
    /// Handles the standard format: <c>choices[0].message.tool_calls[]</c>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Tolerant tool-call parsing: an invalid or unexpected OpenAI-format response yields an empty list so the caller falls back to the text content path.")]
    private static List<ParsedToolCall> ParseToolCallsFromRawResponse(string rawResponseBody)
    {
        var result = new List<ParsedToolCall>();

        try
        {
            using var doc = JsonDocument.Parse(rawResponseBody);
            if (!TryGetToolCallsElement(doc.RootElement, out var toolCalls))
                return result;

            foreach (var tc in toolCalls.EnumerateArray())
            {
                var parsed = TryParseSingleToolCall(tc);
                if (parsed != null)
                    result.Add(parsed);
            }
        }
        catch
        {
            // If raw response is not valid JSON or doesn't match expected format,
            // return empty list — caller will fall back to text content path.
        }

        return result;
    }

    /// <summary>
    /// Navigates <c>choices[0].message.tool_calls</c> from the JSON root element.
    /// Returns <c>false</c> when any intermediate property is missing.
    /// </summary>
    private static bool TryGetToolCallsElement(JsonElement root, out JsonElement toolCalls)
    {
        toolCalls = default;

        if (!root.TryGetProperty("choices", out var choices)) return false;
        if (choices.GetArrayLength() == 0) return false;

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var message)) return false;
        if (!message.TryGetProperty("tool_calls", out toolCalls)) return false;

        return true;
    }

    /// <summary>
    /// Parses a single <c>tool_calls[i]</c> JSON element into a <see cref="ParsedToolCall"/>.
    /// Returns <c>null</c> when the element lacks a function name or a <c>function</c> node.
    /// </summary>
    private static ParsedToolCall? TryParseSingleToolCall(JsonElement tc)
    {
        var callId = ExtractCallId(tc);

        if (!tc.TryGetProperty("function", out var function))
            return null;

        var name = function.TryGetProperty("name", out var nameProp)
            ? nameProp.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrEmpty(name))
            return null;

        var arguments = ExtractToolArguments(function);
        return new ParsedToolCall(callId, name, arguments);
    }

    /// <summary>
    /// Reads the <c>id</c> property from a tool_call element, falling back to a generated GUID.
    /// </summary>
    private static string ExtractCallId(JsonElement tc)
    {
        return tc.TryGetProperty("id", out var idProp)
            ? idProp.GetString() ?? $"call_{Guid.NewGuid():N}"
            : $"call_{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Parses the <c>arguments</c> property from a <c>function</c> node into a dictionary.
    /// Supports both the string form (serialized JSON) and the inline object form.
    /// Falls back to a single <c>_raw</c> entry when parsing fails.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Tolerant argument parsing: if the tool-call arguments are not valid JSON they are preserved verbatim under a '_raw' key rather than failing the call.")]
    private static Dictionary<string, object?>? ExtractToolArguments(JsonElement function)
    {
        if (!function.TryGetProperty("arguments", out var argsProp))
            return [];

        var argsStr = argsProp.ValueKind == JsonValueKind.String
            ? argsProp.GetString()
            : argsProp.GetRawText();

        if (string.IsNullOrEmpty(argsStr))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(argsStr);
            return Serialization.JsonElementConverter.ToDict(doc.RootElement);
        }
        catch
        {
            // If argument parsing fails, pass raw string as single argument
            return new Dictionary<string, object?> { ["_raw"] = argsStr };
        }
    }

    /// <summary>Represents a parsed tool call extracted from the LLM response.</summary>
    private sealed record ParsedToolCall(
        string CallId,
        string Name,
        Dictionary<string, object?>? Arguments);
}
