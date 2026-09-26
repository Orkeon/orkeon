using System.Globalization;
using MailKit.Net.Pop3;
using MimeKit;
using Orkeon.Tools.Email.Auth;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Constants;

namespace Orkeon.Tools.Email.Mailboxes.Pop3;

/// <summary>
/// A POP3 mailbox: the inbox, read and deleted, nothing else. POP3 has no folders, no marks and
/// no server search, so a search scans the newest messages' headers client-side and everything
/// else is refused with a message that says so. Each operation opens its own session: POP3
/// locks the maildrop while connected, and deletions are committed at QUIT.
/// </summary>
internal sealed class Pop3Mailbox : IMailbox
{
    private const string CursorPrefix = "o:";
    private const string SwitchToImap = "switch the account to IMAP (Incoming:Protocol Imap)";
    private const string NoFolders = "has no folders: " + SwitchToImap + " for that";
    private const string NoMarks = "has no read or flagged marks: " + SwitchToImap + " for that";

    private readonly ResolvedEmailAccount _account;
    private readonly MailEndpoint _endpoint;
    private readonly IMailServiceConnector _connector;
    private readonly EmailCredentialProvider _credentials;

    /// <summary>Creates the mailbox of <paramref name="account"/>.</summary>
    public Pop3Mailbox(ResolvedEmailAccount account, IMailServiceConnector connector, EmailCredentialProvider credentials)
    {
        ArgumentNullException.ThrowIfNull(account);
        _account = account;
        _endpoint = account.IncomingEndpoint
            ?? throw new ArgumentException("A POP3 account needs an incoming endpoint.", nameof(account));
        _connector = connector;
        _credentials = credentials;
    }

    /// <inheritdoc />
    public MailboxCapabilities Capabilities => MailboxCapabilities.None;

