using MimeKit;
using Orkeon.Tools.Email.Configuration;

namespace Orkeon.Tools.Email.Mailboxes;

/// <summary>What a mailbox backend can do. A tool asking for more gets an explicit refusal.</summary>
[Flags]
internal enum MailboxCapabilities
{
    /// <summary>Nothing beyond reading the inbox.</summary>
    None = 0,

    /// <summary>Several folders, created and renamed.</summary>
    Folders = 1,

    /// <summary>Messages move between folders.</summary>
    Move = 2,

    /// <summary>Read and flagged marks.</summary>
    Flags = 4,

    /// <summary>Drafts are saved in the mailbox.</summary>
    Drafts = 8,

    /// <summary>The server searches message bodies.</summary>
    BodySearch = 16,

    /// <summary>A provider-native query language (Gmail search, KQL).</summary>
    RawQuery = 32,

    /// <summary>Deleted messages go to a trash folder first.</summary>
    Trash = 64,
}

/// <summary>A folder as the tools present it: <c>/</c>-separated path, well-known role.</summary>
/// <param name="Path">Full path, <c>/</c>-separated whatever the server's separator.</param>
/// <param name="Name">Last segment.</param>
/// <param name="Role"><c>inbox</c>, <c>sent</c>, <c>drafts</c>, <c>trash</c>, <c>junk</c>, <c>archive</c>, <c>all</c>, or null.</param>
/// <param name="Total">Messages in the folder, when known.</param>
/// <param name="Unread">Unread messages, when known.</param>
internal sealed record MailFolderInfo(string Path, string Name, string? Role, int? Total, int? Unread);

/// <summary>A search as an agent phrases it.</summary>
internal sealed record MailSearch
{
    /// <summary>Folder path or role.</summary>
    public string Folder { get; init; } = FolderRoles.Inbox;

    /// <summary>Only unread messages.</summary>
    public bool UnreadOnly { get; init; }

    /// <summary>Only flagged (starred) messages.</summary>
    public bool FlaggedOnly { get; init; }

    /// <summary>Sender contains.</summary>
    public string? From { get; init; }

    /// <summary>Recipient contains.</summary>
    public string? To { get; init; }

    /// <summary>Subject contains.</summary>
    public string? Subject { get; init; }

    /// <summary>Subject or body contains.</summary>
    public string? Text { get; init; }

    /// <summary>Received on or after.</summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>Received before.</summary>
    public DateTimeOffset? Before { get; init; }

    /// <summary>Only messages with (true) or without (false) attachments.</summary>
    public bool? HasAttachments { get; init; }

    /// <summary>Provider-native query, ANDed with the rest.</summary>
    public string? RawQuery { get; init; }

    /// <summary>Page size.</summary>
    public int Limit { get; init; } = Constants.EmailDefaults.DefaultSearchLimit;

    /// <summary>Opaque cursor of the next page, as a previous page returned it.</summary>
    public string? Cursor { get; init; }
}

/// <summary>One message of a search page.</summary>
internal sealed record MessageSummaryInfo
{
    /// <summary>Opaque message id.</summary>
    public required string Id { get; init; }

    /// <summary>Sender, formatted.</summary>
    public required string From { get; init; }

    /// <summary>Subject.</summary>
    public required string Subject { get; init; }

    /// <summary>Date received (or sent), when known.</summary>
    public DateTimeOffset? Date { get; init; }

    /// <summary>Read mark; null when the backend has none (POP3).</summary>
    public bool? Seen { get; init; }

    /// <summary>Flagged mark; null when the backend has none.</summary>
    public bool? Flagged { get; init; }

    /// <summary>Whether it carries attachments, when known.</summary>
    public bool? HasAttachments { get; init; }

    /// <summary>First characters of the body, when known.</summary>
    public string? Preview { get; init; }
}

/// <summary>A page of search results.</summary>
/// <param name="Messages">Newest first.</param>
/// <param name="NextCursor">Cursor of the next page, or null on the last one.</param>
internal sealed record MessagePage(IReadOnlyList<MessageSummaryInfo> Messages, string? NextCursor);

/// <summary>A message fetched whole. Owns (and disposes) the MIME message.</summary>
internal sealed class FetchedMessage : IDisposable
{
    /// <summary>Creates the fetched message.</summary>
    public FetchedMessage(string id, string folder, MimeMessage message, bool? seen, bool? flagged)
    {
        Id = id;
        Folder = folder;
        Message = message;
        Seen = seen;
        Flagged = flagged;
    }

