using Orkeon.Domain.FileSystem;

namespace Orkeon.Infrastructure.FileSystem;

/// <summary>
/// The file system as infrastructure sees it — <see cref="MountVisibility.Internal"/> mounts
/// included. Asked for by name, by the handful of components that legitimately write to an
/// internal root.
/// <para>
/// <c>Internal</c> used to be visibility and nothing more: a mount absent from
/// <c>GetAvailableMounts()</c> that resolved for anyone who typed its name — and the names are
/// documented. One <c>file_read /llm-logs/llm-exchanges-….jsonl</c> handed an agent every
/// prompt and every API response of the run. The registry refuses those mounts now, so the
/// components that must reach them need a way to say so, and this type is that way: a distinct
/// DI registration rather than a flag on the interface everyone already holds. A tool cannot
/// obtain it by accident, and a reviewer can find every holder by searching for the name.
/// </para>
/// </summary>
public sealed class PrivilegedFileSystemAccess
{
    /// <summary>Initializes the accessor over a file system that resolves internal mounts.</summary>
    public PrivilegedFileSystemAccess(IFileSystemService fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        FileSystem = fileSystem;
    }

    /// <summary>The privileged file system. Never hand this to agent-reachable code.</summary>
    public IFileSystemService FileSystem { get; }
}
