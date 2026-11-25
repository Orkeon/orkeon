using Orkeon.Constants.FileSystem;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Host.Gateway;

namespace Orkeon.Host.Files;

/// <summary>
/// Which files of a conversation may be sent to it, whoever asks, a person (<c>/get</c>) or an agent (<c>chat_attach</c>):
/// <c>/outbox</c> and what the crew declares, read at each call, held to the virtual file system. One that is not open is
/// answered as one that is not there.
/// </summary>
internal sealed class DownloadableFiles
{
    /// <summary>What is answered of a file that is not there, or not open.</summary>
    internal const string NoFile = "There is no such file here, or it is not open to download.";

    private const int MaxPathLength = 1024;

    private readonly ConversationFiles _files;
    private readonly CrewFileFolders _folders;

    /// <summary>Builds it over the conversations' folders and what each crew declares.</summary>
    public DownloadableFiles(ConversationFiles files, CrewFileFolders folders)
    {
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _folders = folders ?? throw new ArgumentNullException(nameof(folders));
    }

    /// <summary>The file, ready to send; null when it is not open, or not there. The scope of a calling agent binds it too.</summary>
    public async Task<OutboundFile?> FindAsync(ConversationRef conversation, HostedCrewOptions crew, string typed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(typed);
        if (Canonical(typed) is not { } path || await EntryAsync(conversation, crew, path, ct).ConfigureAwait(false) is not { } entry)
            return null;

        // The channel opens it when and where it likes, an hour later perhaps: still under the scope of who named it, and
        // nothing else of the run is kept for that.
        var scope = AgentRunScope.EnforcedFileSystem;
        return new OutboundFile
        {
            Name = path[(path.LastIndexOf('/') + 1)..],
            Size = entry.SizeBytes,
            Written = entry.LastModified,
            OpenAsync = token => ApartAsync(() => OpenAsync(conversation, crew, path, entry.SizeBytes, entry.LastModified, scope, token), token),
        };
    }

    /// <summary>
    /// What the crew declares, read as the host: an agent's scope narrows the files it sends, not what the crew opens, and
    /// the crew's definition is in no scope of its agents.
    /// </summary>
    internal Task<OpenedFolders> DeclaredAsync(HostedCrewOptions crew, CancellationToken ct) =>
        ApartAsync(() => _folders.ReadAsync(crew.Name, ct), ct);

    /// <summary>Runs in a flow of its own: what the caller's carries, an agent's scope among it, does not reach it.</summary>
    private static async Task<T> ApartAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        Task<T> working;
        if (ExecutionContext.IsFlowSuppressed())
        {
            working = Task.Run(work, ct);
        }
        else
        {
            using (ExecutionContext.SuppressFlow())
                working = Task.Run(work, ct);
        }

