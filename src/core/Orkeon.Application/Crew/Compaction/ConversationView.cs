namespace Orkeon.Application.Crew.Compaction;

/// <summary>
/// A loop's conversation as a <see cref="IContextPressureStage"/> sees it (#432), whatever the loop's
/// message type: the roles, the texts, and the one rewrite a stage may make, a tool result replaced
/// by a shorter text. Index 0 is the system prompt and index 1 the task.
/// </summary>
public abstract class ConversationView
{
    /// <summary>The role of a system message.</summary>
    public const string SystemRole = "system";

    /// <summary>The role of a user message.</summary>
    public const string UserRole = "user";

    /// <summary>The role of an assistant message.</summary>
    public const string AssistantRole = "assistant";

    /// <summary>The role of a tool result.</summary>
    public const string ToolRole = "tool";

    /// <summary>Only the loops' own views derive from it.</summary>
    private protected ConversationView()
    {
    }

    /// <summary>How many messages the conversation holds.</summary>
    public abstract int Count { get; }

    /// <summary>The role of the message at <paramref name="index"/>: one of the role constants above.</summary>
    /// <param name="index">The message's index.</param>
    public abstract string RoleAt(int index);

    /// <summary>The text of the message at <paramref name="index"/>, its tool calls and results written out.</summary>
    /// <param name="index">The message's index.</param>
    public abstract string TextAt(int index);

    /// <summary>
    /// Replaces the content of the tool result at <paramref name="index"/> with <paramref name="text"/>,
    /// keeping its call id so the call and its result stay paired.
    /// </summary>
    /// <param name="index">The message's index.</param>
    /// <param name="text">The new content.</param>
    /// <returns>False when the message is not a tool result, and nothing changed.</returns>
    public abstract bool ReplaceToolResult(int index, string text);

    /// <summary>Replaces <paramref name="count"/> messages from <paramref name="start"/> with one user message.</summary>
    internal abstract void ReplaceRange(int start, int count, string userText);
}