    /// <summary>Opaque id.</summary>
    public string Id { get; }

    /// <summary>Folder path.</summary>
    public string Folder { get; }

    /// <summary>The MIME message.</summary>
    public MimeMessage Message { get; }

    /// <summary>Read mark, when the backend has one.</summary>
    public bool? Seen { get; }

    /// <summary>Flagged mark, when the backend has one.</summary>
    public bool? Flagged { get; }

    /// <inheritdoc />
    public void Dispose() => Message.Dispose();
}

/// <summary>Where a moved message now is.</summary>
/// <param name="Id">Its id before the move.</param>
/// <param name="NewId">Its id after the move, when the server says (IMAP UIDPLUS, Graph).</param>
internal sealed record MovedMessage(string Id, string? NewId);

/// <summary>What a delete did.</summary>
/// <param name="Count">Messages deleted.</param>
/// <param name="Permanent">Whether they are gone for good.</param>
/// <param name="MovedTo">The trash folder they went to, when not permanent.</param>
internal sealed record DeleteOutcome(int Count, bool Permanent, string? MovedTo);

/// <summary>What a send returned.</summary>
/// <param name="ServerResponse">The server's acceptance line, when it gives one.</param>
internal sealed record SendReceipt(string? ServerResponse);

/// <summary>One account's mailbox. Every id it takes is an id it returned.</summary>
internal interface IMailbox
{
    /// <summary>What this backend can do.</summary>
    MailboxCapabilities Capabilities { get; }

    /// <summary>All folders.</summary>
    Task<IReadOnlyList<MailFolderInfo>> ListFoldersAsync(CancellationToken cancellationToken);

    /// <summary>Creates <paramref name="path"/> (parents must exist); returns it, created or already there.</summary>
    Task<(MailFolderInfo Folder, bool Created)> CreateFolderAsync(string path, CancellationToken cancellationToken);

    /// <summary>Renames the last segment of <paramref name="path"/>.</summary>
    Task<MailFolderInfo> RenameFolderAsync(string path, string newName, CancellationToken cancellationToken);

    /// <summary>Searches one folder.</summary>
    Task<MessagePage> SearchAsync(MailSearch search, CancellationToken cancellationToken);

    /// <summary>Fetches a message whole, without marking it read.</summary>
    Task<FetchedMessage> GetMessageAsync(string id, CancellationToken cancellationToken);

    /// <summary>Moves messages to <paramref name="destination"/> (path or role).</summary>
    Task<IReadOnlyList<MovedMessage>> MoveAsync(IReadOnlyList<string> ids, string destination, CancellationToken cancellationToken);

    /// <summary>Sets or clears the read and flagged marks; returns how many messages were updated.</summary>
    Task<int> SetFlagsAsync(IReadOnlyList<string> ids, bool? seen, bool? flagged, CancellationToken cancellationToken);

    /// <summary>Moves messages to the trash, or deletes them for good.</summary>
    Task<DeleteOutcome> DeleteAsync(IReadOnlyList<string> ids, bool permanent, CancellationToken cancellationToken);

    /// <summary>Saves <paramref name="message"/> as a draft; returns its id when the server gives one, and the drafts folder.</summary>
    Task<(string? Id, string Folder)> SaveDraftAsync(MimeMessage message, CancellationToken cancellationToken);

    /// <summary>Files a copy of a sent message in the Sent folder (for servers that do not).</summary>
    Task AppendToSentAsync(MimeMessage message, CancellationToken cancellationToken);
}

/// <summary>One account's way out.</summary>
internal interface IMailSender
{
    /// <summary>
    /// Sends <paramref name="message"/> to exactly <paramref name="recipients"/>, which the caller
    /// has checked against the allow-list: the envelope is never derived from the headers.
    /// </summary>
    Task<SendReceipt> SendAsync(MimeMessage message, MailboxAddress sender, IReadOnlyList<MailboxAddress> recipients, CancellationToken cancellationToken);
}

/// <summary>Hands out each account's mailbox and sender, one instance per account.</summary>
internal interface IMailboxProvider
{
    /// <summary>The mailbox of <paramref name="account"/>.</summary>
    IMailbox GetMailbox(ResolvedEmailAccount account);

    /// <summary>The sender of <paramref name="account"/>.</summary>
    IMailSender GetSender(ResolvedEmailAccount account);
}
