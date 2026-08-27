using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Infrastructure.Sandbox;

/// <summary>
/// The per-process sandbox directory and the <c>/sandbox</c> mount that reaches it —
/// <see cref="MountVisibility.Internal"/>, so the VFS resolves it and no agent is ever told
/// it exists.
/// <para>
/// This used to be an <c>IHostedService</c>, and that was the bug: the runners build a host
/// and never start it, so the mount was provisioned in the daemon alone. Every other shipped
/// host registered the whole code-execution subsystem — <c>ICodeSandbox</c>,
/// <c>DockerSandbox</c>, <c>SecureCodeInterpreterTool</c> — over a virtual root that did not
/// exist, and the first thing an agent's generated code did was fail on
/// "No mount found for virtual path '/sandbox/docker/…'". Building it with the registry
/// instead means it exists exactly when the file system does, in every host, started or not.
/// </para>
/// <para>
/// The session directory is created eagerly (the registry validates that a mount's base path
/// exists) and deleted on dispose, which the DI container does when the host is disposed.
/// The janitor sweep covers the runs that never got there — a process killed, a machine
/// rebooted — so a crashed run costs one stale directory until the next one starts.
/// </para>
/// </summary>
[Orkeon.Compliance.Vfs.SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: provisions the physical directory the /sandbox mount points at — it runs while the registry that IFileSystemService needs is still being built.")]
internal sealed partial class SandboxSession : IDisposable
{
    private readonly SandboxFileSystemOptions _opts;
    private readonly ILogger<SandboxSession> _log;
    private bool _disposed;

    /// <summary>Physical path of this process's sandbox directory.</summary>
    public string Root { get; }

    /// <summary>The mount that exposes <see cref="Root"/> to the VFS, hidden from agents.</summary>
    public FileSystemMount Mount { get; }

    public SandboxSession(IOptions<SandboxFileSystemOptions> opts, ILogger<SandboxSession> log)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(log);

        _opts = opts.Value;
        _log = log;

        var root = _opts.EphemeralRoot ?? Path.Combine(Path.GetTempPath(), "orkeon-sandbox");
        Root = Path.Combine(root, $"{Environment.ProcessId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}");

        Directory.CreateDirectory(Root);
        CleanupOrphans(root, _opts.CleanupOrphansOlderThan);

        Mount = new FileSystemMount(
            basePath: Root,
            virtualPath: _opts.VirtualPath,
            defaultRights: FileAccessRights.ReadWrite,
            overrides: null,
            visibility: MountVisibility.Internal);

        LogSandboxProvisioned(Root, _opts.VirtualPath);
    }

    /// <summary>
    /// Deletes orphaned session directories under <paramref name="root"/> whose last write is
    /// older than <paramref name="threshold"/>. Failures are logged and swallowed so a corrupt
    /// orphan never prevents startup.
    /// <para>
    /// Two guards stand before the delete, and neither is optional. <b>The name must be one we
    /// minted</b> (<c>&lt;pid&gt;-&lt;timestamp&gt;</c>): <c>EphemeralRoot</c> is a free-form
    /// configuration string and this code creates the directory it names, so pointing it at an
    /// existing folder is a two-word edit — and without a name check the next run recursively
    /// deletes every subdirectory of it older than the threshold. <b>The process must be
    /// gone</b>: a directory's mtime only moves when its direct children change, so once
    /// <c>&lt;Root&gt;/docker</c> exists the root's mtime is frozen no matter how busy the
    /// session is. A daemon idle past the threshold looked exactly like an orphan, and this
    /// sweep now runs on every process that builds a VFS rather than only on a started host —
    /// so a CLI invocation would have deleted a live daemon's sandbox out from under it.
    /// </para>
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort orphan cleanup: a failure deleting one stale sandbox directory is logged and skipped so a corrupt orphan never prevents startup or blocks the other deletions.")]
    private void CleanupOrphans(string root, TimeSpan threshold)
    {
        if (!Directory.Exists(root))
            return;

        var cutoff = DateTime.UtcNow - threshold;

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            if (string.Equals(dir, Root, StringComparison.Ordinal))
                continue;

            if (OwningProcessId(Path.GetFileName(dir)) is not { } pid)
                continue;

            if (IsProcessAlive(pid))
                continue;

            try
            {
                if (Directory.GetLastWriteTimeUtc(dir) < cutoff)
                {
                    Directory.Delete(dir, recursive: true);
                    LogOrphanCleaned(dir);
                }
            }
            catch (Exception ex)
            {
                LogOrphanCleanFailed(ex, dir);
            }
        }
    }

    private static readonly System.Buffers.SearchValues<char> s_digits =
        System.Buffers.SearchValues.Create("0123456789");

    /// <summary>
    /// The process id encoded in a session directory name, or <see langword="null"/> when the
    /// name is not one this class mints — in which case the directory is none of our business.
    /// </summary>
    private static int? OwningProcessId(string directoryName)
    {
        var separator = directoryName.IndexOf('-', StringComparison.Ordinal);
        if (separator <= 0)
            return null;

        if (!int.TryParse(directoryName.AsSpan(0, separator), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var pid))
            return null;

        // The stamp is all digits. Its exact width is not pinned here: the shape is what
        // distinguishes a directory this class minted from "Documents" or "backup-2024",
        // and the liveness check below carries the rest of the weight.
        var stamp = directoryName.AsSpan(separator + 1);
        return stamp.Length > 0 && !stamp.ContainsAnyExcept(s_digits) ? pid : null;
    }

    /// <summary>
    /// Whether a process with this id is running. A false negative only delays a cleanup by
    /// one run; a false positive keeps a directory that a later sweep collects. Both are
    /// cheaper than deleting a live session's files.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Probing for a process: any failure means we cannot prove it is gone, and the sandbox directory is then kept rather than deleted.")]
    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>Deletes this process's sandbox directory. Never throws.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort teardown: a failure deleting the session sandbox directory is logged and swallowed so it never blocks host disposal.")]
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
                LogSessionCleaned(Root);
            }
        }
        catch (Exception ex)
        {
            LogSessionCleanFailed(ex, Root);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sandbox mount provisioned at {SessionRoot} → virtual {VirtualPath}")]
    private partial void LogSandboxProvisioned(string sessionRoot, string virtualPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleaned orphan sandbox directory {Path}")]
    private partial void LogOrphanCleaned(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to clean orphan sandbox directory {Path}")]
    private partial void LogOrphanCleanFailed(Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cleaned session sandbox directory {Path}")]
    private partial void LogSessionCleaned(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to clean session sandbox directory {Path}")]
    private partial void LogSessionCleanFailed(Exception ex, string path);
}
