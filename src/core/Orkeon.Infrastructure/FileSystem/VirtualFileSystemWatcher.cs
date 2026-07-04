using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Infrastructure.FileSystem;

// EXCEPTION-ABSTRACTION-INTERNAL: wraps System.IO.FileSystemWatcher (authorized zone §1)
/// <summary>
/// Watches a virtual path for file-system changes and yields strongly-typed change events.
/// </summary>
public sealed partial class VirtualFileSystemWatcher : IVirtualFileSystemWatcher
{
    private readonly IFileSystemService _fs;
    private readonly ILogger<VirtualFileSystemWatcher> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="VirtualFileSystemWatcher"/>.
    /// </summary>
    /// <param name="fs">Virtual file system service used to resolve and validate paths.</param>
    /// <param name="logger">Logger for watcher error diagnostics.</param>
    public VirtualFileSystemWatcher(IFileSystemService fs, ILogger<VirtualFileSystemWatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(logger);
        _fs = fs;
        _logger = logger;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<VirtualFileSystemChange> WatchAsync(
        string virtualRoot,
        VirtualWatchOptions options,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRoot);
        ArgumentNullException.ThrowIfNull(options);
        return WatchCoreAsync(virtualRoot, options, ct);
    }

    private async IAsyncEnumerable<VirtualFileSystemChange> WatchCoreAsync(
        string virtualRoot,
        VirtualWatchOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var resolved = _fs.ResolveAndValidate(virtualRoot, FileAccessRights.Read);
        if (!resolved.IsAllowed)
            throw new FileAccessDeniedException(
                resolved.DenialReason ?? "Read denied", virtualRoot, FileAccessRights.Read);

        var physicalRoot = resolved.ResolvedPath!;

        var channel = Channel.CreateUnbounded<VirtualFileSystemChange>(
            new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });

        using var fsw = new System.IO.FileSystemWatcher(physicalRoot)
        {
            IncludeSubdirectories = options.IncludeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            Filter = options.SearchPattern ?? "*",
        };

        // Debounce support: last-write wins per virtual path within the window.
        Dictionary<string, (VirtualFileSystemChange Change, DateTimeOffset Expires)>? debounce =
            options.DebounceWindow.HasValue ? new(StringComparer.Ordinal) : null;

        void Enqueue(VirtualFileSystemChange change)
        {
            if (debounce is null)
            {
                channel.Writer.TryWrite(change);
                return;
            }

            var expires = DateTimeOffset.UtcNow.Add(options.DebounceWindow!.Value);
            debounce[change.VirtualPath] = (change, expires);

            _ = Task.Delay(options.DebounceWindow.Value, ct).ContinueWith(_ =>
            {
                if (debounce.TryGetValue(change.VirtualPath, out var entry) &&
                    DateTimeOffset.UtcNow >= entry.Expires)
                {
                    debounce.Remove(change.VirtualPath);
                    channel.Writer.TryWrite(entry.Change);
                }
            }, TaskScheduler.Default);
        }

        fsw.Created += (_, e) => OnEvent(e.FullPath, VirtualFileChangeKind.Created, null, Enqueue);
        fsw.Changed += (_, e) => OnEvent(e.FullPath, VirtualFileChangeKind.Modified, null, Enqueue);
        fsw.Deleted += (_, e) => OnEvent(e.FullPath, VirtualFileChangeKind.Deleted, null, Enqueue);
        fsw.Renamed += (_, e) => OnRenameEvent(e.OldFullPath, e.FullPath, Enqueue);
        fsw.Error += (_, e) => LogWatcherError(e.GetException());

        fsw.EnableRaisingEvents = true;

        ct.Register(() => channel.Writer.TryComplete());

        await foreach (var change in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return change;
    }

    private void OnEvent(string physicalPath, VirtualFileChangeKind kind, string? oldPhysical, Action<VirtualFileSystemChange> enqueue)
    {
        var vPath = _fs.ToVirtualPath(physicalPath);
        if (vPath is null) return; // outside mount — drop silently

        var oldVPath = oldPhysical is null ? null : _fs.ToVirtualPath(oldPhysical);

        enqueue(new VirtualFileSystemChange(vPath, kind, DateTimeOffset.UtcNow, oldVPath));
    }

    private void OnRenameEvent(string oldPhysical, string newPhysical, Action<VirtualFileSystemChange> enqueue)
    {
        var newVPath = _fs.ToVirtualPath(newPhysical);
        if (newVPath is null) return; // new path outside mount — drop

        var oldVPath = _fs.ToVirtualPath(oldPhysical);

        enqueue(new VirtualFileSystemChange(newVPath, VirtualFileChangeKind.Renamed, DateTimeOffset.UtcNow, oldVPath));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "FileSystemWatcher reported an error")]
    private partial void LogWatcherError(Exception ex);
}
