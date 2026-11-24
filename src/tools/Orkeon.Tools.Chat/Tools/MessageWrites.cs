using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Places;
using Orkeon.Tools.Chat.Providers;

namespace Orkeon.Tools.Chat.Tools;

/// <summary>What the tools that change an existing message check before they do.</summary>
internal static class MessageWrites
{
    /// <summary>The place, among those the account may post in; a person's is the chat that exists, none is opened.</summary>
    public static async Task<ChatTarget> AdmitAsync(ChatSession session, PlaceReference place, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(place);
        var target = await session.Gate.AdmitAsync(place, PlaceUse.Send, cancellationToken).ConfigureAwait(false);
        if (target.Person is not { } person)
            return target;
        // Listing the chats needs Chat.Read, or Chat.ReadWrite which holds it.
        if (!session.Account.Grants(ChatRights.Read) && !session.Account.Grants(ChatRights.Edit) && !session.Account.Grants(ChatRights.Delete))
        {
            throw new ChatToolException(
                ChatErrorCode.RightMissing,
                $"Chat account '{session.Account.Name}' cannot find the chat with '{place.Value}', since it has no Read right: write chat:<id> instead.");
        }

        return new ChatTarget
        {
            ChatId = await session.Provider.FindDirectChatAsync(person.Id, cancellationToken).ConfigureAwait(false)
                ?? throw new ChatToolException(ChatErrorCode.NotFound, $"No one-to-one chat with '{place.Value}' was found; nothing was changed."),
        };
    }

    /// <summary>The message, when the account's user wrote it and it is still there.</summary>
    public static async Task<ChatMessage> OwnAsync(ChatSession session, ChatTarget target, string? thread, string messageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var self = await session.Provider.GetSelfAsync(cancellationToken).ConfigureAwait(false);
        ChatMessage message;
        try
        {
            message = await session.Provider.GetMessageAsync(target, thread, messageId, cancellationToken).ConfigureAwait(false);
        }
        catch (ChatToolException ex) when (ex.Code == ChatErrorCode.NotFound && target.IsChannel && thread is null)
        {
            throw new ChatToolException(ChatErrorCode.NotFound, $"'{target.Place}' has no message '{messageId}': for a reply, pass the message it replies to as `thread`.", ex);
        }

        if (!message.IsMessage || message.Author is not { IsApplication: false, Id: { Length: > 0 } author } || !string.Equals(author, self.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new ChatToolException(
                ChatErrorCode.NotSupportedByAccount,
                $"Message '{messageId}' was not sent by chat account '{session.Account.Name}': an account changes its own messages only. Nothing was changed.");
        }

        return message.IsDeleted
            ? throw new ChatToolException(ChatErrorCode.NotFound, $"Message '{messageId}' is already deleted; nothing was changed.")
            : message;
    }
}
