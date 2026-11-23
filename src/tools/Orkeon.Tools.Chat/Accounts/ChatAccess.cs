using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Constants;
using Orkeon.Tools.Chat.Places;
using Orkeon.Tools.Chat.Providers;

namespace Orkeon.Tools.Chat.Accounts;

/// <summary>
/// The gate every tool goes through: it resolves the account, checks that the account grants the
/// right the operation needs, then that the place is one it may use.
/// </summary>
internal sealed class ChatAccess
{
    private readonly IChatAccountRegistry _accounts;
    private readonly ChatSessions _sessions;

    /// <summary>Creates the gate.</summary>
    public ChatAccess(IChatAccountRegistry accounts, ChatSessions sessions)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(sessions);
        _accounts = accounts;
        _sessions = sessions;
    }

    /// <summary>The session of the account named <paramref name="account"/>, checked for one of <paramref name="rights"/>.</summary>
    public ChatSession Authorize(string? account, params ReadOnlySpan<ChatRights> rights)
    {
        var resolved = _accounts.Resolve(account);
        foreach (var right in rights)
        {
            if (resolved.Grants(right))
                return _sessions.Get(resolved);
        }

        throw new ChatToolException(
            ChatErrorCode.RightMissing,
            $"Chat account '{resolved.Name}' does not grant the {string.Join(" or ", rights.ToArray())} right (it grants: {resolved.Rights}). " +
            $"An operator adds it under {ChatDefaults.SectionName}:Accounts:{resolved.Name}:Rights.");
    }

    /// <summary>The place a call writes; a refusal that shows the syntax when it is not one.</summary>
    public static PlaceReference Place(string? place) => PlaceReference.TryParse(place, out var parsed)
        ? parsed
        : throw new ChatToolException(ChatErrorCode.InvalidRequest, $"`place` is not a place: {PlaceReference.Syntax}");

    /// <summary>Refuses to read a channel's messages the administrator has not opened, before any request.</summary>
    public static void DemandChannelMessages(ResolvedChatAccount account) =>
        Demand(account, ChatAdminGrants.ChannelMessages, "Reading the messages of a channel", ChatScopes.ChannelMessageRead);

    /// <summary>Refuses to list a channel's members the administrator has not opened, before any request.</summary>
    public static void DemandChannelMembers(ResolvedChatAccount account) =>
        Demand(account, ChatAdminGrants.ChannelMembers, "Listing the members of a channel", ChatScopes.ChannelMemberRead);

    private static void Demand(ResolvedChatAccount account, ChatAdminGrants grant, string what, string permission)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.IsGranted(grant))
            return;
        throw new ChatToolException(
            ChatErrorCode.ConsentMissing,
            $"{what} needs {permission}, which a tenant administrator grants to the application. Once granted, an operator adds " +
            $"{grant} under {ChatDefaults.SectionName}:Accounts:{account.Name}:Auth:AdminGranted and runs `orkeon chat login {account.Name}` again. Chats need no administrator.");
    }
}
