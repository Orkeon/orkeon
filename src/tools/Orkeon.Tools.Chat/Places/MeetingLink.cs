using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Constants;

namespace Orkeon.Tools.Chat.Places;

/// <summary>The join link of a meeting, as a call may pass it.</summary>
internal static class MeetingLink
{
    /// <summary>The link trimmed, when it is one of the account's cloud; null otherwise.</summary>
    public static string? Parse(string? text, ChatCloud cloud)
    {
        var link = text?.Trim() ?? string.Empty;
        if (link.Length is 0 or > ChatDefaults.MaxJoinLinkChars || link.Any(c => c is <= ' ' or > '~'))
            return null;
        return Uri.TryCreate(link, UriKind.Absolute, out var address)
            && address.Scheme == Uri.UriSchemeHttps
            && address.IsDefaultPort
            && address.UserInfo.Length == 0
            && string.Equals(address.Host, Host(cloud), StringComparison.OrdinalIgnoreCase)
                ? link
                : null;
    }

    /// <summary>The host of the join links of <paramref name="cloud"/>.</summary>
    public static string Host(ChatCloud cloud) => cloud switch
    {
        ChatCloud.UsGov => "gov.teams.microsoft.us",
        ChatCloud.UsGovDoD => "dod.teams.microsoft.us",
        _ => "teams.microsoft.com",
    };
}
