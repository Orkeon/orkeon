using Orkeon.Tools.Chat.Accounts;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Places;

namespace Orkeon.Tools.Chat.Tools;

/// <summary>The place, the message and the thread a call that changes a message names.</summary>
internal static class MessageAddress
{
    /// <summary>The refusal of the arguments as they are written, or null.</summary>
    public static string? Refuse(string? place, string? messageId, string? thread)
    {
        if (string.IsNullOrWhiteSpace(place))
            return "`place` is required: pass the place of the message, from chat_read or chat_search";
        if (messageId is null || !ChatIds.IsMessageId(messageId))
            return "`message_id` must be the id of a message from chat_read, chat_search or chat_send";
        return thread is not null && !ChatIds.IsMessageId(thread) ? "`thread` must be the id of the message a reply answers, from chat_read or chat_search" : null;
    }

    /// <summary>The place of a message to edit or delete, refused before any request when the account has not what its kind needs.</summary>
    public static PlaceReference Place(ResolvedChatAccount account, string place, string? thread)
    {
        var parsed = ChatAccess.Place(place);
        if (parsed.Form == PlaceForm.Channel)
            ChatAccess.DemandChannelEdit(account);
        else if (thread is not null)
            throw new ChatToolException(ChatErrorCode.NotSupportedByAccount, "`thread` is the message a channel's reply answers: the messages of a chat have no thread.");
        return parsed;
    }
}
