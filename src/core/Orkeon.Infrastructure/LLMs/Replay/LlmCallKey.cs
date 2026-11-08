using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Replay;

/// <summary>
/// The key a call is filed under in a cassette: the SHA-256 of the profile, the messages, the system
/// message, the tools and the tool mode, the response format, the grammar, the model, the sampling
/// options (temperature, top-p, seed, penalties, stop sequences, maximum tokens, thinking) and the
/// <c>system_message</c> custom parameter - everything of the per-call configuration that shapes the answer - with the timestamps and the generated identifiers the run injected into the
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
            systemMessage = config?.SystemMessage is null ? null : Normalize(config.SystemMessage),
            tools = config?.Tools is null ? null : Serialize(config.Tools),
            // Defaults weigh as nothing: a call without a configuration and one with the default
            // configuration ask the same.
            toolMode = config is null || config.ToolMode == Orkeon.Domain.Tools.Protocol.ToolCallMode.Auto ? null : config.ToolMode.ToString(),
            responseFormat = config?.ResponseFormat is null ? null : Serialize(config.ResponseFormat),
            grammar = config is { GrammarEnabled: true } ? config.GrammarGbnf ?? string.Empty : null,
            model = string.IsNullOrWhiteSpace(config?.Model) ? null : config.Model,
            temperature = config?.Temperature,
            topP = config?.TopP,
            seed = config?.Seed,
            frequencyPenalty = config is { FrequencyPenalty: not 0 } ? config.FrequencyPenalty : (double?)null,
            presencePenalty = config is { PresencePenalty: not 0 } ? config.PresencePenalty : (double?)null,
            stop = config is { StopSequences.Count: > 0 } ? config.StopSequences : null,
            maxTokens = config?.MaxTokens,
            thinking = config?.Thinking is null ? null : Serialize(config.Thinking),
            // The one custom parameter the providers send as part of the request (the system message
            // of a configuration written before SystemMessage existed); the others are the profile's
            // plumbing (the mock's responses file, an API version) and a replay on another profile
            // must still find the call.
            customSystemMessage = config?.CustomParameters.TryGetValue("system_message", out var custom) == true && custom is not null
                ? Normalize(custom.ToString() ?? string.Empty)
                : null,
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
