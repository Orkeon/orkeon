namespace Orkeon.Domain.FileSystem;

/// <summary>
/// Default <see cref="IFileSystemScope"/> backed by <see cref="AsyncLocal{T}"/>. Registered as a
/// singleton so the host that enters a scope and the singleton <see cref="IFileSystemService"/> that
/// reads it share the same ambient slot. <see cref="AsyncLocal{T}"/> isolates the value per
/// asynchronous control flow, so concurrent runs never see each other's mounts.
/// </summary>
public sealed class AsyncLocalFileSystemScope : IFileSystemScope
{
    private readonly AsyncLocal<FileSystemRegistry?> _current = new();

    /// <inheritdoc />
    public FileSystemRegistry? Current => _current.Value;

    /// <inheritdoc />
    public IDisposable Enter(FileSystemRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var previous = _current.Value;
        _current.Value = registry;
        return new PopScope(this, previous);
    }

    private sealed class PopScope(AsyncLocalFileSystemScope owner, FileSystemRegistry? previous) : IDisposable
    {
        public void Dispose() => owner._current.Value = previous;
    }
}
