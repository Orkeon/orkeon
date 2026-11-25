using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Host.Files;

namespace Orkeon.Host.Gateway;

/// <summary>
/// The file commands of one channel: what the conversation has, a file of it, a file left for its crew.
/// <para>
/// <c>/outbox</c> is always open to download; <c>/inbox</c> is listed and not sent back, since the person has what was sent,
/// and a file taken back from the channel must not come out again by the bot; the rest is what the crew declares, read at
/// each command. A path is held to that declaration and to the virtual file system, and one that is not open is answered
/// as one that is not there.
/// </para>
/// </summary>
internal sealed class ChatFileCommands
{
    /// <summary>What a channel offers as choices: so many, and each so long.</summary>
    internal const int MaxChoices = 25;
    internal const int MaxChoiceLength = 100;

    private const int MaxText = 2000;
    private const int MaxScanned = 1000;
    private const int MaxPathLength = 1024;
    private const string NoFile = "There is no such file here, or it is not open to download. /files lists what is.";
    private const string NoFolder = "There is no such folder here, or it is not open. /files lists what is.";
    private const string NoUpload = "There is no such folder here, or it takes no file. /files lists those that do.";
    private static readonly TimeSpan s_receiveLimit = TimeSpan.FromMinutes(2);

    private readonly string _channel;
    private readonly ChatRoutes _routes;
    private readonly IReadOnlyList<HostedCrewOptions> _crews;
    private readonly ConversationFiles _files;
    private readonly CrewFileFolders _folders;

