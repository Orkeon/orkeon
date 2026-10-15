using System.Collections.ObjectModel;

namespace Orkeon.Tools.Code;

/// <summary>
/// The <c>Orkeon:Tools:Shell:Confinement</c> section: whether <see cref="ShellCommandTool"/> runs its
/// process in a view of the file system built from the VFS, rather than on the host's tree.
/// </summary>
public sealed class ShellConfinementOptions
{
    /// <summary>
    /// How the process is confined. <c>None</c> (default) runs it on the host, as the user of the
    /// host. <c>Bubblewrap</c> runs it under <c>bwrap</c> (Linux): each mount bound at its virtual
    /// path, read-only when it lacks Write, the system directories read-only, nothing else of the
    /// host, the network off. When it is unavailable (another OS, no <c>bwrap</c>, namespaces
    /// refused) the call fails with the reason and the command does not run.
    /// </summary>
    public ShellConfinementMode Mode { get; set; }

    /// <summary>
    /// Lets the confined process reach the network. Default false: it gets its own network
    /// namespace, loopback only.
    /// </summary>
    public bool AllowNetwork { get; set; }

    /// <summary>
    /// Absolute host directories bound read-only at their own path, on top of the system
    /// directories (<c>/usr</c>, <c>/bin</c>, <c>/sbin</c>, <c>/lib</c>, <c>/lib32</c>, <c>/lib64</c>,
    /// <c>/etc</c>), for a program installed elsewhere, such as <c>/opt/node</c> or a <c>.dotnet</c> folder.
    /// </summary>
    public Collection<string> ReadOnlyPaths { get; } = [];
}

/// <summary>How <see cref="ShellCommandTool"/> confines the process it starts.</summary>
public enum ShellConfinementMode
{
    /// <summary>No confinement: the process sees the host's tree, as the user of the host.</summary>
    None,

    /// <summary>The process runs under bubblewrap (<c>bwrap</c>, Linux) in a view built from the VFS.</summary>
    Bubblewrap,
}
