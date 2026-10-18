using System.Collections.ObjectModel;

namespace Orkeon.Tools.Code;

/// <summary>
/// The <c>Orkeon:Tools:Shell:Confinement</c> section: whether <see cref="ShellCommandTool"/> runs its
/// process in a view of the file system built from the VFS, rather than on the host's tree.
/// </summary>
public sealed class ShellConfinementOptions
{
    /// <summary>
    /// How the process is confined. <c>Auto</c> (default) picks the backend of the platform:
    /// <c>Bubblewrap</c> on Linux, <c>Seatbelt</c> on macOS, <c>AppContainer</c> on Windows, and no
    /// confinement when the host runs in a container (the container is the boundary). <c>None</c>
    /// runs the process on the host, as the user of the host. <c>Bubblewrap</c> runs it under
    /// <c>bwrap</c> (Linux): each mount bound at its virtual path, read-only when it lacks Write, the
    /// system directories read-only, nothing else of the host, the network off. When the backend is
    /// unavailable (another OS, no <c>bwrap</c>, namespaces refused) the call fails with the reason
    /// and the command does not run.
    /// </summary>
    public ShellConfinementMode Mode { get; set; } = ShellConfinementMode.Auto;

    /// <summary>
    /// Lets the confined process reach the network. Default false: it gets its own network
    /// namespace, loopback only (under AppContainer: no network capability, hence no network at all).
    /// </summary>
    public bool AllowNetwork { get; set; }

    /// <summary>
    /// Absolute host directories bound read-only at their own path, on top of the system
    /// directories (<c>/usr</c>, <c>/bin</c>, <c>/sbin</c>, <c>/lib</c>, <c>/lib32</c>, <c>/lib64</c>,
    /// <c>/etc</c>), for a program installed elsewhere, such as <c>/opt/node</c> or a <c>.dotnet</c> folder.
    /// Under AppContainer (Windows) they are fully qualified host directories the container may read and
    /// execute, such as a tool installed under the user profile.
    /// </summary>
    public Collection<string> ReadOnlyPaths { get; } = [];
}

/// <summary>
/// How <see cref="ShellCommandTool"/> confines the process it starts. The values of <c>None</c> and
/// <c>Bubblewrap</c> are kept from 1.0.0-rc.4; <see cref="ShellConfinementOptions.Mode"/> defaults to
/// <see cref="Auto"/>.
/// </summary>
public enum ShellConfinementMode
{
    /// <summary>No confinement: the process sees the host's tree, as the user of the host.</summary>
    None = 0,

    /// <summary>The process runs under bubblewrap (<c>bwrap</c>, Linux) in a view built from the VFS.</summary>
    Bubblewrap = 1,

    /// <summary>
    /// The backend of the platform: <see cref="Bubblewrap"/> on Linux, <see cref="Seatbelt"/> on
    /// macOS, <see cref="AppContainer"/> on Windows; <see cref="None"/> inside a container.
    /// </summary>
    Auto = 2,

    /// <summary>The process runs under a Seatbelt profile (<c>sandbox-exec</c>, macOS).</summary>
    Seatbelt = 3,

    /// <summary>The process runs in an AppContainer inside a Job Object (Windows).</summary>
    AppContainer = 4,
}
