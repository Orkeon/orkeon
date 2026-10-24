using System.Collections.Immutable;
using MimeKit;
using Orkeon.Tools.Email.Mailboxes;

namespace Orkeon.Tools.Email.Trial;

/// <summary>An in-memory mailbox and sender: a trial's fictitious account. It never leaves the process.</summary>
internal sealed class TrialMailbox : IMailbox, IMailSender
{
    private const string CursorPrefix = "t:";

    private static readonly string[] DefaultFolders = ["Inbox", "Sent", "Drafts", "Archive", "Trash"];

    private readonly object _gate;
    private readonly List<string> _folders;
    private readonly List<Entry> _entries;
    private readonly Counter _ids;
    private readonly Action<SimulatedMailAction> _record;
    private readonly string _account;

    public TrialMailbox(TrialMailboxSeed seed, Action<SimulatedMailAction> record)
    {
        _gate = new object();
        _folders = [.. DefaultFolders];
        _entries = [];
        _ids = new Counter();
        _record = record;
        _account = "";
        foreach (var message in seed.Messages)
        {
            var folder = EnsureFolder(message.Folder);
            Add(folder, message, StartsSeen(folder));
        }
    }

    private TrialMailbox(TrialMailbox shared, string account)
    {
        _gate = shared._gate;
        _folders = shared._folders;
        _entries = shared._entries;
        _ids = shared._ids;
        _record = shared._record;
        _account = account;
    }

    /// <summary>The same mailbox state, whose recorded actions carry <paramref name="account"/>.</summary>
    public TrialMailbox ForAccount(string account) => new(this, account);

    public MailboxCapabilities Capabilities =>
        MailboxCapabilities.Folders | MailboxCapabilities.Move | MailboxCapabilities.Flags
        | MailboxCapabilities.Drafts | MailboxCapabilities.BodySearch | MailboxCapabilities.Trash;

