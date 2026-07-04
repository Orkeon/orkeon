namespace Orkeon.Domain.FileSystem;

/// <summary>
/// Watches a virtual directory and yields change events without exposing physical paths.
/// </summary>
public interface IVirtualFileSystemWatcher
{
    /// <summary>
    /// Watches <paramref name="virtualRoot"/> and yields <see cref="VirtualFileSystemChange"/> events
    /// until <paramref name="ct"/> is canceled. Physical paths are never surfaced to the caller.
    /// </summary>
    IAsyncEnumerable<VirtualFileSystemChange> WatchAsync(
        string virtualRoot,
        VirtualWatchOptions options,
        CancellationToken ct);
}
