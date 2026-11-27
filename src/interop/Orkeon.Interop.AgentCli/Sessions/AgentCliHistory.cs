using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Interop.AgentCli.Sessions;

/// <summary>
/// One message of a conversation as a session compares it (plan 4.4): its role, its text and the ids of the
/// tool calls it makes or answers — never its raw bytes, which the crews' path rewrites.
/// </summary>
internal readonly record struct AgentCliMessageKey(string Role, string Text, string ToolIds);

/// <summary>How the provider reads the conversation it is handed: the system prompt, the history, their keys.</summary>
internal static class AgentCliHistory
{
    /// <summary>The note a system message in the middle of the history is written as: the CLI's prompt is set at launch.</summary>
    internal const string SystemNotePrefix = "[System note] ";

#pragma warning disable CA1308 // the role token is normalised for comparison, not displayed
    internal static string RoleOf(LlmMessage message) => (message.Role ?? "").Trim().ToLowerInvariant();
#pragma warning restore CA1308

    /// <summary>
    /// Splits <paramref name="messages"/>: the leading system messages, with <paramref name="systemMessage"/>
    /// before them, make the system prompt; the rest is the history.
    /// </summary>
    internal static (string SystemPrompt, IReadOnlyList<LlmMessage> History) Split(IReadOnlyList<LlmMessage> messages, string? systemMessage)
    {
        var prompt = new List<string>();
        if (!string.IsNullOrWhiteSpace(systemMessage))
            prompt.Add(systemMessage);

        var index = 0;
        while (index < messages.Count && RoleOf(messages[index]) == "system")
        {
            if (!string.IsNullOrWhiteSpace(messages[index].Content))
                prompt.Add(messages[index].Content);
            index++;
        }

        return (string.Join("\n\n", prompt), messages.Skip(index).ToList());
    }

    internal static AgentCliMessageKey KeyOf(LlmMessage message)
    {
        var role = RoleOf(message);
        var ids = role switch
        {
            "assistant" => string.Join(',', ToolCallsOf(message).Select(call => call.Id)),
            "tool" => message.ToolCallId ?? "",
            _ => "",
        };
        return new AgentCliMessageKey(role, message.Content ?? "", ids);
    }

    /// <summary>The tool calls an assistant message carries (<see cref="LlmMessage.RawToolCalls"/>, either shape).</summary>
    internal static IReadOnlyList<(string Id, string Name, string Arguments)> ToolCallsOf(LlmMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.RawToolCalls))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(message.RawToolCalls);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var calls = new List<(string, string, string)>();
            foreach (var call in doc.RootElement.EnumerateArray())
            {
                if (call.ValueKind != JsonValueKind.Object)
                    continue;
                var id = call.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? "" : "";
                if (call.TryGetProperty("function", out var function) && function.ValueKind == JsonValueKind.Object)
                {
                    calls.Add((id,
                        function.TryGetProperty("name", out var fn) ? fn.GetString() ?? "" : "",
                        function.TryGetProperty("arguments", out var args) ? args.ToString() : "{}"));
                }
                else
                {
                    calls.Add((id,
                        call.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                        call.TryGetProperty("input", out var input) ? input.GetRawText() : "{}"));
                }
            }

            return calls;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>
    /// What tells sessions apart within a run: the model, the system prompt and the first message (plan 4.4), and the
    /// thinking effort, which the CLI takes for its whole process (#487): a call at another level gets a process of its own.
    /// </summary>
    internal static string Fingerprint(string? model, string systemPrompt, IReadOnlyList<AgentCliMessageKey> keys, string? effort = null)
    {
        var first = keys.Count > 0 ? keys[0] : default;
        var text = string.Join('\u001f', model ?? "", systemPrompt, first.Role, first.Text, first.ToolIds, effort ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>
    /// The first message of a new session that takes over <paramref name="history"/> (plan 4.4, probe 31): one
    /// user message carrying the history rendered as text, then the current request — the last user message, or
    /// a request to go on from where the history stops.
    /// </summary>
    internal static string Seed(IReadOnlyList<LlmMessage> history)
    {
        if (history.Count == 1 && RoleOf(history[0]) == "user")
            return history[0].Content ?? "";

        var last = history.Count - 1;
        var request = RoleOf(history[last]) == "user" ? history[last].Content : null;
        var earlier = request is null ? history : history.Take(last).ToList();

        var text = new StringBuilder();
        text.Append("<conversation_history>\n");
        text.Append("This conversation started earlier; here it is, oldest first. Tool results are shown as they were returned.\n\n");
        foreach (var message in earlier)
            Render(text, message);
        text.Append("</conversation_history>\n\n");
        text.Append(request ?? "Continue from where the conversation above stops.");
        return text.ToString();
    }

    private static void Render(StringBuilder text, LlmMessage message)
    {
        switch (RoleOf(message))
        {
            case "assistant":
                if (!string.IsNullOrWhiteSpace(message.Content))
                    text.Append("[assistant]\n").Append(message.Content).Append("\n\n");
                foreach (var (id, name, arguments) in ToolCallsOf(message))
                    text.Append("[assistant called tool ").Append(name).Append(" (").Append(id).Append(") with ").Append(arguments).Append("]\n\n");
                break;
            case "tool":
                text.Append("[result of tool call ").Append(message.ToolCallId).Append("]\n").Append(message.Content).Append("\n\n");
                break;
            case "system":
                text.Append(SystemNotePrefix).Append(message.Content).Append("\n\n");
                break;
            default:
                text.Append("[user]\n").Append(message.Content).Append("\n\n");
                break;
        }
    }
}