        return await working.ConfigureAwait(false);
    }

    private async Task<VirtualFileEntry?> EntryAsync(ConversationRef conversation, HostedCrewOptions crew, string path, CancellationToken ct)
    {
        var declared = await DeclaredAsync(crew, ct).ConfigureAwait(false);
        var root = RootOf(declared, path);
        return root is null
            ? null
            : await _files.UseAsync(conversation, crew, (fs, token) => EntryAsync(fs, root, path, token), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the file whole, judged again: the declaration, the rights and the links may have changed since it was named.
    /// What changed since is not what was announced, or accepted: it is not sent. A copy, not the open file, so neither the conversation nor an agent that writes there waits for the upload.
    /// </summary>
    private async Task<Stream> OpenAsync(
        ConversationRef conversation, HostedCrewOptions crew, string path, long announced, DateTimeOffset written, AgentFileSystemScope? scope, CancellationToken ct)
    {
        var agent = AgentRunScope.Begin(null, null, null, scope);
        await using var ended = agent.ConfigureAwait(false);
        using var call = AgentRunScope.EnterToolCall();
        var declared = await DeclaredAsync(crew, ct).ConfigureAwait(false);
        var root = RootOf(declared, path);
        return await _files.UseAsync<Stream>(
            conversation,
            crew,
            async (fs, token) =>
            {
                if (root is null || await EntryAsync(fs, root, path, token).ConfigureAwait(false) is not { } entry)
                    throw new FileNotFoundException(NoFile);
                if (entry.SizeBytes != announced || entry.LastModified != written)
                    throw new IOException("The file changed since it was named.");

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

    internal static IEnumerable<string> Roots(OpenedFolders declared) =>
        [RunnerVirtualRoots.Outbox, .. declared.Open.Where(f => f.Download).Select(f => f.Path)];

    /// <summary>The folder a file is opened through: the closest to it, of one opened inside another.</summary>
    private static string? RootOf(OpenedFolders declared, string path) =>
        Roots(declared).Where(r => IsUnder(path, r)).MaxBy(r => r.Length);

    internal static bool IsUnder(string path, string root) => path.StartsWith(root + "/", StringComparison.Ordinal);

    /// <summary>The path as typed when it has one reading; null otherwise. Nothing is resolved here: what would need it is refused.</summary>
    internal static string? Canonical(string typed)
    {
        var path = typed.Trim();
        if (path.Length is < 2 or > MaxPathLength || path[0] != '/' || path.Contains('\\', StringComparison.Ordinal) || path.Any(char.IsControl))
            return null;

        return path[1..].Split('/').All(IsName) ? path : null;

        static bool IsName(string segment) =>
            segment.Length > 0
            && segment is not ("." or "..")
            && (!OperatingSystem.IsWindows()
                || !(segment.Contains(':', StringComparison.Ordinal) || segment.EndsWith('.') || segment.EndsWith(' ') || IsShortName(segment)));
    }

    /// <summary>Whether the name may be the short one Windows gives a file beside its own: SECRET~1 is a second reading of .secret.</summary>
    internal static bool IsShortName(string segment)
    {
        var stem = segment.Split('.')[0];
        return stem.Length <= 8 && segment.Length <= 12 && stem.IndexOf('~', StringComparison.Ordinal) is var at and > 0
            && at + 1 < stem.Length && stem[(at + 1)..].All(char.IsAsciiDigit);
    }

    /// <summary>The file, when it may be read through the folder opened: nothing hidden, no link, no other mount, and the rights of the mount.</summary>
    private static async Task<VirtualFileEntry?> EntryAsync(IFileSystemService fs, string root, string path, CancellationToken ct)
    {
        if (!IsShown(fs, root, path) || !await IsDirectAsync(fs, path, ct).ConfigureAwait(false))
            return null;

        return await fs.TryGetEntryAsync(path, ct).ConfigureAwait(false) is { Kind: VirtualEntryKind.File } entry ? entry : null;
    }

    internal static bool IsShown(IFileSystemService fs, string root, string path)
    {
        if (path[(root.Length + 1)..].Split('/').Any(s => s.StartsWith('.')))
            return false;

        var mounts = fs.GetAvailableMounts();
        // In any case: where the file system folds it, what lies under the mount is another reading of one folder.
        if (mounts.Any(m => IsUnder(m.VirtualPath, root)
            && (path.Equals(m.VirtualPath, StringComparison.OrdinalIgnoreCase) || path.StartsWith(m.VirtualPath + "/", StringComparison.OrdinalIgnoreCase))))
            return false;

        return fs.ResolveAndValidate(path, FileAccessRights.Read).IsAllowed;
    }

    private static MountInfo? MountOf(IReadOnlyList<MountInfo> mounts, string path) =>
        mounts.Where(m => path == m.VirtualPath || IsUnder(path, m.VirtualPath)).MaxBy(m => m.VirtualPath.Length);

    /// <summary>Whether no link stands between the root of the mount, the operator's own, and the path.</summary>
    internal static async Task<bool> IsDirectAsync(IFileSystemService fs, string path, CancellationToken ct)
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
}
