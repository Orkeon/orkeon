using System.Globalization;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Represents a response from a language model.
/// </summary>
public sealed record LlmResponse
{
    /// <summary>Gets the generated text content.</summary>
    public string Content { get; init; } = string.Empty;
    /// <summary>Gets the total number of tokens used.</summary>
    public int TokensUsed { get; init; }
    /// <summary>Gets the number of prompt tokens consumed.</summary>
    public int? PromptTokens { get; init; }
    /// <summary>Gets the number of completion tokens generated.</summary>
    public int? CompletionTokens { get; init; }
    /// <summary>Gets the number of prompt tokens that hit the provider's prompt cache (DeepSeek's <c>prompt_cache_hit_tokens</c>).</summary>
    public int? CacheHitTokens { get; init; }
    /// <summary>Gets the number of prompt tokens that missed the provider's prompt cache (DeepSeek's <c>prompt_cache_miss_tokens</c>).</summary>
    public int? CacheMissTokens { get; init; }
    /// <summary>
    /// Gets the normalized prompt-cache hit ratio — <c>hit / (hit + miss)</c> per DeepSeek guideline §6.5 —
    /// or <see langword="null"/> when the provider reports no cache token breakdown. Monitor this:
    /// &gt; 0.8 means a well-structured, prefix-stable multi-turn agent; &lt; 0.2 means the prompt prefix
    /// is being rebuilt each turn (timestamp/GUID in the prefix, reordered tools, mutated history).
    /// </summary>
    public double? CacheHitRatio =>
        CacheHitTokens is { } hit && CacheMissTokens is { } miss && (hit + miss) > 0
            ? (double)hit / (hit + miss)
            : null;
    /// <summary>Gets the cost.</summary>
    public double Cost { get; init; }
    /// <summary>Gets the model identifier that generated the response.</summary>
    public string? Model { get; init; }
    /// <summary>Gets additional metadata about the response.</summary>
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();

    /// <summary>Gets the raw JSON response body for tool call parsing.</summary>
    public string? RawResponseBody { get; init; }
}

/// <summary>
/// Strongly typed representation of a function call in an LLM message.
/// </summary>
public sealed class FunctionCallInfo
{
    /// <summary>
    /// The name of the function to call.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The arguments for the function call.
    /// </summary>
    public Dictionary<string, object> Arguments { get; }

    /// <summary>Initializes a new <see cref="FunctionCallInfo"/>.</summary>
    /// <param name="name">The function name to call.</param>
    /// <param name="arguments">The function call arguments, or <see langword="null"/> for none.</param>
    public FunctionCallInfo(string name, Dictionary<string, object>? arguments = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Arguments = arguments ?? [];
    }

    /// <summary>
    /// Gets a typed argument value.
    /// </summary>
    public T? GetArgument<T>(string key)
    {
        if (!Arguments.TryGetValue(key, out var value))
            return default;

        if (value is T typed)
            return typed;

        try
        {
            return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            return default;
        }
    }

    /// <summary>
    /// Converts to a dictionary for backward compatibility.
    /// </summary>
    public Dictionary<string, object> ToDictionary()
    {
        var result = new Dictionary<string, object>
        {
            ["name"] = Name,
            ["arguments"] = Arguments
        };
        return result;
    }

    /// <summary>
    /// Creates a FunctionCallInfo from a dictionary (backward compatibility).
    /// </summary>
    public static FunctionCallInfo? FromDictionary(Dictionary<string, object>? dictionary)
    {
        if (dictionary == null || dictionary.Count == 0)
            return null;

        var name = dictionary.TryGetValue("name", out var nameObj) && nameObj is string nameStr
            ? nameStr
            : string.Empty;

        Dictionary<string, object>? arguments = null;
        if (dictionary.TryGetValue("arguments", out var argsObj))
        {
            if (argsObj is Dictionary<string, object> argsDict)
            {
                arguments = argsDict;
            }
            else if (argsObj is IDictionary<string, object> iDict)
            {
                arguments = new Dictionary<string, object>(iDict);
            }
        }

        return new FunctionCallInfo(name, arguments);
    }
}

/// <summary>
/// Represents a message in a conversation with a language model.
/// </summary>
public sealed record LlmMessage
{
    /// <summary>Gets the role of the message sender (e.g., "user", "assistant", "system").</summary>
    public string Role { get; init; } = string.Empty;
    /// <summary>Gets the text content of the message.</summary>
    public string Content { get; init; } = string.Empty;
    /// <summary>Gets the optional name of the message sender.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Strongly typed function call information.
    /// </summary>
    public FunctionCallInfo? FunctionCallInfo { get; init; }

    /// <summary>Gets tool call-related metadata for multi-turn tool calling.</summary>
    public string? ToolCallId { get; init; }

    /// <summary>Gets the raw tool calls JSON for assistant messages with native tool calling.</summary>
    public string? RawToolCalls { get; init; }

    /// <summary>
    /// Gets the reasoning trace produced by thinking-mode models (e.g. DeepSeek R1, DeepSeek V4 in
    /// thinking mode). Providers that surface a separate <c>reasoning_content</c> field require it
    /// to be replayed on subsequent assistant messages — without it the API rejects the next turn
    /// with an HTTP 400 (<c>"reasoning_content in the thinking mode must be passed back"</c>).
    /// Only emitted on the wire when the provider explicitly opts in (e.g. <c>DeepSeekLlmProvider</c>).
    /// </summary>
    public string? ReasoningContent { get; init; }

    /// <summary>
    /// Gets the optional structured multi-modal content (text/image parts) of the message.
    /// When set, vision-capable providers (Anthropic, OpenAI) compose structured payloads
    /// (image content blocks / <c>image_url</c> parts) from these parts. <see cref="Content"/>
    /// remains the text-only fallback used by providers without vision support.
    /// </summary>
    public MultiModalContent? MultiModalContent { get; init; }

    /// <summary>
    /// Creates a system message.
    /// </summary>
    public static LlmMessage System(string content) => new() { Role = LlmRoles.System, Content = content };

    /// <summary>
    /// Creates a user message.
    /// </summary>
    public static LlmMessage User(string content) => new() { Role = LlmRoles.User, Content = content };

    /// <summary>
    /// Creates a user message carrying structured multi-modal content (text and image parts).
    /// <see cref="Content"/> is set to the concatenated text parts so providers without vision
    /// support degrade gracefully to text.
    /// </summary>
    /// <param name="content">The multi-modal content of the message.</param>
    /// <returns>A user message carrying the multi-modal content.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
    public static LlmMessage User(MultiModalContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new()
        {
            Role = LlmRoles.User,
            Content = content.ToTextOnly(),
            MultiModalContent = content
        };
    }

    /// <summary>
    /// Creates an assistant message.
    /// </summary>
    public static LlmMessage Assistant(string content) => new() { Role = LlmRoles.Assistant, Content = content };
}
