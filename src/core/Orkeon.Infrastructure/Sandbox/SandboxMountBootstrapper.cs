using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Infrastructure.Sandbox;

/// <summary>
/// Hosted service that provisions a per-session sandbox mount at <c>/sandbox</c> with
/// <see cref="MountVisibility.Internal"/> visibility (hidden from agents).
/// <para>
/// On startup it:
/// <list type="bullet">
///   <item>Creates a session directory at <c>{EphemeralRoot}/{pid}-{timestamp}</c>.</item>
///   <item>Registers the directory as an Internal read-write VFS mount.</item>
///   <item>Runs a janitor sweep to delete orphaned session directories older than
///         <see cref="SandboxFileSystemOptions.CleanupOrphansOlderThan"/>.</item>
/// </list>
/// </para>
/// <para>
/// On graceful shutdown (via <see cref="IHostApplicationLifetime.ApplicationStopping"/>)
/// it recursively deletes the session directory.
/// </para>
/// </summary>
internal sealed partial class SandboxMountBootstrapper : IHostedService
{
    private readonly SandboxFileSystemOptions _opts;
    private readonly FileSystemRegistry _registry;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<SandboxMountBootstrapper> _log;

    /// <summary>Physical path of the current session sandbox directory.</summary>
    private string _sessionRoot = "";

    /// <summary>Initializes a new instance of <see cref="SandboxMountBootstrapper"/>.</summary>
    public SandboxMountBootstrapper(
        IOptions<SandboxFileSystemOptions> opts,
        FileSystemRegistry registry,
        IHostApplicationLifetime lifetime,
        ILogger<SandboxMountBootstrapper> log)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(log);

        _opts = opts.Value;
        _registry = registry;
        _lifetime = lifetime;
        _log = log;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var root = _opts.EphemeralRoot
                   ?? Path.Combine(Path.GetTempPath(), "orkeon-sandbox");

        _sessionRoot = Path.Combine(root,
            $"{Environment.ProcessId}-{DateTime.UtcNow:yyyyMMddHHmmss}");

        // EXCEPTION-BOOTSTRAP: direct Directory.CreateDirectory call is intentional here.
        // This runs before the VFS sandbox mount is registered, so we cannot use IFileSystemService.
        // All other file system access in application code must go through the VFS abstraction.
        Directory.CreateDirectory(_sessionRoot);

        _registry.AddMount(new FileSystemMount(
            basePath: _sessionRoot,
            virtualPath: _opts.VirtualPath,
            defaultRights: FileAccessRights.ReadWrite,
            overrides: null,
            visibility: MountVisibility.Internal));

        CleanupOrphans(root, _opts.CleanupOrphansOlderThan);

        _lifetime.ApplicationStopping.Register(CleanupSession);

        LogSandboxProvisioned(_sessionRoot, _opts.VirtualPath);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>Cleanup is registered via <see cref="IHostApplicationLifetime.ApplicationStopping"/>; this is a no-op.</remarks>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Deletes orphaned session directories under <paramref name="root"/> whose
    /// <see cref="Directory.GetLastWriteTimeUtc"/> is older than <paramref name="threshold"/>.
    /// Failures are logged and swallowed so a corrupt orphan never prevents startup.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort orphan cleanup: a failure deleting one stale sandbox directory is logged and skipped so a corrupt orphan never prevents startup or blocks the other deletions.")]
    private void CleanupOrphans(string root, TimeSpan threshold)
    {
        if (!Directory.Exists(root))
            return;

        var cutoff = DateTime.UtcNow - threshold;

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            // Skip the session directory we just created
            if (string.Equals(dir, _sessionRoot, StringComparison.Ordinal))
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

    /// <summary>
    /// Deletes the current session sandbox directory recursively.
    /// Called when <see cref="IHostApplicationLifetime.ApplicationStopping"/> fires.
    /// Failures are logged and swallowed to avoid blocking graceful shutdown.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort shutdown cleanup: a failure deleting the session sandbox directory is logged and swallowed to avoid blocking graceful shutdown.")]
    private void CleanupSession()
    {
        try
        {
            if (Directory.Exists(_sessionRoot))
            {
                Directory.Delete(_sessionRoot, recursive: true);
                LogSessionCleaned(_sessionRoot);
            }
        }
        catch (Exception ex)
        {
            LogSessionCleanFailed(ex, _sessionRoot);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sandbox mount provisioned at {SessionRoot} → virtual {VirtualPath}")]
    private partial void LogSandboxProvisioned(string sessionRoot, string virtualPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleaned orphan sandbox directory {Path}")]
    private partial void LogOrphanCleaned(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to clean orphan sandbox directory {Path}")]
    private partial void LogOrphanCleanFailed(Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sandbox session directory cleaned up: {SessionRoot}")]
    private partial void LogSessionCleaned(string sessionRoot);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to clean up sandbox session directory {SessionRoot}")]
    private partial void LogSessionCleanFailed(Exception ex, string sessionRoot);
}
