using Microsoft.Extensions.Logging;
using Orkeon.Compliance.Vfs;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Hosting;

/// <summary>
/// Shared logging helpers for Orkeon runners.
/// </summary>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: prints the physical base paths of CLI-declared mounts; runs on raw mount strings before the VFS resolves them.")]
public static partial class RunnerLogging
{
    /// <summary>
    /// Logs the active virtual filesystem mounts in Linux mount-style format,
    /// showing physical path → virtual path (rights).
    /// </summary>
    public static void LogMounts(IReadOnlyList<string> cliMounts, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(cliMounts);
        if (cliMounts.Count == 0) return;

        LogMountsHeader(logger);
        foreach (var raw in cliMounts)
        {
            try
            {
                var mount = FileSystemMount.Parse(raw);
                var rights = FormatRights(mount.DefaultRights);
                var absolutePath = Path.GetFullPath(mount.BasePath);
                LogMountEntry(logger, absolutePath, mount.VirtualPath, rights);

                foreach (var ov in mount.Overrides)
                {
                    var overrideRights = FormatRights(ov.Rights);
                    LogMountOverride(logger, ov.RelativePath, overrideRights);
                }
            }
            catch (FormatException)
            {
                LogInvalidMount(logger, raw);
            }
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "── Virtual filesystem mounts ──")]
    private static partial void LogMountsHeader(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "  {BasePath} on {VirtualPath} type vfs ({Rights})")]
    private static partial void LogMountEntry(ILogger logger, string basePath, string virtualPath, string rights);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "    └─ {SubPath} ({OverrideRights})")]
    private static partial void LogMountOverride(ILogger logger, string subPath, string overrideRights);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "  (invalid mount: {Raw})")]
    private static partial void LogInvalidMount(ILogger logger, string raw);

    private static string FormatRights(FileAccessRights rights) => rights switch
    {
        FileAccessRights.ReadOnly          => "ro",
        FileAccessRights.ReadWrite         => "rw",
        FileAccessRights.ReadWriteNoDelete => "rwnd",
#pragma warning disable CA1308 // lowercase is the produced/displayed rights token returned by FormatRights, not a comparison normalization
        _                                  => rights.ToString().ToLowerInvariant()
#pragma warning restore CA1308
    };
}