    /// <summary>Builds the commands of a channel.</summary>
    /// <param name="channel">The channel's name, part of the conversation's key.</param>
    /// <param name="routes">The crew of each room.</param>
    /// <param name="crews">The hosted crews.</param>
    /// <param name="files">The conversations' folders.</param>
    /// <param name="folders">What each crew declares.</param>
    public ChatFileCommands(
        string channel, ChatRoutes routes, IReadOnlyList<HostedCrewOptions> crews, ConversationFiles files, CrewFileFolders folders)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
        _crews = crews ?? throw new ArgumentNullException(nameof(crews));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _folders = folders ?? throw new ArgumentNullException(nameof(folders));
    }

    /// <summary>Answers a file command of a person already known to be allowed.</summary>
    public async Task<CommandReply> AnswerAsync(CommandInvocation invocation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (await CrewAsync(invocation, ct).ConfigureAwait(false) is not { } crew)
            return new CommandReply("The channel this conversation belongs to is not known, so neither is its crew. Use this command in a thread, or try again.");

        try
        {
            return invocation.CommandName switch
            {
                ChatCommands.Files => await ListAsync(invocation, crew, ct).ConfigureAwait(false),
                ChatCommands.Get => await GetAsync(invocation, crew, ct).ConfigureAwait(false),
                ChatCommands.Put => await PutAsync(invocation, crew, ct).ConfigureAwait(false),
                _ => new CommandReply("Unknown command."),
            };
        }
        catch (ConversationPreparationException ex)
        {
            return new CommandReply(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileAccessDeniedException)
        {
            return new CommandReply("The files of this conversation cannot be read now. Try again.");
        }
    }

    /// <summary>What may be typed next, for a person already known to be allowed. A path too long for a choice is typed by hand.</summary>
    public async Task<IReadOnlyList<string>> SuggestAsync(CommandInvocation invocation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (invocation.Focused is null || await CrewAsync(invocation, ct).ConfigureAwait(false) is not { } crew)
            return [];

        var declared = await _folders.ReadAsync(crew.Name, ct).ConfigureAwait(false);
        IEnumerable<string> candidates = (invocation.CommandName, invocation.Focused) switch
        {
            (ChatCommands.Files, "folder") =>
                [RunnerVirtualRoots.Outbox, RunnerVirtualRoots.Inbox, .. declared.Open.Where(f => f.Download).Select(f => f.Path)],
            (ChatCommands.Put, "folder") => declared.Open.Where(f => f.Upload).Select(f => f.Path),
            (ChatCommands.Get, "path") => await PathsAsync(invocation, crew, declared, ct).ConfigureAwait(false),
            _ => [],
        };

        var typed = Argument(invocation, invocation.Focused).Trim();
        return [.. candidates
            .Where(c => c.Length <= MaxChoiceLength && FileNotices.IsPlain(c) && c.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxChoices)];
    }

    /// <summary>The crew of the conversation; null when its room is not known: another crew's folders must not stand in.</summary>
    private async Task<HostedCrewOptions?> CrewAsync(CommandInvocation invocation, CancellationToken ct)
    {
        var room = invocation.RoomId;
        if (room is null && invocation.ResolveRoomAsync is { } resolve)
            room = await resolve(ct).ConfigureAwait(false);
        if (room is null)
            return null;

        var name = _routes.Resolve(room);
        return _crews.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private ConversationRef Conversation(CommandInvocation invocation) => new(_channel, invocation.ConversationId);

    private static string Argument(CommandInvocation invocation, string name) => invocation.Arguments.GetValueOrDefault(name) ?? string.Empty;

    // ----- /files -----

    private async Task<CommandReply> ListAsync(CommandInvocation invocation, HostedCrewOptions crew, CancellationToken ct)
    {
        var declared = await _folders.ReadAsync(crew.Name, ct).ConfigureAwait(false);
        string? asked = null;
        if (Argument(invocation, "folder").Trim() is { Length: > 0 } typed)
        {
            asked = Canonical(typed.TrimEnd('/'));
            if (asked is null || !Roots(declared).Any(r => r == asked || IsUnder(asked, r)))
                return new CommandReply(NoFolder);
        }

        var roots = Roots(declared).Where(r => asked is null || r == asked || IsUnder(asked, r)).ToList();
        var listed = await _files.UseAsync(
            Conversation(invocation),
            crew,
            async (fs, token) =>
            {
                var sections = new List<(string Root, List<VirtualFileEntry> Files, bool Cut)>();
                foreach (var root in roots)
                {
                    var (files, cut) = await ScanAsync(fs, root, token).ConfigureAwait(false);
                    sections.Add((root, [.. files.Where(f => asked is null || asked == root || IsUnder(f.VirtualPath, asked))], cut));
                }

                return sections;
            },
            ct).ConfigureAwait(false);

        // A folder under an opened one is not told from none when it shows nothing.
        if (asked is not null && !roots.Contains(asked) && listed.All(s => s.Files.Count == 0))
            return new CommandReply(NoFolder);

        return new CommandReply(Text(listed, declared, asked is null));
    }

    private static string Text(List<(string Root, List<VirtualFileEntry> Files, bool Cut)> sections, OpenedFolders declared, bool whole)
    {
        var tail = new List<string>();
        var uploads = declared.Open.Where(f => f.Upload).Select(f => FileNotices.Path(f.Path)).ToList();
        if (whole && uploads.Count > 0)
            tail.Add("/put leaves a file in " + FileNotices.Bounded(uploads, ", ", 400));
        if (whole && declared.Invalid && declared.Reason is { } reason)
            tail.Add("The crew's folders are closed: " + FileNotices.Inline(reason, 300));

        var room = MaxText - 20 - tail.Sum(t => t.Length + 1);
        var blocks = new List<string>();
        for (var i = 0; i < sections.Count; i++)
        {
            var (root, files, cut) = sections[i];
            var head = root == RunnerVirtualRoots.Inbox ? "received, not sent back" : "/get sends one";
            var title = $"{FileNotices.Path(root)} ({head})";
            var share = room / (sections.Count - i);
            var block = files.Count == 0
                ? $"{title}: empty"
                : $"{title}:\n" + FileNotices.Bounded([.. files.Select(f => FileNotices.PathAndSize(f.VirtualPath, f.SizeBytes))], "\n", share - title.Length - 40);
            if (cut)
                block += "\nNot all of it was read.";
            if (block.Length > share && blocks.Count > 0)
                block = string.Empty;
            room -= block.Length + 1;
            if (block.Length > 0)
                blocks.Add(block);
        }

        return string.Join('\n', [.. blocks, .. tail]);
    }

    // ----- /get -----

    private async Task<CommandReply> GetAsync(CommandInvocation invocation, HostedCrewOptions crew, CancellationToken ct)
    {
        var conversation = Conversation(invocation);
        var path = Canonical(Argument(invocation, "path"));
        var entry = path is null ? null : await EntryAsync(conversation, crew, path, ct).ConfigureAwait(false);
        if (entry is null || path is null)
            return new CommandReply(NoFile);

        return new CommandReply(FileNotices.Path(path), [new OutboundFile
        {
            Name = path[(path.LastIndexOf('/') + 1)..],
            Size = entry.SizeBytes,
            OpenAsync = token => OpenAsync(conversation, crew, path, entry.SizeBytes, token),
        }]);
    }

    private async Task<VirtualFileEntry?> EntryAsync(ConversationRef conversation, HostedCrewOptions crew, string path, CancellationToken ct)
    {
        var declared = await _folders.ReadAsync(crew.Name, ct).ConfigureAwait(false);
        var root = DownloadRoots(declared).FirstOrDefault(r => IsUnder(path, r));
        return root is null
            ? null
            : await _files.UseAsync(conversation, crew, (fs, token) => EntryAsync(fs, root, path, token), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the file whole, judged again: the declaration, the rights and the links may have changed since it was named.
    /// A copy, not the open file, so neither the conversation nor an agent that writes there waits for the upload.
    /// </summary>
    private async Task<Stream> OpenAsync(ConversationRef conversation, HostedCrewOptions crew, string path, long announced, CancellationToken ct)
    {
        var declared = await _folders.ReadAsync(crew.Name, ct).ConfigureAwait(false);
        var root = DownloadRoots(declared).FirstOrDefault(r => IsUnder(path, r));
        return await _files.UseAsync<Stream>(
            conversation,
            crew,
            async (fs, token) =>
            {
                if (root is null || await EntryAsync(fs, root, path, token).ConfigureAwait(false) is null)
                    throw new FileNotFoundException(NoFile);

                var source = await fs.OpenReadStreamAsync(path, token).ConfigureAwait(false);
                await using var lease = source.ConfigureAwait(false);
                var copy = new MemoryStream();
                try
                {
                    var block = new byte[81920];
                    int read;
                    while ((read = await source.ReadAsync(block, token).ConfigureAwait(false)) > 0)
                    {
                        // More than was announced: it is being written, and may be more than the channel takes.
                        if (copy.Length + read > announced)
                            throw new IOException("The file is being written.");
                        await copy.WriteAsync(block.AsMemory(0, read), token).ConfigureAwait(false);
                    }

                    copy.Position = 0;
                    return copy;
                }
                catch
                {
                    await copy.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            },
            ct).ConfigureAwait(false);
    }

    private async Task<IEnumerable<string>> PathsAsync(CommandInvocation invocation, HostedCrewOptions crew, OpenedFolders declared, CancellationToken ct) =>
        await _files.UseAsync(
            Conversation(invocation),
            crew,
            async (fs, token) =>
            {
                var paths = new List<string>();
                foreach (var root in DownloadRoots(declared))
                    paths.AddRange((await ScanAsync(fs, root, token).ConfigureAwait(false)).Files.Select(f => f.VirtualPath));
                return paths;
            },
            ct).ConfigureAwait(false);

    // ----- /put -----

    private async Task<CommandReply> PutAsync(CommandInvocation invocation, HostedCrewOptions crew, CancellationToken ct)
    {
        if (invocation is not { Attachments: [var attachment, ..], AttachmentSource: { } source })
            return new CommandReply("There is no file to keep.");

        var folder = Canonical(Argument(invocation, "folder").Trim().TrimEnd('/'));
        var declared = await _folders.ReadAsync(crew.Name, ct).ConfigureAwait(false);
        var conversation = Conversation(invocation);
        if (folder is null
            || !declared.Open.Any(f => f.Upload && f.Path == folder)
            || !await _files.UseAsync(conversation, crew, (fs, token) => IsDirectAsync(fs, folder, token), ct).ConfigureAwait(false))
        {
            return new CommandReply(NoUpload);
        }

        // The conversation's lock is held while it is read: a file that stops coming must not hold it.
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(s_receiveLimit);
        try
        {
            var stored = await _files.StoreAsync(
                conversation,
                crew,
                new IncomingFile(attachment.Name, token => source.OpenAsync(attachment, token)) { Id = attachment.Id, Size = attachment.Size },
                index: 0,
                folder,
                bounded.Token).ConfigureAwait(false);
            return new CommandReply(stored.Refused is { } refused
                ? FileNotices.Refused([refused])
                : $"Kept as {FileNotices.Path(stored.Path!)}.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new CommandReply("The file was not kept: it took too long to arrive.");
        }
    }

    // ----- what a path may name -----

    private static IEnumerable<string> Roots(OpenedFolders declared) =>
        [RunnerVirtualRoots.Outbox, RunnerVirtualRoots.Inbox, .. declared.Open.Where(f => f.Download).Select(f => f.Path)];

    private static IEnumerable<string> DownloadRoots(OpenedFolders declared) =>
        [RunnerVirtualRoots.Outbox, .. declared.Open.Where(f => f.Download).Select(f => f.Path)];

    private static bool IsUnder(string path, string root) => path.StartsWith(root + "/", StringComparison.Ordinal);

    /// <summary>The path as typed when it has one reading; null otherwise. Nothing is resolved here: what would need it is refused.</summary>
    private static string? Canonical(string typed)
    {
        var path = typed.Trim();
        if (path.Length is < 2 or > MaxPathLength || path[0] != '/' || path.Contains('\\', StringComparison.Ordinal) || path.Any(char.IsControl))
            return null;

        return path[1..].Split('/').All(IsName) ? path : null;

        static bool IsName(string segment) =>
            segment.Length > 0
            && segment is not ("." or "..")
            && (!OperatingSystem.IsWindows() || !(segment.Contains(':', StringComparison.Ordinal) || segment.EndsWith('.') || segment.EndsWith(' ')));
    }

    /// <summary>The file, when it may be read through the folder opened: nothing hidden, no link, no other mount, and the rights of the mount.</summary>
    private static async Task<VirtualFileEntry?> EntryAsync(IFileSystemService fs, string root, string path, CancellationToken ct)
    {
        if (!IsShown(fs, root, path) || !await IsDirectAsync(fs, path, ct).ConfigureAwait(false))
            return null;

        return await fs.TryGetEntryAsync(path, ct).ConfigureAwait(false) is { Kind: VirtualEntryKind.File } entry ? entry : null;
    }

    private static bool IsShown(IFileSystemService fs, string root, string path)
    {
        if (path[(root.Length + 1)..].Split('/').Any(s => s.StartsWith('.')))
            return false;

        var mounts = fs.GetAvailableMounts();
        if (mounts.Any(m => IsUnder(m.VirtualPath, root) && (path == m.VirtualPath || IsUnder(path, m.VirtualPath))))
            return false;

        return fs.ResolveAndValidate(path, FileAccessRights.Read).IsAllowed;
    }

    private static MountInfo? MountOf(IReadOnlyList<MountInfo> mounts, string path) =>
        mounts.Where(m => path == m.VirtualPath || IsUnder(path, m.VirtualPath)).MaxBy(m => m.VirtualPath.Length);

    /// <summary>Whether no link stands between the root of the mount, the operator's own, and the path.</summary>
    private static async Task<bool> IsDirectAsync(IFileSystemService fs, string path, CancellationToken ct)
    {
        if (MountOf(fs.GetAvailableMounts(), path) is not { } mount)
            return false;

        for (var end = mount.VirtualPath.Length; end < path.Length;)
        {
            end = path.IndexOf('/', end + 1) is var next && next < 0 ? path.Length : next;
            // Null where the rights forbid looking: what is below them is then judged by its own.
            if (await fs.TryGetEntryAsync(path[..end], ct).ConfigureAwait(false) is { Kind: VirtualEntryKind.SymLink })
                return false;
        }

        return true;
    }

    /// <summary>The files open under a folder: the latest first in the conversation's own, by path elsewhere.</summary>
    private static async Task<(List<VirtualFileEntry> Files, bool Cut)> ScanAsync(IFileSystemService fs, string root, CancellationToken ct)
    {
        var files = new List<VirtualFileEntry>();
        var cut = false;
        try
        {
            if (await IsDirectAsync(fs, root, ct).ConfigureAwait(false))
            {
                var seen = 0;
                await foreach (var entry in fs.EnumerateFilesAsync(root, new VirtualEnumerationOptions(), ct).ConfigureAwait(false))
                {
                    if (++seen > MaxScanned)
                    {
                        cut = true;
                        break;
                    }

                    if (entry.Kind == VirtualEntryKind.File && IsShown(fs, root, entry.VirtualPath))
                        files.Add(entry);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileAccessDeniedException)
        {
            // What was read of it is shown, and that it is not the whole.
            cut = true;
        }

        var ordered = root is RunnerVirtualRoots.Outbox or RunnerVirtualRoots.Inbox
            ? files.OrderByDescending(f => f.LastModified).ThenBy(f => f.VirtualPath, StringComparer.Ordinal)
            : files.OrderBy(f => f.VirtualPath, StringComparer.Ordinal);
        return ([.. ordered], cut);
    }
}
