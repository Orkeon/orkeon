using Orkeon.Tools.Chat.Administration;
using Orkeon.Tools.Chat.Configuration;

namespace Orkeon.Tools.Chat.Accounts;

/// <summary>Tells which chat rights a crew's tools need that no ready account grants.</summary>
public sealed class ChatRightsCheck
{
    private const string SendTool = "chat_send";

    private readonly ChatAccountAdministration _administration;

    /// <summary>Creates the check over the accounts' administration.</summary>
    public ChatRightsCheck(ChatAccountAdministration administration) => _administration = administration;

    /// <summary>One line per chat tool whose right no ready account grants, over the accounts as they stand.</summary>
    public async Task<IReadOnlyList<string>> CheckAsync(IReadOnlyCollection<string> crewTools, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(crewTools);
        if (!crewTools.Any(tool => BaseRight(tool) != ChatRights.None))
            return [];

        var statuses = await _administration.ListAsync(cancellationToken).ConfigureAwait(false);
        return Check(crewTools, statuses);
    }

    /// <summary>One line per chat tool whose right no ready account grants.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Public instance API: the check is a registered service, its callers hold an instance.")]
    public IReadOnlyList<string> Check(IReadOnlyCollection<string> crewTools, IReadOnlyList<ChatAccountStatus> accounts)
    {
        ArgumentNullException.ThrowIfNull(crewTools);
        ArgumentNullException.ThrowIfNull(accounts);
        var lines = new List<string>();
        foreach (var tool in crewTools.Distinct(StringComparer.Ordinal))
        {
            var right = BaseRight(tool);
            if (right == ChatRights.None)
                continue;
            if (accounts.Any(account => Grants(account, tool, right)))
                continue;

            var side = right == ChatRights.Send ? "Send" : "Read";
            lines.Add(
                $"{tool} needs the {right} right with a non-empty {side}:AllowedPlaces on a ready chat account, and no account has it. " +
                $"Add it under Orkeon:Tools:Chat:Accounts:<name>:Rights and {side}.");
        }

        return lines;
    }

    /// <summary>The right a chat tool needs.</summary>
    internal static ChatRights BaseRight(string tool) => tool switch
    {
        "chat_list" or "chat_read" or "chat_members" => ChatRights.Read,
        SendTool => ChatRights.Send,
        _ => ChatRights.None,
    };

    private static bool Grants(ChatAccountStatus account, string tool, ChatRights right)
    {
        if (!account.Ready || !Enum.TryParse<ChatRights>(account.Rights, out var granted))
            return false;
        // A webhook posts into one place, and has nothing to list or to look up.
        if (account.Provider == nameof(ChatProvider.TeamsWebhook))
            return tool == SendTool;
        // A person is looked up in the directory, where no place counts.
        if (tool == "chat_members" && (granted & (ChatRights.Read | ChatRights.Send)) != 0)
            return true;
        // The places to post in are listed too.
        if (tool == "chat_list" && granted.HasFlag(ChatRights.Send) && account.SendPlaces > 0)
            return true;
        return granted.HasFlag(right) && (right == ChatRights.Send ? account.SendPlaces : account.ReadPlaces) > 0;
    }
}
