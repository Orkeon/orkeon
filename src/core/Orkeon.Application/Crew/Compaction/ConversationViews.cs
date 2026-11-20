using System.Text;
using Microsoft.Extensions.AI;
using Orkeon.Domain.Constants.Llm;
using LlmMessage = Orkeon.Domain.SharedKernel.ValueObjects.LlmMessage;

namespace Orkeon.Application.Crew.Compaction;

/// <summary>The chat-client loop's conversation (<see cref="ChatMessage"/>) as a <see cref="ConversationView"/>.</summary>
internal sealed class ChatConversationView : ConversationView
{
    private readonly List<ChatMessage> _messages;

    internal ChatConversationView(List<ChatMessage> messages) => _messages = messages;

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
}

/// <summary>The native loop's conversation (<see cref="LlmMessage"/>) as a <see cref="ConversationView"/>.</summary>
internal sealed class LlmConversationView : ConversationView
{
    private readonly List<LlmMessage> _messages;

    internal LlmConversationView(List<LlmMessage> messages) => _messages = messages;

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
