using System.Globalization;
using System.Text;

namespace Orkeon.Tools.Email.Mailboxes;

/// <summary>An IMAP message address: folder, UIDVALIDITY epoch and UID.</summary>
/// <param name="Folder">Server-side full name of the folder.</param>
/// <param name="UidValidity">The folder's UIDVALIDITY when the id was issued.</param>
/// <param name="Uid">The message UID.</param>
internal sealed record ImapMessageId(string Folder, uint UidValidity, uint Uid);

/// <summary>
/// The opaque ids the tools hand out. An agent passes them back verbatim; each backend reads
/// only its own prefix, so an id from one account's backend is refused by another's.
/// </summary>
internal static class MessageIds
{
    private const string ImapPrefix = "imap:";
    private const string GraphPrefix = "graph:";
    private const string Pop3Prefix = "pop3:";

    /// <summary>An IMAP id.</summary>
    public static string Imap(string folder, uint uidValidity, uint uid) =>
        string.Create(CultureInfo.InvariantCulture, $"{ImapPrefix}{Encode(folder)}:{uidValidity}:{uid}");

    /// <summary>A Graph id.</summary>
    public static string Graph(string immutableId) => GraphPrefix + immutableId;

    /// <summary>A POP3 id.</summary>
    public static string Pop3(string uidl) => Pop3Prefix + uidl;

    /// <summary>Reads an IMAP id.</summary>
    public static ImapMessageId ParseImap(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (id.StartsWith(ImapPrefix, StringComparison.Ordinal))
        {
            var parts = id[ImapPrefix.Length..].Split(':');
            if (parts.Length == 3
                && TryDecode(parts[0], out var folder)
                && uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var validity)
                && uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var uid)
                && uid > 0)
            {
                return new ImapMessageId(folder, validity, uid);
            }
        }

        throw Invalid(id);
    }

    /// <summary>Reads a Graph id.</summary>
    public static string ParseGraph(string id) => ParseSuffix(id, GraphPrefix);

    /// <summary>Reads a POP3 id.</summary>
    public static string ParsePop3(string id) => ParseSuffix(id, Pop3Prefix);

    private static string ParseSuffix(string id, string prefix)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (id.StartsWith(prefix, StringComparison.Ordinal) && id.Length > prefix.Length)
            return id[prefix.Length..];
        throw Invalid(id);
    }

    private static EmailToolException Invalid(string id) =>
        new(EmailErrorCode.InvalidRequest,
            $"'{id}' is not a message id of this account: pass an id exactly as email_search returned it.");

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryDecode(string value, out string decoded)
    {
        decoded = string.Empty;
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        var buffer = new byte[base64.Length];
        if (!Convert.TryFromBase64String(base64, buffer, out var written))
            return false;
        decoded = Encoding.UTF8.GetString(buffer, 0, written);
        return decoded.Length > 0;
    }
}
