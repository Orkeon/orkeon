using Orkeon.Domain.FileSystem;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>
/// Observes what the runner entered as the ambient mount set for a run — the virtual paths of
/// each namespace, in the order the runs entered them — and enters it for real.
/// </summary>
internal sealed class SpyFileSystemScope : IFileSystemScope
{
    private readonly AsyncLocalFileSystemScope _inner = new();
    private readonly List<IReadOnlyList<string>> _entered = [];

    /// <summary>The virtual paths of every namespace entered so far.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Entered
    {
        get
        {
            lock (_entered)
                return [.. _entered];
        }
    }

    public FileSystemRegistry? Current => _inner.Current;

    public IDisposable Enter(FileSystemRegistry registry)
    {
        lock (_entered)
            _entered.Add([.. registry.GetAllMountsInternal().Select(m => m.VirtualPath)]);
        return _inner.Enter(registry);
    }
}
