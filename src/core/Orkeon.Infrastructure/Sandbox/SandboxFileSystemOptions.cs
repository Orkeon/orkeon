using Orkeon.Constants.FileSystem;

namespace Orkeon.Infrastructure.Sandbox;

/// <summary>
/// Configuration options for the sandbox virtual file system mount.
/// Bind from the "Orkeon:Sandbox" configuration section.
/// </summary>
public sealed class SandboxFileSystemOptions
{
    /// <summary>
    /// Root directory under which per-session sandbox directories are created.
    /// When <c>null</c>, defaults to <c>Path.Combine(Path.GetTempPath(), "orkeon-sandbox")</c>.
    /// </summary>
    public string? EphemeralRoot { get; set; }

    /// <summary>
    /// Age threshold for orphaned session directories.
    /// Directories older than this value are deleted by the janitor on startup.
    /// Defaults to 24 hours.
    /// </summary>
    public TimeSpan CleanupOrphansOlderThan { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Virtual path at which the sandbox is mounted.
    /// Defaults to <c>/sandbox</c>.
    /// </summary>
    public string VirtualPath { get; set; } = RunnerVirtualRoots.Sandbox;
}
