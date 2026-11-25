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
    private const string NoFile = DownloadableFiles.NoFile + " /files lists what is.";
    private const string NoFolder = "There is no such folder here, or it is not open. /files lists what is.";
    private const string NoUpload = "There is no such folder here, or it takes no file. /files lists those that do.";
    private static readonly TimeSpan s_receiveLimit = TimeSpan.FromMinutes(2);

    private readonly string _channel;
    private readonly ChatRoutes _routes;
    private readonly IReadOnlyList<HostedCrewOptions> _crews;
    private readonly ConversationFiles _files;
    private readonly CrewFileFolders _folders;
    private readonly DownloadableFiles _downloads;

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
        _downloads = new DownloadableFiles(files, folders);
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
            asked = DownloadableFiles.Canonical(typed.TrimEnd('/'));
            if (asked is null || !Roots(declared).Any(r => r == asked || DownloadableFiles.IsUnder(asked, r)))
                return new CommandReply(NoFolder);
        }

        var roots = Roots(declared).Where(r => asked is null || r == asked || DownloadableFiles.IsUnder(asked, r)).ToList();
        var listed = await _files.UseAsync(
            Conversation(invocation),
            crew,
            async (fs, token) =>
            {
                var sections = new List<(string Root, List<VirtualFileEntry> Files, bool Cut)>();
                foreach (var root in roots)
                {
                    var (files, cut) = await ScanAsync(fs, root, token).ConfigureAwait(false);
                    sections.Add((root, [.. files.Where(f => asked is null || asked == root || DownloadableFiles.IsUnder(f.VirtualPath, asked))], cut));
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
        var path = Argument(invocation, "path").Trim();
        return await _downloads.FindAsync(conversation, crew, path, ct).ConfigureAwait(false) is { } file
            ? new CommandReply(FileNotices.Path(path), [file])
            : new CommandReply(NoFile);
    }

    private async Task<IEnumerable<string>> PathsAsync(CommandInvocation invocation, HostedCrewOptions crew, OpenedFolders declared, CancellationToken ct) =>
        await _files.UseAsync(
            Conversation(invocation),
            crew,
            async (fs, token) =>
            {
                var paths = new List<string>();
                foreach (var root in DownloadableFiles.Roots(declared))
                    paths.AddRange((await ScanAsync(fs, root, token).ConfigureAwait(false)).Files.Select(f => f.VirtualPath));
                return paths;
            },
            ct).ConfigureAwait(false);

    // ----- /put -----

    private async Task<CommandReply> PutAsync(CommandInvocation invocation, HostedCrewOptions crew, CancellationToken ct)
    {
        if (invocation is not { Attachments: [var attachment, ..], AttachmentSource: { } source })
            return new CommandReply("There is no file to keep.");

        var folder = DownloadableFiles.Canonical(Argument(invocation, "folder").Trim().TrimEnd('/'));
        var conversation = Conversation(invocation);
        var declared = await _folders.ReadAsync(crew.Name, ct).ConfigureAwait(false);
        if (folder is null
            || !declared.Open.Any(f => f.Upload && f.Path == folder)
            || !await _files.UseAsync(conversation, crew, (fs, token) => DownloadableFiles.IsDirectAsync(fs, folder, token), ct).ConfigureAwait(false))
        {
            return new CommandReply(NoUpload);
        }

        // What it may take is reserved while it is read: a file that stops coming must not keep it.
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
                // The crew may have closed the folder, or put a link in its place, while the file came.
                async (fs, token) => await IsOpenAsync(fs, crew, folder, token).ConfigureAwait(false) ? null : "its folder takes no file any more",
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

    private async Task<bool> IsOpenAsync(IFileSystemService fs, HostedCrewOptions crew, string folder, CancellationToken ct)
    {
        var declared = await _downloads.DeclaredAsync(crew, ct).ConfigureAwait(false);
        return declared.Open.Any(f => f.Upload && f.Path == folder) && await DownloadableFiles.IsDirectAsync(fs, folder, ct).ConfigureAwait(false);
    }

    // ----- what a path may name -----

    private static IEnumerable<string> Roots(OpenedFolders declared) =>
        [RunnerVirtualRoots.Outbox, RunnerVirtualRoots.Inbox, .. declared.Open.Where(f => f.Download).Select(f => f.Path)];

    /// <summary>The files open under a folder: the latest first in the conversation's own, by path elsewhere.</summary>
    private static async Task<(List<VirtualFileEntry> Files, bool Cut)> ScanAsync(IFileSystemService fs, string root, CancellationToken ct)
    {
        var files = new List<VirtualFileEntry>();
        var cut = false;
        try
        {
            if (await DownloadableFiles.IsDirectAsync(fs, root, ct).ConfigureAwait(false))
            {
                var seen = 0;
                await foreach (var entry in fs.EnumerateFilesAsync(root, new VirtualEnumerationOptions(), ct).ConfigureAwait(false))
                {
                    if (++seen > MaxScanned)
                    {
                        cut = true;
                        break;
                    }

                    if (entry.Kind == VirtualEntryKind.File && DownloadableFiles.IsShown(fs, root, entry.VirtualPath))
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
