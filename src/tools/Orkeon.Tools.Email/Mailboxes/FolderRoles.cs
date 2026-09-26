namespace Orkeon.Tools.Email.Mailboxes;

/// <summary>
/// The well-known folder roles an agent can name instead of a path, whatever the provider calls
/// the folder (<c>[Gmail]/Sent Mail</c>, <c>Sent Items</c>, <c>sentitems</c>…).
/// </summary>
internal static class FolderRoles
{
    /// <summary>The inbox.</summary>
    public const string Inbox = "inbox";

    /// <summary>Sent mail.</summary>
    public const string Sent = "sent";

    /// <summary>Drafts.</summary>
    public const string Drafts = "drafts";

    /// <summary>Trash / deleted items.</summary>
    public const string Trash = "trash";

    /// <summary>Junk / spam.</summary>
    public const string Junk = "junk";

    /// <summary>Archive.</summary>
    public const string Archive = "archive";

    /// <summary>All mail (Gmail).</summary>
    public const string All = "all";

    /// <summary>The role a folder argument names, or null when it is a path.</summary>
    public static string? Parse(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return folder.Trim() switch
        {
            var f when f.Equals(Inbox, StringComparison.OrdinalIgnoreCase) => Inbox,
            var f when f.Equals(Sent, StringComparison.OrdinalIgnoreCase) => Sent,
            var f when f.Equals(Drafts, StringComparison.OrdinalIgnoreCase) => Drafts,
            var f when f.Equals(Trash, StringComparison.OrdinalIgnoreCase) => Trash,
            var f when f.Equals(Junk, StringComparison.OrdinalIgnoreCase) || f.Equals("spam", StringComparison.OrdinalIgnoreCase) => Junk,
            var f when f.Equals(Archive, StringComparison.OrdinalIgnoreCase) => Archive,
            var f when f.Equals(All, StringComparison.OrdinalIgnoreCase) => All,
            _ => null,
        };
    }
}
