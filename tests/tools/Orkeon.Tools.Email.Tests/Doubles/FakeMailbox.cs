using System.Text;
using MimeKit;
using Orkeon.Tools.Email.Mailboxes;

namespace Orkeon.Tools.Email.Tests.Doubles;

/// <summary>
/// In-memory <see cref="IMailbox"/> for the tool tests: messages are raw MIME keyed by id,
/// every call is recorded, and the answers of search, move and delete are set by the test.
/// </summary>
internal sealed class FakeMailbox : IMailbox
{
    private readonly Dictionary<string, (byte[] Raw, string Folder, bool? Seen, bool? Flagged)> _messages = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public MailboxCapabilities Capabilities { get; set; } =
        MailboxCapabilities.Folders | MailboxCapabilities.Move | MailboxCapabilities.Flags | MailboxCapabilities.Drafts
        | MailboxCapabilities.BodySearch | MailboxCapabilities.Trash;

    /// <summary>What <see cref="ListFoldersAsync"/> answers.</summary>
    public List<MailFolderInfo> Folders { get; } = [new("INBOX", "INBOX", FolderRoles.Inbox, 3, 1)];

    /// <summary>What <see cref="SearchAsync"/> answers.</summary>
    public MessagePage NextPage { get; set; } = new([], null);

    /// <summary>The searches received.</summary>
    public List<MailSearch> Searches { get; } = [];

    /// <summary>The ids fetched with <see cref="GetMessageAsync"/>.</summary>
    public List<string> Fetched { get; } = [];

    /// <summary>Every MIME message handed out, to check that the caller disposed it.</summary>
    public List<MimeMessage> Issued { get; } = [];

    /// <summary>The folder paths created.</summary>
    public List<string> Created { get; } = [];

    /// <summary>The renames received.</summary>
    public List<(string Path, string NewName)> Renamed { get; } = [];

    /// <summary>The moves received.</summary>
    public List<(IReadOnlyList<string> Ids, string Destination)> Moves { get; } = [];

    /// <summary>The flag updates received.</summary>
    public List<(IReadOnlyList<string> Ids, bool? Seen, bool? Flagged)> FlagUpdates { get; } = [];

    /// <summary>The deletes received.</summary>
    public List<(IReadOnlyList<string> Ids, bool Permanent)> Deletes { get; } = [];

    /// <summary>The drafts saved, as MIME text.</summary>
    public List<string> Drafts { get; } = [];

    /// <summary>The sent copies filed, as MIME text.</summary>
    public List<string> SentCopies { get; } = [];

    /// <summary>When set, <see cref="AppendToSentAsync"/> throws it.</summary>
    public Exception? AppendToSentFailure { get; set; }

    /// <summary>Adds a message under <paramref name="id"/>.</summary>
    public FakeMailbox Add(string id, string rawMime, string folder = "INBOX", bool? seen = false, bool? flagged = false)
    {
        _messages[id] = (Encoding.UTF8.GetBytes(rawMime), folder, seen, flagged);
        return this;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MailFolderInfo>> ListFoldersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MailFolderInfo>>(Folders);

    /// <inheritdoc />
    public Task<(MailFolderInfo Folder, bool Created)> CreateFolderAsync(string path, CancellationToken cancellationToken)
    {
        Created.Add(path);
        var name = path[(path.LastIndexOf('/') + 1)..];
        return Task.FromResult((new MailFolderInfo(path, name, null, 0, 0), true));
    }

    /// <inheritdoc />
    public Task<MailFolderInfo> RenameFolderAsync(string path, string newName, CancellationToken cancellationToken)
    {
        Renamed.Add((path, newName));
        var normalized = path.Trim().Trim('/');
        var cut = normalized.LastIndexOf('/');
        var renamed = cut < 0 ? newName.Trim() : normalized[..(cut + 1)] + newName.Trim();
        return Task.FromResult(new MailFolderInfo(renamed, newName, null, 0, 0));
    }

    /// <inheritdoc />
    public Task<MessagePage> SearchAsync(MailSearch search, CancellationToken cancellationToken)
    {
        Searches.Add(search);
        return Task.FromResult(NextPage);
    }

    /// <inheritdoc />
    public async Task<FetchedMessage> GetMessageAsync(string id, CancellationToken cancellationToken)
    {
        Fetched.Add(id);
        if (!_messages.TryGetValue(id, out var entry))
            throw new EmailToolException(EmailErrorCode.MessageNotFound, $"No message '{id}' in the fake mailbox.");

        using var stream = new MemoryStream(entry.Raw, writable: false);
        var message = await MimeMessage.LoadAsync(stream, cancellationToken);
        Issued.Add(message);
        return new FetchedMessage(id, entry.Folder, message, entry.Seen, entry.Flagged);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MovedMessage>> MoveAsync(IReadOnlyList<string> ids, string destination, CancellationToken cancellationToken)
    {
        Moves.Add((ids, destination));
        return Task.FromResult<IReadOnlyList<MovedMessage>>(ids.Select(id => new MovedMessage(id, id + "-moved")).ToList());
    }

    /// <inheritdoc />
    public Task<int> SetFlagsAsync(IReadOnlyList<string> ids, bool? seen, bool? flagged, CancellationToken cancellationToken)
    {
        FlagUpdates.Add((ids, seen, flagged));
        return Task.FromResult(ids.Count);
    }

    /// <inheritdoc />
    public Task<DeleteOutcome> DeleteAsync(IReadOnlyList<string> ids, bool permanent, CancellationToken cancellationToken)
    {
        Deletes.Add((ids, permanent));
        return Task.FromResult(new DeleteOutcome(ids.Count, permanent, permanent ? null : "Trash"));
    }

    /// <inheritdoc />
    public async Task<(string? Id, string Folder)> SaveDraftAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        Drafts.Add(await ToTextAsync(message, cancellationToken));
        return ("draft-" + Drafts.Count, "Drafts");
    }

    /// <inheritdoc />
    public async Task AppendToSentAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        if (AppendToSentFailure is { } failure)
            throw failure;
        SentCopies.Add(await ToTextAsync(message, cancellationToken));
    }

    /// <summary>The MIME text of <paramref name="message"/>.</summary>
    public static async Task<string> ToTextAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await message.WriteToAsync(buffer, cancellationToken);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
