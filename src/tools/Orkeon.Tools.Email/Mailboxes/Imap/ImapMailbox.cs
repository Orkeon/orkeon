using System.Globalization;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;
using Orkeon.Tools.Email.Auth;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Constants;

namespace Orkeon.Tools.Email.Mailboxes.Imap;

/// <summary>
/// An IMAP mailbox over one pooled MailKit connection per account. MailKit clients are not
/// thread-safe, so every operation holds the account's gate; a connection idle for a while, or
/// broken (a cancelled command makes MailKit drop it), is replaced on the next call.
/// </summary>
internal sealed partial class ImapMailbox : IMailbox, IAsyncDisposable, IDisposable
{
    private const string CursorPrefix = "u:";
    private const int MaxBatch = 100;
    private static readonly TimeSpan IdleReconnect = TimeSpan.FromMinutes(5);

    /// <summary>The roles a server that flags no folder has by folder name (see <see cref="ConventionalNames"/>).</summary>
    private static readonly string[] ConventionalRoles = [FolderRoles.Sent, FolderRoles.Drafts, FolderRoles.Trash, FolderRoles.Junk, FolderRoles.Archive];

    private const MessageSummaryItems SummaryItems =
        MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags
        | MessageSummaryItems.InternalDate | MessageSummaryItems.BodyStructure | MessageSummaryItems.PreviewText;

    private readonly ResolvedEmailAccount _account;
    private readonly MailEndpoint _endpoint;
    private readonly IMailServiceConnector _connector;
    private readonly EmailCredentialProvider _credentials;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ImapClient? _client;
    private DateTimeOffset _lastUse;

    /// <summary>Creates the mailbox of <paramref name="account"/>.</summary>
    public ImapMailbox(ResolvedEmailAccount account, IMailServiceConnector connector, EmailCredentialProvider credentials, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(account);
        _account = account;
        _endpoint = account.IncomingEndpoint
            ?? throw new ArgumentException("An IMAP account needs an incoming endpoint.", nameof(account));
        _connector = connector;
        _credentials = credentials;
        _time = time;
    }

    /// <inheritdoc />
    public MailboxCapabilities Capabilities =>
        MailboxCapabilities.Folders | MailboxCapabilities.Move | MailboxCapabilities.Flags | MailboxCapabilities.Drafts
        | MailboxCapabilities.BodySearch | MailboxCapabilities.Trash
        | (_account.Provider == EmailProvider.Gmail ? MailboxCapabilities.RawQuery : MailboxCapabilities.None);