    public Task<IReadOnlyList<MailFolderInfo>> ListFoldersAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<MailFolderInfo>>([.. _folders.Select(Info)]);
    }

    public Task<(MailFolderInfo Folder, bool Created)> CreateFolderAsync(string path, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var existing = FindFolder(path);
            if (existing is not null)
                return Task.FromResult((Info(existing), false));

            var cut = path.LastIndexOf('/');
            if (cut > 0 && FindFolder(path[..cut]) is null)
                throw new EmailToolException(EmailErrorCode.FolderNotFound, $"The parent folder '{path[..cut]}' does not exist: create it first.");

            _folders.Add(path);
            Record(new SimulatedMailAction { Kind = SimulatedMailActionKind.CreateFolder, Folder = path });
            return Task.FromResult((Info(path), true));
        }
    }

    public Task<MailFolderInfo> RenameFolderAsync(string path, string newName, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var folder = RequireFolder(path);
            if (RoleOf(folder) is { } role)
                throw new EmailToolException(EmailErrorCode.InvalidRequest, $"The {role} folder is a system folder and cannot be renamed.");

            var cut = folder.LastIndexOf('/');
            var name = newName.Trim();
            if (name.Length == 0 || name.Contains('/', StringComparison.Ordinal))
                throw new EmailToolException(EmailErrorCode.InvalidRequest, "`new_name` is a single folder name, without '/'.");

            var renamed = cut < 0 ? name : $"{folder[..cut]}/{name}";
            if (FindFolder(renamed) is not null)
                throw new EmailToolException(EmailErrorCode.InvalidRequest, $"A folder '{renamed}' already exists.");

            for (var i = 0; i < _folders.Count; i++)
                _folders[i] = Rebase(_folders[i], folder, renamed);
            foreach (var entry in _entries)
                entry.Folder = Rebase(entry.Folder, folder, renamed);
            Record(new SimulatedMailAction { Kind = SimulatedMailActionKind.RenameFolder, Folder = folder, Target = renamed });
            return Task.FromResult(Info(renamed));
        }
    }

    public Task<MessagePage> SearchAsync(MailSearch search, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(search.RawQuery))
            throw new EmailToolException(EmailErrorCode.Unsupported, "The trial mailbox cannot run a `raw_query`: search with the other filters.");

        lock (_gate)
        {
            var folder = RequireFolder(search.Folder);
            var matches = _entries
                .Where(e => e.Folder == folder && Matches(e, search))
                .OrderByDescending(e => e.Data.Date)
                .ToList();
            var offset = ParseCursor(search.Cursor);
            var limit = Math.Max(search.Limit, 1);
            var page = matches.Skip(offset).Take(limit).Select((e, i) => Summary(e, offset + i + 1)).ToList();
            var next = offset + limit < matches.Count ? Cursor(offset + limit) : null;
            return Task.FromResult(new MessagePage(page, next, matches.Count));
        }
    }

    public Task<FetchedMessage> GetMessageAsync(string id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var entry = Require(id);
            return Task.FromResult(new FetchedMessage(entry.Id, entry.Folder, ToMime(entry.Data), entry.Seen, entry.Flagged));
        }
    }

    public Task<IReadOnlyList<MovedMessage>> MoveAsync(IReadOnlyList<string> ids, string destination, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var folder = RequireFolder(destination);
            var entries = ids.Select(Require).ToList();
            foreach (var entry in entries)
            {
                Record(new SimulatedMailAction { Kind = SimulatedMailActionKind.Move, MessageId = entry.Id, Folder = entry.Folder, Target = folder });
                entry.Folder = folder;
            }

            return Task.FromResult<IReadOnlyList<MovedMessage>>([.. entries.Select(e => new MovedMessage(e.Id, null))]);
        }
    }

    public Task<int> SetFlagsAsync(IReadOnlyList<string> ids, bool? seen, bool? flagged, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var entries = ids.Select(Require).ToList();
            foreach (var entry in entries)
            {
                entry.Seen = seen ?? entry.Seen;
                entry.Flagged = flagged ?? entry.Flagged;
                Record(new SimulatedMailAction { Kind = SimulatedMailActionKind.Mark, MessageId = entry.Id, Folder = entry.Folder, Target = Marks(seen, flagged) });
            }

            return Task.FromResult(entries.Count);
        }
    }

    public Task<DeleteOutcome> DeleteAsync(IReadOnlyList<string> ids, bool permanent, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var entries = ids.Select(Require).ToList();
            if (permanent)
            {
                foreach (var entry in entries)
                {
                    _entries.Remove(entry);
                    Record(new SimulatedMailAction { Kind = SimulatedMailActionKind.Delete, MessageId = entry.Id, Folder = entry.Folder, Target = "permanent" });
                }

                return Task.FromResult(new DeleteOutcome(entries.Count, true, null)
                {
                    Messages = [.. entries.Select(e => new MovedMessage(e.Id, null))],
                });
            }

            var trash = RoleFolder(FolderRoles.Trash);
            foreach (var entry in entries)
            {
                Record(new SimulatedMailAction { Kind = SimulatedMailActionKind.Delete, MessageId = entry.Id, Folder = entry.Folder, Target = trash });
                entry.Folder = trash;
            }

            return Task.FromResult(new DeleteOutcome(entries.Count, false, trash)
            {
                Messages = [.. entries.Select(e => new MovedMessage(e.Id, e.Id))],
            });
        }
    }

    public Task<(string? Id, string Folder)> SaveDraftAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var drafts = RoleFolder(FolderRoles.Drafts);
            var entry = Add(drafts, ToData(message, drafts), true);
            Record(Written(SimulatedMailActionKind.Draft, entry, [.. message.To.Mailboxes.Concat(message.Cc.Mailboxes).Select(a => a.Address)]));
            return Task.FromResult<(string?, string)>((entry.Id, drafts));
        }
    }

    // The send already filed its copy: a second one would double the Sent folder.
    public Task AppendToSentAsync(MimeMessage message, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<SendReceipt> SendAsync(MimeMessage message, MailboxAddress sender, IReadOnlyList<MailboxAddress> recipients, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var sent = RoleFolder(FolderRoles.Sent);
            var entry = Add(sent, ToData(message, sent), true);
            Record(Written(SimulatedMailActionKind.Send, entry, [.. recipients.Select(a => a.Address)]));
            return Task.FromResult(new SendReceipt("250 simulated by the trial"));
        }
    }

    private static string? RoleOf(string folder) => folder.Contains('/', StringComparison.Ordinal) ? null : FolderRoles.Parse(folder);

    private static bool StartsSeen(string folder) => RoleOf(folder) is FolderRoles.Sent or FolderRoles.Drafts;

    private static string Rebase(string path, string from, string to)
    {
        if (path == from)
            return to;
        return path.StartsWith(from + "/", StringComparison.Ordinal) ? to + path[from.Length..] : path;
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool Matches(Entry entry, MailSearch search)
    {
        var data = entry.Data;
        return (!search.UnreadOnly || !entry.Seen)
            && (!search.FlaggedOnly || entry.Flagged)
            && (search.From is null || Contains(data.From, search.From))
            && (search.To is null || data.To.Any(t => Contains(t, search.To)))
            && (search.Subject is null || Contains(data.Subject, search.Subject))
            && (search.Text is null || Contains(data.Subject, search.Text) || Contains(data.Body, search.Text))
            && (search.Since is null || data.Date >= search.Since)
            && (search.Before is null || data.Date < search.Before)
            && (search.HasAttachments is null || (data.Attachments.Count > 0) == search.HasAttachments);
    }

    private static int ParseCursor(string? cursor)
    {
        if (cursor is null)
            return 0;
        if (cursor.StartsWith(CursorPrefix, StringComparison.Ordinal)
            && int.TryParse(cursor.AsSpan(CursorPrefix.Length), out var offset) && offset >= 0)
            return offset;
        throw Cursors.Refused(cursor, $"{CursorPrefix}<number>");
    }

    private static string Cursor(int offset) => $"{CursorPrefix}{offset}";

    private static MessageSummaryInfo Summary(Entry entry, int resumeOffset) => new()
    {
        Id = entry.Id,
        From = entry.Data.From,
        Subject = entry.Data.Subject,
        Date = entry.Data.Date,
        Seen = entry.Seen,
        Flagged = entry.Flagged,
        HasAttachments = entry.Data.Attachments.Count > 0,
        Preview = Previews.Shorten(entry.Data.Body),
        ResumeCursor = Cursor(resumeOffset),
    };

    private static MimeMessage ToMime(TrialMailMessage data)
    {
        var message = new MimeMessage { Subject = data.Subject, Date = data.Date };
        message.From.AddRange(Addresses(data.From));
        foreach (var to in data.To)
            message.To.AddRange(Addresses(to));
        var builder = new BodyBuilder { TextBody = data.Body };
        foreach (var attachment in data.Attachments)
            builder.Attachments.Add(attachment.Name, attachment.Content, ContentType.Parse(attachment.MediaType));
        message.Body = builder.ToMessageBody();
        return message;
    }

    private static InternetAddressList Addresses(string text) =>
        InternetAddressList.TryParse(text, out var list) ? list : [];

    private static TrialMailMessage ToData(MimeMessage message, string folder)
    {
        var attachments = new List<TrialAttachment>();
        foreach (var part in message.Attachments.OfType<MimePart>())
        {
            using var content = new MemoryStream();
            part.Content?.DecodeTo(content);
            attachments.Add(new TrialAttachment
            {
                Name = part.FileName ?? "attachment",
                MediaType = part.ContentType.MimeType,
                Content = content.ToArray(),
            });
        }

        return new TrialMailMessage
        {
            Folder = folder,
            From = message.From.ToString(),
            To = [.. message.To.Concat(message.Cc).Select(a => a.ToString())],
            Subject = message.Subject ?? "",
            Body = message.TextBody ?? "",
            Date = message.Date,
            Attachments = [.. attachments],
        };
    }

    private static string Marks(bool? seen, bool? flagged) =>
        string.Join(',', new[] { Mark(seen, "seen", "unseen"), Mark(flagged, "flagged", "unflagged") }.OfType<string>());

    /// <summary><paramref name="set"/> or <paramref name="cleared"/> for a flag given, null for one left alone.</summary>
    private static string? Mark(bool? value, string set, string cleared)
    {
        if (value is not { } on)
            return null;
        return on ? set : cleared;
    }

    private static SimulatedMailAction Written(SimulatedMailActionKind kind, Entry entry, ImmutableList<string> recipients) => new()
    {
        Kind = kind,
        Recipients = recipients,
        Subject = entry.Data.Subject,
        Body = entry.Data.Body,
        MessageId = entry.Id,
        Folder = entry.Folder,
    };

    private void Record(SimulatedMailAction action) => _record(action with { Account = _account });

    private MailFolderInfo Info(string path)
    {
        var inFolder = _entries.Where(e => e.Folder == path).ToList();
        return new MailFolderInfo(path, path[(path.LastIndexOf('/') + 1)..], RoleOf(path), inFolder.Count, inFolder.Count(e => !e.Seen));
    }

    private string? FindFolder(string path) =>
        _folders.FirstOrDefault(f => f.Equals(path.Trim(), StringComparison.OrdinalIgnoreCase));

    private string RoleFolder(string role) => _folders.First(f => RoleOf(f) == role);

    private string RequireFolder(string pathOrRole)
    {
        var role = FolderRoles.Parse(pathOrRole);
        var found = role is null ? FindFolder(pathOrRole) : _folders.FirstOrDefault(f => RoleOf(f) == role);
        return found ?? throw new EmailToolException(EmailErrorCode.FolderNotFound, $"There is no folder '{pathOrRole}' in this mailbox.");
    }

    private string EnsureFolder(string path)
    {
        var existing = FindFolder(path);
        if (existing is not null)
            return existing;
        _folders.Add(path);
        return path;
    }

    private Entry Require(string id) =>
        _entries.FirstOrDefault(e => e.Id == id)
        ?? throw new EmailToolException(EmailErrorCode.MessageNotFound, $"There is no message '{id}' in this mailbox.");

    private Entry Add(string folder, TrialMailMessage data, bool seen)
    {
        var entry = new Entry($"m{++_ids.Last}", folder, data, seen);
        _entries.Add(entry);
        return entry;
    }

    private sealed class Counter
    {
        public int Last { get; set; }
    }

    private sealed class Entry
    {
        public Entry(string id, string folder, TrialMailMessage data, bool seen)
        {
            Id = id;
            Folder = folder;
            Data = data;
            Seen = seen;
        }

        public string Id { get; }

        public string Folder { get; set; }

        public TrialMailMessage Data { get; }

        public bool Seen { get; set; }

        public bool Flagged { get; set; }
    }
}