    /// <inheritdoc />
    public Task<IReadOnlyList<MailFolderInfo>> ListFoldersAsync(CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<MailFolderInfo>>(async client =>
        {
            var count = await client.GetMessageCountAsync(cancellationToken).ConfigureAwait(false);
            return [new MailFolderInfo("INBOX", "INBOX", FolderRoles.Inbox, count, null)];
        }, cancellationToken);

    /// <inheritdoc />
    public Task<(MailFolderInfo Folder, bool Created)> CreateFolderAsync(string path, CancellationToken cancellationToken) =>
        throw Unsupported(NoFolders);

    /// <inheritdoc />
    public Task<MailFolderInfo> RenameFolderAsync(string path, string newName, CancellationToken cancellationToken) =>
        throw Unsupported(NoFolders);

    /// <inheritdoc />
    public Task<MessagePage> SearchAsync(MailSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        RequireInbox(search.Folder);
        if (search.UnreadOnly || search.FlaggedOnly)
            throw Unsupported(NoMarks);
        if (!string.IsNullOrWhiteSpace(search.Text) || search.HasAttachments is not null || !string.IsNullOrWhiteSpace(search.RawQuery))
            throw Unsupported("cannot search message bodies (`text`, `has_attachments`, `raw_query`): search on from, to, subject and dates, or " + SwitchToImap);

        return RunAsync(async client =>
        {
            var uids = await client.GetMessageUidsAsync(cancellationToken).ConfigureAwait(false);
            var skip = ParseCursor(search.Cursor);
            var page = new List<MessageSummaryInfo>(search.Limit);
            var scanned = 0;
            var position = skip;

            for (var index = uids.Count - 1 - skip; index >= 0 && page.Count < search.Limit && scanned < EmailDefaults.Pop3ScanWindow; index--)
            {
                scanned++;
                position++;
                var headers = await client.GetMessageHeadersAsync(index, cancellationToken).ConfigureAwait(false);
                var summary = Summarize(uids[index], headers) with
                {
                    ResumeCursor = string.Create(CultureInfo.InvariantCulture, $"{CursorPrefix}{position}"),
                };
                if (Matches(search, headers, summary))
                    page.Add(summary);
            }

            var cursor = position < uids.Count && (page.Count == search.Limit || scanned >= EmailDefaults.Pop3ScanWindow)
                ? string.Create(CultureInfo.InvariantCulture, $"{CursorPrefix}{position}")
                : null;
            return new MessagePage(page, cursor);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<FetchedMessage> GetMessageAsync(string id, CancellationToken cancellationToken)
    {
        var uid = MessageIds.ParsePop3(id);
        return RunAsync(async client =>
        {
            var index = await IndexOfAsync(client, uid, cancellationToken).ConfigureAwait(false);
            var message = await client.GetMessageAsync(index, cancellationToken).ConfigureAwait(false);
            return new FetchedMessage(id, "INBOX", message, null, null);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MovedMessage>> MoveAsync(IReadOnlyList<string> ids, string destination, CancellationToken cancellationToken) =>
        throw Unsupported(NoFolders);

    /// <inheritdoc />
    public Task<int> SetFlagsAsync(IReadOnlyList<string> ids, bool? seen, bool? flagged, CancellationToken cancellationToken) =>
        throw Unsupported(NoMarks);

    /// <inheritdoc />
    public Task<DeleteOutcome> DeleteAsync(IReadOnlyList<string> ids, bool permanent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (!permanent)
            throw Unsupported("has no trash and deletes for good: pass `permanent: true` (needs the Purge right)");

        var uids = ids.Select(MessageIds.ParsePop3).Distinct(StringComparer.Ordinal).ToList();
        return RunAsync(async client =>
        {
            // One UIDL before the first DELE: an RFC 1939 server stops listing a message once it is
            // marked deleted, and MailKit refuses the gapped listing a second UIDL would return.
            var listed = await client.GetMessageUidsAsync(cancellationToken).ConfigureAwait(false);
            var indexes = uids.Select(uid => IndexOf(listed, uid)).ToList();
            foreach (var index in indexes)
                await client.DeleteMessageAsync(index, cancellationToken).ConfigureAwait(false);

            return new DeleteOutcome(uids.Count, true, null);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<(string? Id, string Folder)> SaveDraftAsync(MimeMessage message, CancellationToken cancellationToken) =>
        throw Unsupported("has no drafts folder: " + SwitchToImap + " for that");

    /// <inheritdoc />
    public Task AppendToSentAsync(MimeMessage message, CancellationToken cancellationToken) =>
        throw Unsupported("has no Sent folder to file a copy in: an operator sets SaveSentCopy to false, or " + SwitchToImap);

    private async Task<T> RunAsync<T>(Func<Pop3Client, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var client = new Pop3Client();
        try
        {
            await MailKitSessions.OpenAsync(client, _endpoint, _account, _connector, _credentials, cancellationToken).ConfigureAwait(false);
            var result = await operation(client).ConfigureAwait(false);
            // QUIT commits the deletions; a session closed without it rolls them back.
            await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex) when (MailKitSessions.Translate(ex, _account, _endpoint) is { } translated)
        {
            throw translated;
        }
    }

    private static async Task<int> IndexOfAsync(Pop3Client client, string uid, CancellationToken cancellationToken) =>
        IndexOf(await client.GetMessageUidsAsync(cancellationToken).ConfigureAwait(false), uid);

    private static int IndexOf(IList<string> uids, string uid)
    {
        var index = uids.IndexOf(uid);
        if (index < 0)
            throw new EmailToolException(EmailErrorCode.MessageNotFound, "The message is no longer on the server: search again.");
        return index;
    }

    private static MessageSummaryInfo Summarize(string uid, HeaderList headers)
    {
        var from = headers[HeaderId.From];
        var date = headers[HeaderId.Date];
        return new MessageSummaryInfo
        {
            Id = MessageIds.Pop3(uid),
            From = from is null ? string.Empty : FormatFirst(from),
            Subject = headers[HeaderId.Subject] ?? string.Empty,
            Date = date is not null && MimeKit.Utils.DateUtils.TryParse(date, out var parsed) ? parsed : null,
        };
    }

    private static string FormatFirst(string header) =>
        InternetAddressList.TryParse(header, out var list) && list.Mailboxes.FirstOrDefault() is { } first
            ? Mime.MimeMessageReader.Format(first)
            : header;

    private static bool Matches(MailSearch search, HeaderList headers, MessageSummaryInfo summary)
    {
        if (!string.IsNullOrWhiteSpace(search.From) && !summary.From.Contains(search.From.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(search.To) && !(headers[HeaderId.To] ?? string.Empty).Contains(search.To.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(search.Subject) && !summary.Subject.Contains(search.Subject.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (search.Since is { } since && (summary.Date is not { } sinceDate || sinceDate < since))
            return false;
        return search.Before is not { } before || (summary.Date is { } beforeDate && beforeDate < before);
    }

    private static int ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;
        if (cursor.StartsWith(CursorPrefix, StringComparison.Ordinal)
            && int.TryParse(cursor.AsSpan(CursorPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var skip))
        {
            return skip;
        }

        throw new EmailToolException(EmailErrorCode.InvalidRequest, "`cursor` is not a cursor of this account: pass `next_cursor` exactly as a previous page returned it.");
    }

    private void RequireInbox(string folder)
    {
        if (FolderRoles.Parse(folder) != FolderRoles.Inbox && !folder.Trim().Equals("INBOX", StringComparison.OrdinalIgnoreCase))
            throw Unsupported("only has the inbox: " + SwitchToImap + " for other folders");
    }

    /// <summary>A refusal; <paramref name="clause"/> completes "…reads mail over POP3, which …".</summary>
    private EmailToolException Unsupported(string clause) =>
        new(EmailErrorCode.Unsupported, $"E-mail account '{_account.Name}' reads mail over POP3, which {clause}.");
}