    /// <inheritdoc />
    public Task<IReadOnlyList<MailFolderInfo>> ListFoldersAsync(CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<MailFolderInfo>>(async client =>
        {
            var folders = await client.GetFoldersAsync(
                client.PersonalNamespaces[0], StatusItems.Count | StatusItems.Unread, false, cancellationToken).ConfigureAwait(false);
            return folders
                .Where(folder => !folder.Attributes.HasFlag(FolderAttributes.NonExistent))
                .Select(folder => Describe(client, folder))
                .OrderBy(folder => folder.Role == FolderRoles.Inbox ? 0 : 1)
                .ThenBy(folder => folder.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);

    /// <inheritdoc />
    public Task<(MailFolderInfo Folder, bool Created)> CreateFolderAsync(string path, CancellationToken cancellationToken) =>
        RunAsync(async client =>
        {
            var segments = SplitPath(path);
            var existing = await TryGetFolderAsync(client, string.Join('/', segments), cancellationToken).ConfigureAwait(false);
            if (existing is not null)
                return (Describe(client, existing), false);

            var parent = RootFolder(client);
            for (var depth = 0; depth < segments.Length; depth++)
            {
                var current = await TryGetFolderAsync(client, string.Join('/', segments[..(depth + 1)]), cancellationToken).ConfigureAwait(false);
                parent = current
                    ?? await parent.CreateAsync(segments[depth], true, cancellationToken).ConfigureAwait(false)
                    ?? throw new EmailToolException(EmailErrorCode.ServerError, $"The server did not create the folder '{segments[depth]}'.");
            }

            return (Describe(client, parent), true);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<MailFolderInfo> RenameFolderAsync(string path, string newName, CancellationToken cancellationToken) =>
        RunAsync(async client =>
        {
            var folder = await ResolveFolderAsync(client, path, cancellationToken).ConfigureAwait(false);
            if (RoleOf(client, folder) is { } role)
                throw new EmailToolException(EmailErrorCode.InvalidRequest, $"The {role} folder is a system folder and cannot be renamed.");

            var name = newName.Trim();
            if (name.Length == 0 || name.Contains('/', StringComparison.Ordinal) || name.Contains(folder.DirectorySeparator, StringComparison.Ordinal))
                throw new EmailToolException(EmailErrorCode.InvalidRequest, "`new_name` is a single folder name, without '/'.");

            await folder.RenameAsync(folder.ParentFolder ?? RootFolder(client), name, cancellationToken).ConfigureAwait(false);
            return Describe(client, folder);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<MessagePage> SearchAsync(MailSearch search, CancellationToken cancellationToken) =>
        RunAsync(async client =>
        {
            var folder = await ResolveFolderAsync(client, search.Folder, cancellationToken).ConfigureAwait(false);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);

            var uids = await folder.SearchAsync(BuildQuery(client, search), cancellationToken).ConfigureAwait(false);
            var below = ParseCursor(search.Cursor);
            var ordered = uids.Where(uid => uid.Id < below).OrderByDescending(uid => uid.Id).ToList();
            return await CollectPageAsync(folder, ordered, search, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<FetchedMessage> GetMessageAsync(string id, CancellationToken cancellationToken) =>
        RunAsync(async client =>
        {
            var target = MessageIds.ParseImap(id);
            var (folder, uid) = await OpenMessageFolderAsync(client, target, FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);
            var summaries = await folder.FetchAsync([uid], new FetchRequest(MessageSummaryItems.UniqueId | MessageSummaryItems.Flags), cancellationToken).ConfigureAwait(false);
            if (summaries.Count == 0)
                throw MessageGone();

            var flags = summaries[0].Flags ?? MessageFlags.None;
            var message = await folder.GetMessageAsync(uid, cancellationToken).ConfigureAwait(false);
            return new FetchedMessage(id, DisplayPath(folder), message, flags.HasFlag(MessageFlags.Seen), flags.HasFlag(MessageFlags.Flagged));
        }, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<MovedMessage>> MoveAsync(IReadOnlyList<string> ids, string destination, CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<MovedMessage>>(async client =>
        {
            var target = await ResolveFolderAsync(client, destination, cancellationToken).ConfigureAwait(false);
            var moved = new List<MovedMessage>();
            foreach (var group in GroupByFolder(ids))
            {
                var (folder, uids) = await OpenGroupAsync(client, group, FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);
                if (string.Equals(folder.FullName, target.FullName, StringComparison.Ordinal))
                {
                    moved.AddRange(group.Select(item => new MovedMessage(item.Id, item.Id)));
                    continue;
                }

                var map = await folder.MoveToAsync(uids, target, cancellationToken).ConfigureAwait(false);
                foreach (var (item, uid) in group.Zip(uids))
                {
                    var index = map.Source.IndexOf(uid);
                    var newId = index >= 0 && index < map.Destination.Count
                        ? MessageIds.Imap(target.FullName, map.Destination[index].Validity, map.Destination[index].Id)
                        : null;
                    moved.Add(new MovedMessage(item.Id, newId));
                }
            }

            return moved;
        }, cancellationToken);

    /// <inheritdoc />
    public Task<int> SetFlagsAsync(IReadOnlyList<string> ids, bool? seen, bool? flagged, CancellationToken cancellationToken) =>
        RunAsync(async client =>
        {
            var updated = 0;
            foreach (var group in GroupByFolder(ids))
            {
                var (folder, uids) = await OpenGroupAsync(client, group, FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);
                if (seen is { } read)
                    await StoreAsync(folder, uids, read, MessageFlags.Seen, cancellationToken).ConfigureAwait(false);
                if (flagged is { } star)
                    await StoreAsync(folder, uids, star, MessageFlags.Flagged, cancellationToken).ConfigureAwait(false);
                updated += uids.Count;
            }

            return updated;
        }, cancellationToken);

    /// <inheritdoc />
    public Task<DeleteOutcome> DeleteAsync(IReadOnlyList<string> ids, bool permanent, CancellationToken cancellationToken) =>
        RunAsync(async client =>
        {
            var trash = await TryGetRoleFolderAsync(client, FolderRoles.Trash, cancellationToken).ConfigureAwait(false);
            if (permanent)
                return await PurgeAsync(client, ids, trash, cancellationToken).ConfigureAwait(false);

            if (trash is null)
            {
                throw new EmailToolException(
                    EmailErrorCode.Unsupported,
                    "This server declares no trash folder: delete for good with `permanent: true` (needs the Purge right).");
            }

            var count = 0;
            foreach (var group in GroupByFolder(ids))
            {
                var (folder, uids) = await OpenGroupAsync(client, group, FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);
                if (string.Equals(folder.FullName, trash.FullName, StringComparison.Ordinal))
                {
                    throw new EmailToolException(
                        EmailErrorCode.InvalidRequest,
                        "These messages are already in the trash: pass `permanent: true` to delete them for good (needs the Purge right).");
                }

                await folder.MoveToAsync(uids, trash, cancellationToken).ConfigureAwait(false);
                count += uids.Count;
            }

            return new DeleteOutcome(count, false, DisplayPath(trash));
        }, cancellationToken);

    /// <inheritdoc />
    public Task<(string? Id, string Folder)> SaveDraftAsync(MimeMessage message, CancellationToken cancellationToken) =>
        RunAsync(async client =>
        {
            var drafts = await TryGetRoleFolderAsync(client, FolderRoles.Drafts, cancellationToken).ConfigureAwait(false)
                ?? throw new EmailToolException(EmailErrorCode.Unsupported, "This account has no drafts folder.");
            var uid = await drafts.AppendAsync(new AppendRequest(message, MessageFlags.Draft | MessageFlags.Seen), cancellationToken).ConfigureAwait(false);
            var id = uid is { } appended ? MessageIds.Imap(drafts.FullName, appended.Validity, appended.Id) : null;
            return (id, DisplayPath(drafts));
        }, cancellationToken);

    /// <inheritdoc />
    public Task AppendToSentAsync(MimeMessage message, CancellationToken cancellationToken) =>
        RunAsync(async client =>
        {
            // Said, not skipped: the operator asked for copies (SaveSentCopy), and email_send
            // reports this as a warning next to the message it did send.
            var sent = await TryGetRoleFolderAsync(client, FolderRoles.Sent, cancellationToken).ConfigureAwait(false)
                ?? throw new EmailToolException(
                    EmailErrorCode.FolderNotFound,
                    "This account has no Sent folder to file a copy in: an operator creates one named Sent, or sets SaveSentCopy to false.");
            await sent.AppendAsync(new AppendRequest(message, MessageFlags.Seen), cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    /// <summary>Drops the pooled connection without a LOGOUT (synchronous container disposal).</summary>
    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
        _gate.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DropClientAsync().ConfigureAwait(false);
            _client?.Dispose();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task<T> RunAsync<T>(Func<ImapClient, Task<T>> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var client = await EnsureClientAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = await operation(client).ConfigureAwait(false);
                _lastUse = _time.GetUtcNow();
                return result;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ProtocolException || !client.IsConnected)
            {
                // MailKit drops the connection when a command is cancelled; a broken one is
                // replaced on the next call rather than reused half-open.
                await DropClientAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception ex) when (MailKitSessions.Translate(ex, _account, _endpoint) is { } translated)
        {
            throw translated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ImapClient> EnsureClientAsync(CancellationToken cancellationToken)
    {
        if (_client is { IsConnected: true, IsAuthenticated: true } && _time.GetUtcNow() - _lastUse < IdleReconnect)
            return _client;

        await DropClientAsync().ConfigureAwait(false);
        var client = new ImapClient();
        try
        {
            await MailKitSessions.OpenAsync(client, _endpoint, _account, _connector, _credentials, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;
        _lastUse = _time.GetUtcNow();
        return client;
    }

    private async Task DropClientAsync()
    {
        if (_client is not { } client)
            return;

        _client = null;
        try
        {
            if (client.IsConnected)
                await client.DisconnectAsync(true, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ProtocolException or CommandException or OperationCanceledException or System.Net.Sockets.SocketException)
        {
            // Best-effort goodbye on a connection that is being thrown away anyway.
        }
        finally
        {
            client.Dispose();
        }
    }

    private static SearchQuery BuildQuery(ImapClient client, MailSearch search)
    {
        var parts = new List<SearchQuery>();
        if (search.UnreadOnly)
            parts.Add(SearchQuery.NotSeen);
        if (search.FlaggedOnly)
            parts.Add(SearchQuery.Flagged);
        if (!string.IsNullOrWhiteSpace(search.From))
            parts.Add(SearchQuery.FromContains(search.From.Trim()));
        if (!string.IsNullOrWhiteSpace(search.To))
            parts.Add(SearchQuery.ToContains(search.To.Trim()));
        if (!string.IsNullOrWhiteSpace(search.Subject))
            parts.Add(SearchQuery.SubjectContains(search.Subject.Trim()));
        if (!string.IsNullOrWhiteSpace(search.Text))
            parts.Add(SearchQuery.SubjectContains(search.Text.Trim()).Or(SearchQuery.BodyContains(search.Text.Trim())));
        if (search.Since is { } since)
            parts.Add(SearchQuery.DeliveredAfter(since.UtcDateTime.Date));
        if (search.Before is { } before)
            parts.Add(SearchQuery.DeliveredBefore(before.UtcDateTime.Date));
        if (!string.IsNullOrWhiteSpace(search.RawQuery))
        {
            if (!client.Capabilities.HasFlag(ImapCapabilities.GMailExt1))
                throw new EmailToolException(EmailErrorCode.Unsupported, "`raw_query` needs a server with a native search language (Gmail's X-GM-RAW); use the other criteria.");
            parts.Add(SearchQuery.GMailRawSearch(search.RawQuery.Trim()));
        }

        return parts.Count == 0 ? SearchQuery.All : parts.Aggregate((left, right) => left.And(right));
    }

    private static async Task<MessagePage> CollectPageAsync(IMailFolder folder, List<UniqueId> ordered, MailSearch search, CancellationToken cancellationToken)
    {
        var page = new List<MessageSummaryInfo>(search.Limit);
        var batchSize = Math.Min(MaxBatch, search.HasAttachments is null ? search.Limit : search.Limit * 3);
        UniqueId? last = null;

        for (var start = 0; start < ordered.Count && page.Count < search.Limit; start += batchSize)
        {
            var batch = ordered.Skip(start).Take(batchSize).ToList();
            var summaries = await folder.FetchAsync(batch, new FetchRequest(SummaryItems), cancellationToken).ConfigureAwait(false);
            foreach (var summary in summaries.OrderByDescending(s => s.UniqueId.Id))
            {
                last = summary.UniqueId;
                var hasAttachments = summary.Attachments.Any();
                if (search.HasAttachments is { } wanted && wanted != hasAttachments)
                    continue;

                page.Add(Summarize(folder, summary, hasAttachments));
                if (page.Count == search.Limit)
                    break;
            }
        }

        var more = last is { } boundary && ordered.Exists(uid => uid.Id < boundary.Id) && page.Count == search.Limit;
        var cursor = more ? string.Create(CultureInfo.InvariantCulture, $"{CursorPrefix}{last!.Value.Id}") : null;
        return new MessagePage(page, cursor);
    }

    private static MessageSummaryInfo Summarize(IMailFolder folder, IMessageSummary summary, bool hasAttachments)
    {
        var flags = summary.Flags ?? MessageFlags.None;
        var sender = summary.Envelope?.From.Mailboxes.FirstOrDefault();
        return new MessageSummaryInfo
        {
            Id = MessageIds.Imap(folder.FullName, folder.UidValidity, summary.UniqueId.Id),
            From = sender is null ? string.Empty : Mime.MimeMessageReader.Format(sender),
            Subject = summary.Envelope?.Subject ?? string.Empty,
            Date = summary.Envelope?.Date ?? summary.InternalDate,
            Seen = flags.HasFlag(MessageFlags.Seen),
            Flagged = flags.HasFlag(MessageFlags.Flagged),
            HasAttachments = hasAttachments,
            Preview = Previews.Shorten(summary.PreviewText),
            ResumeCursor = string.Create(CultureInfo.InvariantCulture, $"{CursorPrefix}{summary.UniqueId.Id}"),
        };
    }

    private static uint ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return uint.MaxValue;
        if (cursor.StartsWith(CursorPrefix, StringComparison.Ordinal)
            && uint.TryParse(cursor.AsSpan(CursorPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var below))
        {
            return below;
        }

        throw new EmailToolException(EmailErrorCode.InvalidRequest, "`cursor` is not a cursor of this account: pass `next_cursor` exactly as a previous page returned it.");
    }

    private static async Task<DeleteOutcome> PurgeAsync(ImapClient client, IReadOnlyList<string> ids, IMailFolder? trash, CancellationToken cancellationToken)
    {
        if (!client.Capabilities.HasFlag(ImapCapabilities.UidPlus))
        {
            throw new EmailToolException(
                EmailErrorCode.Unsupported,
                "This server lacks UIDPLUS: a permanent delete could also remove other messages already marked deleted, so it is refused.");
        }

        var gmail = client.Capabilities.HasFlag(ImapCapabilities.GMailExt1);
        var count = 0;
        foreach (var group in GroupByFolder(ids))
        {
            var (folder, uids) = await OpenGroupAsync(client, group, FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);
            if (gmail && trash is not null && !string.Equals(folder.FullName, trash.FullName, StringComparison.Ordinal))
            {
                // Gmail archives what is expunged from a label; only the trash deletes for good.
                var map = await folder.MoveToAsync(uids, trash, cancellationToken).ConfigureAwait(false);
                await trash.OpenAsync(FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);
                await ExpungeAsync(trash, map.Destination, cancellationToken).ConfigureAwait(false);
                count += uids.Count;
                continue;
            }

            await ExpungeAsync(folder, uids, cancellationToken).ConfigureAwait(false);
            count += uids.Count;
        }

        return new DeleteOutcome(count, true, null);
    }

    private static async Task ExpungeAsync(IMailFolder folder, IList<UniqueId> uids, CancellationToken cancellationToken)
    {
        if (uids.Count == 0)
            return;
        await folder.StoreAsync(uids, new StoreFlagsRequest(StoreAction.Add, MessageFlags.Deleted) { Silent = true }, cancellationToken).ConfigureAwait(false);
        await folder.ExpungeAsync(uids, cancellationToken).ConfigureAwait(false);
    }

    private static async Task StoreAsync(IMailFolder folder, IList<UniqueId> uids, bool set, MessageFlags flag, CancellationToken cancellationToken) =>
        await folder.StoreAsync(uids, new StoreFlagsRequest(set ? StoreAction.Add : StoreAction.Remove, flag) { Silent = true }, cancellationToken)
            .ConfigureAwait(false);

    private static IMailFolder RootFolder(ImapClient client) =>
        client.GetFolder(client.PersonalNamespaces[0])
        ?? throw new EmailToolException(EmailErrorCode.ServerError, "The server exposes no personal folder namespace.");

    private static IEnumerable<List<(string Id, ImapMessageId Target)>> GroupByFolder(IReadOnlyList<string> ids) =>
        ids.Select(id => (Id: id, Target: MessageIds.ParseImap(id)))
            .GroupBy(item => item.Target.Folder, StringComparer.Ordinal)
            .Select(group => group.ToList());

    private static async Task<(IMailFolder Folder, IList<UniqueId> Uids)> OpenGroupAsync(
        ImapClient client, List<(string Id, ImapMessageId Target)> group, FolderAccess access, CancellationToken cancellationToken)
    {
        var (folder, _) = await OpenMessageFolderAsync(client, group[0].Target, access, cancellationToken).ConfigureAwait(false);
        if (group.Exists(item => item.Target.UidValidity != folder.UidValidity))
            throw MessageGone();
        return (folder, group.Select(item => new UniqueId(item.Target.UidValidity, item.Target.Uid)).ToList());
    }

    private static async Task<(IMailFolder Folder, UniqueId Uid)> OpenMessageFolderAsync(
        ImapClient client, ImapMessageId target, FolderAccess access, CancellationToken cancellationToken)
    {
        var folder = await client.GetFolderAsync(target.Folder, cancellationToken).ConfigureAwait(false);
        await folder.OpenAsync(access, cancellationToken).ConfigureAwait(false);
        if (folder.UidValidity != target.UidValidity)
            throw MessageGone();
        return (folder, new UniqueId(target.UidValidity, target.Uid));
    }

    private static EmailToolException MessageGone() =>
        new(EmailErrorCode.MessageNotFound, "The message is no longer where this id points (moved, deleted, or the folder was rebuilt): search again.");

    private static string[] SplitPath(string path)
    {
        var segments = path.Trim().Trim('/').Split('/', StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(segment => segment.Length == 0 || segment.Any(char.IsControl)))
            throw new EmailToolException(EmailErrorCode.InvalidRequest, $"'{path}' is not a folder path: segments separated by '/', none empty.");
        return segments;
    }

    private static async Task<IMailFolder> ResolveFolderAsync(ImapClient client, string folder, CancellationToken cancellationToken)
    {
        if (FolderRoles.Parse(folder) is { } role)
        {
            return await TryGetRoleFolderAsync(client, role, cancellationToken).ConfigureAwait(false)
                ?? throw new EmailToolException(EmailErrorCode.FolderNotFound, $"This account has no {role} folder: list them with email_folders.");
        }

        return await TryGetFolderAsync(client, string.Join('/', SplitPath(folder)), cancellationToken).ConfigureAwait(false)
            ?? throw new EmailToolException(EmailErrorCode.FolderNotFound, $"The folder '{folder}' does not exist: list them with email_folders.");
    }

    private static async Task<IMailFolder?> TryGetRoleFolderAsync(ImapClient client, string role, CancellationToken cancellationToken)
    {
        if (role == FolderRoles.Inbox)
            return client.Inbox;

        // MailKit throws rather than answer null when the server has neither SPECIAL-USE nor XLIST;
        // such a server is exactly the one the conventional names below are for.
        var special = !FlagsRoles(client) ? null : role switch
        {
            FolderRoles.Sent => client.GetFolder(SpecialFolder.Sent),
            FolderRoles.Drafts => client.GetFolder(SpecialFolder.Drafts),
            FolderRoles.Trash => client.GetFolder(SpecialFolder.Trash),
            FolderRoles.Junk => client.GetFolder(SpecialFolder.Junk),
            FolderRoles.Archive => client.GetFolder(SpecialFolder.Archive),
            FolderRoles.All => client.GetFolder(SpecialFolder.All),
            _ => null,
        };
        if (special is not null)
            return special;

        foreach (var name in ConventionalNames(role))
        {
            if (await TryGetFolderAsync(client, name, cancellationToken).ConfigureAwait(false) is { } found)
                return found;
        }

        // Gmail has no archive folder: archiving is leaving the inbox for All Mail, where the
        // message keeps its other labels.
        if (role == FolderRoles.Archive && client.Capabilities.HasFlag(ImapCapabilities.GMailExt1) && FlagsRoles(client))
            return client.GetFolder(SpecialFolder.All);

        return null;
    }

    private static async Task<IMailFolder?> TryGetFolderAsync(ImapClient client, string displayPath, CancellationToken cancellationToken)
    {
        if (displayPath.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
            return client.Inbox;

        var separator = client.PersonalNamespaces.Count > 0 ? client.PersonalNamespaces[0].DirectorySeparator : '/';
        var serverPath = separator is '/' or '\0' ? displayPath : displayPath.Replace('/', separator);
        try
        {
            return await client.GetFolderAsync(serverPath, cancellationToken).ConfigureAwait(false);
        }
        catch (FolderNotFoundException)
        {
            return null;
        }
    }

    private static string[] ConventionalNames(string role) => role switch
    {
        FolderRoles.Sent => ["Sent", "Sent Items", "Sent Messages", "INBOX/Sent"],
        FolderRoles.Drafts => ["Drafts", "INBOX/Drafts"],
        FolderRoles.Trash => ["Trash", "Deleted Items", "Deleted Messages", "INBOX/Trash"],
        FolderRoles.Junk => ["Junk", "Spam", "Junk E-mail", "INBOX/Junk"],
        FolderRoles.Archive => ["Archive", "Archives", "INBOX/Archive"],
        _ => [],
    };

    private static MailFolderInfo Describe(ImapClient client, IMailFolder folder)
    {
        var selectable = !folder.Attributes.HasFlag(FolderAttributes.NoSelect);
        return new MailFolderInfo(
            DisplayPath(folder),
            folder.Name,
            RoleOf(client, folder),
            selectable ? folder.Count : null,
            selectable ? folder.Unread : null);
    }

    private static string? RoleOf(ImapClient client, IMailFolder folder)
    {
        if (ReferenceEquals(folder, client.Inbox) || folder.FullName.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
            return FolderRoles.Inbox;

        // A server that flags no folder is named by convention: the same names the role lookup
        // tries, so a folder listed as "sent" is the one a search of role sent opens.
        if (!FlagsRoles(client))
            return Array.Find(ConventionalRoles, role => ConventionalNames(role).Contains(DisplayPath(folder), StringComparer.Ordinal));

        var attributes = folder.Attributes;
        return attributes switch
        {
            _ when attributes.HasFlag(FolderAttributes.Sent) => FolderRoles.Sent,
            _ when attributes.HasFlag(FolderAttributes.Drafts) => FolderRoles.Drafts,
            _ when attributes.HasFlag(FolderAttributes.Trash) => FolderRoles.Trash,
            _ when attributes.HasFlag(FolderAttributes.Junk) => FolderRoles.Junk,
            _ when attributes.HasFlag(FolderAttributes.Archive) => FolderRoles.Archive,
            _ when attributes.HasFlag(FolderAttributes.All) => FolderRoles.All,
            _ => null,
        };
    }

    /// <summary>Whether the server flags folder roles itself (SPECIAL-USE or XLIST).</summary>
    private static bool FlagsRoles(ImapClient client) =>
        (client.Capabilities & (ImapCapabilities.SpecialUse | ImapCapabilities.XList)) != 0;

    private static string DisplayPath(IMailFolder folder) =>
        folder.DirectorySeparator is '/' or '\0' ? folder.FullName : folder.FullName.Replace(folder.DirectorySeparator, '/');
}
