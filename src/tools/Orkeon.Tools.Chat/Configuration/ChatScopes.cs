namespace Orkeon.Tools.Chat.Configuration;

/// <summary>The Microsoft Graph permissions of the Teams provider, by the names Graph uses.</summary>
internal static class ChatScopes
{
    public const string TeamRead = "Team.ReadBasic.All";
    public const string ChannelRead = "Channel.ReadBasic.All";
    public const string ChatRead = "Chat.Read";
    public const string UserRead = "User.ReadBasic.All";
    public const string ChannelMessageRead = "ChannelMessage.Read.All";
    public const string ChannelMessageSend = "ChannelMessage.Send";
    public const string ChatMessageSend = "ChatMessage.Send";
    public const string ChatCreate = "Chat.Create";
    public const string ChatReadWrite = "Chat.ReadWrite";
    public const string ChannelMessageReadWrite = "ChannelMessage.ReadWrite";
    public const string OnlineMeetingsRead = "OnlineMeetings.Read";
    public const string TranscriptRead = "OnlineMeetingTranscript.Read.All";

    /// <summary>The members of a channel: an administrator's permission that no right asks for.</summary>
    public const string ChannelMemberRead = "ChannelMember.Read.All";

    /// <summary>
    /// The delegated permissions each right asks for, and the administrator's grant a permission
    /// waits for: Entra refuses a whole consent request when one of its permissions needs an
    /// administrator, so such a permission is asked only when <c>AdminGranted</c> names it.
    /// </summary>
    public static readonly IReadOnlyList<(ChatRights Right, string Permission, ChatAdminGrants Needs)> Table =
    [
        (ChatRights.Read, TeamRead, ChatAdminGrants.None),
        (ChatRights.Read, ChannelRead, ChatAdminGrants.None),
        (ChatRights.Read, ChatRead, ChatAdminGrants.None),
        (ChatRights.Read, UserRead, ChatAdminGrants.None),
        (ChatRights.Read, ChannelMessageRead, ChatAdminGrants.ChannelMessages),
        (ChatRights.Send, ChannelMessageSend, ChatAdminGrants.None),
        (ChatRights.Send, ChatMessageSend, ChatAdminGrants.None),
        (ChatRights.Send, ChatCreate, ChatAdminGrants.None),
        (ChatRights.Send, TeamRead, ChatAdminGrants.None),
        (ChatRights.Send, ChannelRead, ChatAdminGrants.None),
        (ChatRights.Send, UserRead, ChatAdminGrants.None),
        (ChatRights.Edit, ChatReadWrite, ChatAdminGrants.None),
        (ChatRights.Edit, TeamRead, ChatAdminGrants.None),
        (ChatRights.Edit, ChannelRead, ChatAdminGrants.None),
        (ChatRights.Edit, UserRead, ChatAdminGrants.None),
        (ChatRights.Edit, ChannelMessageRead, ChatAdminGrants.ChannelMessages),
        (ChatRights.Edit, ChannelMessageReadWrite, ChatAdminGrants.ChannelEdit),
        (ChatRights.Delete, ChatReadWrite, ChatAdminGrants.None),
        (ChatRights.Delete, TeamRead, ChatAdminGrants.None),
        (ChatRights.Delete, ChannelRead, ChatAdminGrants.None),
        (ChatRights.Delete, UserRead, ChatAdminGrants.None),
        (ChatRights.Delete, ChannelMessageRead, ChatAdminGrants.ChannelMessages),
        (ChatRights.Delete, ChannelMessageReadWrite, ChatAdminGrants.ChannelEdit),
        (ChatRights.React, ChatMessageSend, ChatAdminGrants.None),
        (ChatRights.React, UserRead, ChatAdminGrants.None),
        (ChatRights.Meetings, OnlineMeetingsRead, ChatAdminGrants.None),
        (ChatRights.Meetings, TranscriptRead, ChatAdminGrants.Meetings),
    ];

    /// <summary>The permissions an account with <paramref name="rights"/> and <paramref name="granted"/> asks for, never more.</summary>
    public static IReadOnlyList<string> Permissions(ChatRights rights, ChatAdminGrants granted) =>
        Table.Where(row => rights.HasFlag(row.Right) && granted.HasFlag(row.Needs))
            .Select(row => row.Permission)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Whether a tenant administrator has to consent to <paramref name="permission"/>.</summary>
    public static bool NeedsAdministrator(string permission) =>
        permission == ChannelMemberRead || Table.Any(row => row.Permission == permission && row.Needs != ChatAdminGrants.None);
}
