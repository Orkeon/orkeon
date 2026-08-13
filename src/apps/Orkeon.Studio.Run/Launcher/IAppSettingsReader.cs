using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Run.Launcher;

/// <summary>
/// Reads the appsettings file a launch was pinned to, so the launcher can list the mounts
/// it already declares next to the launch-only ones (SPEC §5.2). An interface so the
/// launcher's mount panel can be exercised without a disk.
/// </summary>
internal interface IAppSettingsReader
{
    /// <summary>
    /// Reads <paramref name="path"/>. A missing or unreadable file is reported through
    /// <paramref name="failureReason"/>, never thrown: an appsettings file that is not there yet is
    /// an ordinary state of the launcher, not a fault.
    /// </summary>
    bool TryRead(string path, out string? json, out string? failureReason);
}

/// <summary>Real-disk <see cref="IAppSettingsReader"/>.</summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; it reads the user-picked " +
    "appsettings.json from the physical disk to preview its mounts, before any VFS mount exists.")]
internal sealed class PhysicalAppSettingsReader : IAppSettingsReader
{
    /// <summary>Shared stateless instance.</summary>
    public static PhysicalAppSettingsReader Instance { get; } = new();

    /// <inheritdoc />
    public bool TryRead(string path, out string? json, out string? failureReason)
    {
        json = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            failureReason = "No settings file was given.";
            return false;
        }

        if (!File.Exists(path))
        {
            failureReason = $"'{path}' does not exist yet.";
            return false;
        }

        try
        {
            json = File.ReadAllText(path);
            failureReason = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failureReason = $"'{path}' could not be read: {ex.Message}";
            return false;
        }
    }
}
