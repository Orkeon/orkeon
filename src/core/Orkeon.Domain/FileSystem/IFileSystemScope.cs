namespace Orkeon.Domain.FileSystem;

/// <summary>
/// Ambient per-execution override of the active <see cref="FileSystemRegistry"/> (mount set).
/// A host embedding Orkeon (e.g. a run engine) can give one execution flow its own mounts —
/// built from a run profile — without disturbing the boot-time singleton mounts that every other
/// flow keeps seeing. The override is scoped to the current asynchronous control flow, so sibling
/// runs executing concurrently each observe their own mounts and never leak across one another.
/// </summary>
/// <remarks>
/// This is an <see cref="System.Threading.AsyncLocal{T}"/>-style ambient context (mirrors the
/// EventHub caller context), not a DI-scoped service: <see cref="IFileSystemService"/> must stay a
/// singleton (many singletons inject it), so it consults this ambient override per operation rather
/// than being resolved per scope.
/// </remarks>
public interface IFileSystemScope
{
    /// <summary>
    /// The registry active for the current async flow, or <c>null</c> when none has been entered
    /// (callers then fall back to the boot registry, i.e. unchanged behavior).
    /// </summary>
    FileSystemRegistry? Current { get; }

    /// <summary>
    /// Installs <paramref name="registry"/> as the active mount set for the current async flow.
    /// Dispose the returned token (typically via <c>using</c>) to restore the previous value.
    /// Nesting is supported: the previous registry is restored on dispose.
    /// </summary>
    /// <remarks>
    /// The caller owns <paramref name="registry"/>'s lifetime — this method does not dispose it (it
    /// may be reused across scopes). Build and dispose it explicitly, e.g.
    /// <c>using var registry = new FileSystemRegistry(mounts); using var _ = scope.Enter(registry);</c>.
    /// Rights, path-traversal and workspace-root guards apply to the entered mounts exactly as to
    /// boot mounts — they are enforced per operation, over whatever registry is active.
    /// </remarks>
    IDisposable Enter(FileSystemRegistry registry);
}
