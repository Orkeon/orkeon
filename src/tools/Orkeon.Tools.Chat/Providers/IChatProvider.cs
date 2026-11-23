namespace Orkeon.Tools.Chat.Providers;

/// <summary>
/// One account's conversation with its chat service, by ids alone: names, rights and allowed
/// places are judged before it is called. What a service or an identity cannot do answers
/// <see cref="ChatErrorCode.NotSupportedByAccount"/>.
/// </summary>
internal interface IChatProvider
{
    /// <summary>The identity the account acts under.</summary>
    Task<ChatPerson> GetSelfAsync(CancellationToken cancellationToken);

    /// <summary>The teams the account has joined.</summary>
    Task<IReadOnlyList<ChatTeam>> ListTeamsAsync(CancellationToken cancellationToken);

    /// <summary>The channels of a team the account sees.</summary>
    Task<IReadOnlyList<ChatChannel>> ListChannelsAsync(string teamId, CancellationToken cancellationToken);

    /// <summary>A page of the account's chats, members included.</summary>
    Task<ChatPage<ChatConversation>> ListChatsAsync(int top, string? page, CancellationToken cancellationToken);

    /// <summary>A chat and its members.</summary>
    Task<ChatConversation> GetChatAsync(string chatId, CancellationToken cancellationToken);

    /// <summary>The members of a place.</summary>
    Task<IReadOnlyList<ChatMember>> ListMembersAsync(ChatTarget target, CancellationToken cancellationToken);

    /// <summary>The person of a directory id, or null when the directory has none.</summary>
    Task<ChatPerson?> GetPersonAsync(string userId, CancellationToken cancellationToken);

    /// <summary>The people with an address.</summary>
    Task<IReadOnlyList<ChatPerson>> FindByAddressAsync(string address, CancellationToken cancellationToken);

    /// <summary>The people whose name holds some words.</summary>
    Task<IReadOnlyList<ChatPerson>> FindByNameAsync(string name, int top, CancellationToken cancellationToken);

    /// <summary>The id of the existing one-to-one chat with a person, or null.</summary>
    Task<string?> FindDirectChatAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Opens the one-to-one chat with a person, or returns the existing one. A write: the place is checked first.</summary>
    Task<string> OpenDirectChatAsync(string userId, CancellationToken cancellationToken);

    /// <summary>A page of the messages of a place, newest first.</summary>
    Task<ChatPage<ChatMessage>> ReadAsync(ChatTarget target, ChatReadQuery query, CancellationToken cancellationToken);

    /// <summary>Posts a message. A write: the place is checked first.</summary>
    Task<ChatSent> SendAsync(ChatTarget target, ChatOutgoing message, CancellationToken cancellationToken);

    /// <summary>One message of a place: <paramref name="thread"/> is the message it replies to, for a reply in a channel.</summary>
    Task<ChatMessage> GetMessageAsync(ChatTarget target, string? thread, string messageId, CancellationToken cancellationToken) => throw Unsupported("read one message");

    /// <summary>
    /// A window of what the service's search finds, as ids alone: the places they are in are
    /// not checked, and nothing of a hit may be shown before its place is.
    /// </summary>
    Task<ChatSearchPage> SearchAsync(ChatSearchQuery query, CancellationToken cancellationToken) => throw Unsupported("search messages");

    /// <summary>Rewrites a message. A write: the place and the author are checked first.</summary>
    Task EditAsync(ChatTarget target, string? thread, string messageId, ChatOutgoing message, CancellationToken cancellationToken) => throw Unsupported("edit a message");

    /// <summary>Deletes a message, recoverably. A write: the place and the author are checked first.</summary>
    Task DeleteAsync(ChatTarget target, string? thread, string messageId, CancellationToken cancellationToken) => throw Unsupported("delete a message");

    /// <summary>Sets or removes the account's reaction on a message of a chat. A write: the place is checked first.</summary>
    Task ReactAsync(ChatTarget target, string messageId, string reaction, bool remove, CancellationToken cancellationToken) => throw Unsupported("react to a message");

    /// <summary>The join link of a meeting's chat, or null for another chat. The place is checked first.</summary>
    Task<string?> GetMeetingLinkAsync(string chatId, CancellationToken cancellationToken) => throw Unsupported("find the meeting of a chat");

    /// <summary>The meeting of a join link. Nothing of it may be shown before its chat is checked.</summary>
    Task<ChatMeeting?> FindMeetingAsync(string joinLink, CancellationToken cancellationToken) => throw Unsupported("find a meeting");

    /// <summary>The transcripts of a meeting.</summary>
    Task<IReadOnlyList<ChatTranscript>> ListTranscriptsAsync(string meetingId, CancellationToken cancellationToken) => throw Unsupported("list transcripts");

    /// <summary>A transcript as WebVTT, or as plain text when the tenant hides who speaks.</summary>
    Task<ChatTranscriptText> ReadTranscriptAsync(string meetingId, string transcriptId, CancellationToken cancellationToken) => throw Unsupported("read a transcript");

    private static ChatToolException Unsupported(string what) =>
        new(ChatErrorCode.NotSupportedByAccount, $"This chat account cannot {what}.");
}
