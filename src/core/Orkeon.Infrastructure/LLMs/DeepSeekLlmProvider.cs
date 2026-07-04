using Microsoft.Extensions.Logging;
using Polly;
using System.Text.Json;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// DeepSeek LLM provider implementation.
/// Supports DeepSeek-V3 (general) and DeepSeek-R1 (reasoning with reasoning_content).
/// </summary>
public partial class DeepSeekLlmProvider : OpenAICompatibleProviderBase
{
    /// <inheritdoc />
    public override string Name => "deepseek";

    /// <inheritdoc />
    protected override Uri DefaultBaseUrl => new(LlmEndpoints.DeepSeek);

    /// <inheritdoc />
    protected override string DefaultModel => ProviderDefaults.DeepSeekDefaults.DefaultModel;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "DeepSeek";

    /// <summary>Initializes a new instance of <see cref="DeepSeekLlmProvider"/>.</summary>
    public DeepSeekLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<DeepSeekLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>Constructor overload that accepts an optional resilience policy for testing.</summary>
    public DeepSeekLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<DeepSeekLlmProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Constructor overload that accepts a tool calling strategy.</summary>
    public DeepSeekLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<DeepSeekLlmProvider>? logger = null)
        : base(config, httpClientFactory, toolCallingStrategy, logger)
    {
    }

    /// <summary>
    /// Extracts DeepSeek-specific metadata, including reasoning_content for R1 models.
    /// </summary>
    protected override void ExtractResponseMetadata(JsonDocument doc, LlmResponseMetadata.Builder metadata)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(metadata);
        ExtractReasoningContent(doc.RootElement, metadata);
        ExtractUsageMetadata(doc.RootElement, metadata);
    }

    /// <summary>
    /// Attaches DeepSeek-V4 <c>thinking</c> block + <c>reasoning_effort</c> hint and the
    /// <c>response_format</c> output constraint when the effective config declares them.
    /// Experiment 07 friction #4 (thinking) + LLM Response Format feature (json_object).
    /// </summary>
    protected override void ApplyProviderSpecificOptions(Dictionary<string, object> payload, LlmConfig effectiveConfig)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(effectiveConfig);
        ApplyThinkingOptions(payload, effectiveConfig.Thinking);
        ApplyResponseFormatOption(payload, effectiveConfig.ResponseFormat);
    }

    private static void ApplyThinkingOptions(Dictionary<string, object> payload, LlmThinkingConfig? thinking)
    {
        if (thinking is null) return;

        if (thinking.Enabled.HasValue)
        {
            payload["thinking"] = new Dictionary<string, object?>
            {
                ["type"] = thinking.Enabled.Value ? "enabled" : "disabled",
            };
        }

        if (!string.IsNullOrWhiteSpace(thinking.Effort))
        {
            payload["reasoning_effort"] = thinking.Effort;
        }
    }

    private void ApplyResponseFormatOption(Dictionary<string, object> payload, LlmResponseFormat? responseFormat)
    {
        if (responseFormat is null
            || string.IsNullOrWhiteSpace(responseFormat.Type)
            || string.Equals(responseFormat.Type, "text", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        payload["response_format"] = new Dictionary<string, object?>
        {
            ["type"] = responseFormat.Type,
        };

        WarnIfPromptDoesNotMentionJson(payload);
    }

    /// <summary>
    /// DeepSeek's <c>response_format: json_object</c> requires the prompt (system or user)
    /// to mention "json" — otherwise the API may emit an infinite whitespace stream until
    /// <c>max_tokens</c>. We do NOT mutate the prompt; we surface a structured warning so
    /// the caller can fix the contract upstream.
    /// </summary>
    private void WarnIfPromptDoesNotMentionJson(Dictionary<string, object> payload)
    {
        if (!payload.TryGetValue("messages", out var messagesObj) || messagesObj is null)
        {
            return;
        }

        if (MessagesMentionJson(messagesObj))
        {
            return;
        }

        LogMissingJsonKeyword();
    }

    private static bool MessagesMentionJson(object messagesObj)
    {
        if (messagesObj is System.Collections.IEnumerable messages)
        {
            return messages.Cast<object?>().Any(MessageMentionsJson);
        }
        return false;
    }

    private static bool MessageMentionsJson(object? msg)
    {
        if (msg is null) return false;

        // Chat path: Dictionary<string, object?> with "role" / "content" keys.
        if (msg is IDictionary<string, object?> dict)
        {
            if (dict.TryGetValue("role", out var roleObj) && roleObj is string role
                && (role.Equals("system", StringComparison.OrdinalIgnoreCase)
                    || role.Equals("user", StringComparison.OrdinalIgnoreCase))
                && dict.TryGetValue("content", out var contentObj) && contentObj is string content)
            {
                return content.Contains("json", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        // Generate path: anonymous type { role, content } accessed via reflection.
        var type = msg.GetType();
        var roleProp = type.GetProperty("role");
        var contentProp = type.GetProperty("content");
        if (roleProp?.GetValue(msg) is string anonRole
            && (anonRole.Equals("system", StringComparison.OrdinalIgnoreCase)
                || anonRole.Equals("user", StringComparison.OrdinalIgnoreCase))
            && contentProp?.GetValue(msg) is string anonContent)
        {
            return anonContent.Contains("json", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Warning,
        Message = "DeepSeek response_format=json_object is set but no system/user message contains the word 'json'. The API may emit an infinite whitespace stream until max_tokens. Add 'json' to the prompt to be safe.")]
    private partial void LogMissingJsonKeyword();

    /// <summary>
    /// DeepSeek thinking-mode models (deepseek-v4-flash, deepseek-v4-pro, deepseek-reasoner)
    /// require the previous assistant turn's <c>reasoning_content</c> to be replayed verbatim
    /// on every subsequent request. Without it the API returns:
    ///   <c>HTTP 400: "The reasoning_content in the thinking mode must be passed back to the API."</c>
    /// This override re-emits it on the wire when the source <see cref="LlmMessage"/> carries it.
    /// </summary>
    protected override void EnrichAssistantMessage(
        Dictionary<string, object?> messageDict,
        Orkeon.Domain.SharedKernel.ValueObjects.LlmMessage source)
    {
        ArgumentNullException.ThrowIfNull(messageDict);
        ArgumentNullException.ThrowIfNull(source);
        if (!string.IsNullOrEmpty(source.ReasoningContent))
        {
            messageDict["reasoning_content"] = source.ReasoningContent;
        }
    }

    private static void ExtractReasoningContent(JsonElement root, LlmResponseMetadata.Builder metadata)
    {
        if (!root.TryGetProperty("choices", out var choices))
            return;

        var firstChoice = choices.EnumerateArray().FirstOrDefault();
        if (firstChoice.ValueKind == JsonValueKind.Undefined)
            return;

        if (!firstChoice.TryGetProperty("message", out var message))
            return;

        // DeepSeek-R1 includes reasoning_content alongside content
        if (message.TryGetProperty("reasoning_content", out var reasoning)
            && reasoning.GetString() is { Length: > 0 } reasoningText)
        {
            metadata.Add("reasoning_content", reasoningText);
        }
    }

    private static void ExtractUsageMetadata(JsonElement root, LlmResponseMetadata.Builder metadata)
    {
        if (!root.TryGetProperty("usage", out var usage))
            return;

        if (usage.TryGetProperty("prompt_tokens", out var promptTokens))
            metadata.Add("prompt_tokens", promptTokens.GetInt32());
        if (usage.TryGetProperty("completion_tokens", out var completionTokens))
            metadata.Add("completion_tokens", completionTokens.GetInt32());

        // DeepSeek context caching — billed at 1/10 of the input price.
        // Auto-populated by the API when the prompt prefix matches a recent request.
        if (usage.TryGetProperty("prompt_cache_hit_tokens", out var cacheHit))
            metadata.Add("prompt_cache_hit_tokens", cacheHit.GetInt32());
        if (usage.TryGetProperty("prompt_cache_miss_tokens", out var cacheMiss))
            metadata.Add("prompt_cache_miss_tokens", cacheMiss.GetInt32());
    }
}
