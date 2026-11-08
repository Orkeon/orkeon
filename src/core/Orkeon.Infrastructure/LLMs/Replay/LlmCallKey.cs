using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Replay;

/// <summary>
/// The key a call is filed under in a cassette: the SHA-256 of the profile, the messages, the tools and
/// the response format, the model and the sampling options, with the timestamps and the generated identifiers the run injected into the
/// text replaced by a fixed mark and the secret-shaped values redacted as the cassette stores them, so that a second run asks for the same key.
/// </summary>
internal static partial class LlmCallKey
{
    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:?\d{2})?", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Timestamp();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Identifier();

    /// <summary>The key of a chat call.</summary>
    public static string Of(string profile, LlmMessage[] messages, LlmConfig? config)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(messages);
        var request = new
        {
            profile,
            messages = messages.Select(m => new
            {
                role = m.Role,
                content = Normalize(m.Content),
                name = m.Name,
                toolCallId = m.ToolCallId,
                rawToolCalls = m.RawToolCalls is null ? null : Normalize(m.RawToolCalls),
                multiModal = m.MultiModalContent is null ? null : Serialize(m.MultiModalContent),
            }),
            tools = config?.Tools is null ? null : Serialize(config.Tools),
            responseFormat = config?.ResponseFormat is null ? null : Serialize(config.ResponseFormat),
            model = string.IsNullOrWhiteSpace(config?.Model) ? null : config.Model,
            temperature = config?.Temperature,
            maxTokens = config?.MaxTokens,
            thinking = config?.Thinking is null ? null : Serialize(config.Thinking),
        };
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
    }

    /// <summary>The key of a prompt call: the key of a chat call holding that one user message.</summary>
    public static string OfPrompt(string profile, string prompt, LlmConfig? config) =>
        Of(profile, [new LlmMessage { Role = "user", Content = prompt ?? string.Empty }], config);

    /// <summary>A short, normalized excerpt of the last user message, for a miss to name the call.</summary>
    public static string Excerpt(LlmMessage[] messages)
    {
        var last = Array.FindLast(messages, m => m.Role == "user") ?? (messages.Length > 0 ? messages[^1] : null);
        var text = Normalize(last?.Content ?? string.Empty);
        return text.Length <= 80 ? text : text[..80] + "...";
    }

    private static string Normalize(string text) =>
        Identifier().Replace(Timestamp().Replace(CassetteRedaction.Text(text), "<time>"), "<id>");

    private static string Serialize(object value)
    {
        try
        {
            return Normalize(JsonSerializer.Serialize(value));
        }
        catch (NotSupportedException)
        {
            return Normalize(value.ToString() ?? string.Empty);
        }
        catch (InvalidOperationException)
        {
            return Normalize(value.ToString() ?? string.Empty);
        }
    }
}
