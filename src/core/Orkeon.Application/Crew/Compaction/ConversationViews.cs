using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Orkeon.Domain.Constants.Llm;
using LlmMessage = Orkeon.Domain.SharedKernel.ValueObjects.LlmMessage;

namespace Orkeon.Application.Crew.Compaction;

/// <summary>Whether the tool <c>name</c> only reads for <c>arguments</c>; false for a tool the loop does not know.</summary>
internal delegate bool ReadToolCheck(string name, IReadOnlyDictionary<string, object?> arguments);

/// <summary>The <see cref="ReadToolCheck"/> of a loop's tools.</summary>
internal static class ReadToolChecks
{
    /// <summary>A tool of <paramref name="tools"/> whose access for the call's input is <see cref="Domain.Tools.ToolAccess.Read"/>.</summary>
    internal static ReadToolCheck Over(IEnumerable<Domain.Tools.IBaseTool> tools)
    {
        var byName = new Dictionary<string, Domain.Tools.IBaseTool>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in tools)
            byName.TryAdd(tool.Name, tool);
        return (name, arguments) => byName.TryGetValue(name, out var tool) && tool.AccessFor(arguments) == Domain.Tools.ToolAccess.Read;
    }
}

/// <summary>The chat-client loop's conversation (<see cref="ChatMessage"/>) as a <see cref="ConversationView"/>.</summary>
internal sealed class ChatConversationView : ConversationView
{
    private readonly List<ChatMessage> _messages;
    private readonly ReadToolCheck _isRead;

    internal ChatConversationView(List<ChatMessage> messages, ReadToolCheck? isRead = null)
    {
        _messages = messages;
        _isRead = isRead ?? NoReadTool;
    }

    public override int Count => _messages.Count;

    public override string RoleAt(int index)
    {
        var role = _messages[index].Role;
        if (role == ChatRole.System) return SystemRole;
        if (role == ChatRole.Assistant) return AssistantRole;
        if (role == ChatRole.Tool) return ToolRole;
        return UserRole;
    }

    public override string TextAt(int index)
    {
        var message = _messages[index];
        var text = new StringBuilder(message.Text ?? string.Empty);
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case FunctionCallContent call:
                    Separate(text).Append("call ").Append(call.Name).Append('(')
                        .Append(call.Arguments is null ? string.Empty : System.Text.Json.JsonSerializer.Serialize(call.Arguments))
                        .Append(')');
                    break;
                case FunctionResultContent result:
                    Separate(text).Append(result.Result?.ToString() ?? string.Empty);
                    break;
            }
        }
        return text.ToString();
    }

    public override ConversationToolCall? ToolCallAt(int index)
    {
        if (_messages[index].Role != ChatRole.Tool
            || _messages[index].Contents.OfType<FunctionResultContent>().FirstOrDefault()?.CallId is not { } callId)
            return null;

        // The call sits in an assistant message before its result.
        for (var i = index - 1; i >= 0; i--)
        {
            if (_messages[i].Contents.OfType<FunctionCallContent>().FirstOrDefault(c => c.CallId == callId) is not { } call)
                continue;
            var arguments = call.Arguments is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(call.Arguments);
            return new ConversationToolCall
            {
                CallId = callId,
                ToolName = call.Name,
                Input = JsonSerializer.Serialize(arguments),
                IsRead = _isRead(call.Name, arguments),
            };
        }
        return null;
    }

    public override bool ReplaceToolResult(int index, string text)
    {
        var message = _messages[index];
        if (message.Role != ChatRole.Tool)
            return false;

        var callId = message.Contents.OfType<FunctionResultContent>().FirstOrDefault()?.CallId;
        _messages[index] = callId is null
            ? new ChatMessage(ChatRole.Tool, text)
            : new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, text)]);
        return true;
    }

    internal override void ReplaceRange(int start, int count, string userText)
    {
        _messages.RemoveRange(start, count);
        _messages.Insert(start, new ChatMessage(ChatRole.User, userText));
    }

    private static StringBuilder Separate(StringBuilder text) => text.Length == 0 ? text : text.Append('\n');

    internal static bool NoReadTool(string name, IReadOnlyDictionary<string, object?> arguments) => false;
}

/// <summary>The native loop's conversation (<see cref="LlmMessage"/>) as a <see cref="ConversationView"/>.</summary>
internal sealed class LlmConversationView : ConversationView
{
    private readonly List<LlmMessage> _messages;
    private readonly ReadToolCheck _isRead;

    // The calls the loop ran, by id: a raw tool-call block is in a provider's own shape, the loop's parse is not.
    private readonly Dictionary<string, ConversationToolCall> _calls = new(StringComparer.Ordinal);

    internal LlmConversationView(List<LlmMessage> messages, ReadToolCheck? isRead = null)
    {
        _messages = messages;
        _isRead = isRead ?? ChatConversationView.NoReadTool;
    }

    /// <summary>Notes a call the loop ran, so <see cref="ToolCallAt"/> finds it from its result's id.</summary>
    internal void NoteToolCall(string callId, string toolName, IReadOnlyDictionary<string, object?> arguments) =>
        _calls[callId] = new ConversationToolCall
        {
            CallId = callId,
            ToolName = toolName,
            Input = JsonSerializer.Serialize(arguments),
            IsRead = _isRead(toolName, arguments),
        };

    public override ConversationToolCall? ToolCallAt(int index) =>
        LlmRoles.IsTool(_messages[index].Role) && _messages[index].ToolCallId is { } callId
            && _calls.TryGetValue(callId, out var call)
            ? call
            : null;

    public override int Count => _messages.Count;

    public override string RoleAt(int index)
    {
        var role = _messages[index].Role;
        if (LlmRoles.IsTool(role)) return ToolRole;
        if (LlmRoles.IsAssistant(role)) return AssistantRole;
        return LlmRoles.IsSystem(role) ? SystemRole : UserRole;
    }

    public override string TextAt(int index)
    {
        var message = _messages[index];
        return string.IsNullOrEmpty(message.RawToolCalls)
            ? message.Content
            : string.IsNullOrEmpty(message.Content) ? message.RawToolCalls : $"{message.Content}\n{message.RawToolCalls}";
    }

    public override bool ReplaceToolResult(int index, string text)
    {
        if (!LlmRoles.IsTool(_messages[index].Role))
            return false;

        _messages[index] = _messages[index] with { Content = text };
        return true;
    }

    internal override void ReplaceRange(int start, int count, string userText)
    {
        _messages.RemoveRange(start, count);
        _messages.Insert(start, LlmMessage.User(userText));
    }
}
