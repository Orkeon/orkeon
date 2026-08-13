using Orkeon.Compliance.Vfs;
using Orkeon.Hosting;
using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Core.Storage;

/// <summary>One step of the settings resolution chain, for display by the UIs.</summary>
/// <param name="Order">1-based rank; the first step that matches wins.</param>
/// <param name="Title">Short label.</param>
/// <param name="Description">What the runtime actually looks for at this step.</param>
public sealed record SettingsResolutionStep(int Order, string Title, string Description);

/// <summary>
/// Where a settings file can be written, and where the runtime will look for one.
/// The global path comes from <see cref="RunnerSettings.GetGlobalSettingsPath"/> — the
/// same path <c>orkeon init</c> writes — so Studio and the CLI can never disagree
/// about the per-user location.
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; it resolves user-chosen appsettings.json " +
    "locations on the physical disk, before any VFS mount exists.")]
public static class SettingsLocations
{
    /// <summary>
    /// The per-user global file: <c>%APPDATA%\Orkeon\appsettings.json</c> on Windows,
    /// <c>$XDG_CONFIG_HOME/Orkeon/appsettings.json</c> (else <c>~/.config/…</c>) elsewhere.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No per-user configuration directory can be determined (a bare container without
    /// <c>HOME</c>). Use <see cref="TryGetGlobalSettingsPath"/> to surface that as a message.
    /// </exception>
    public static string GetGlobalSettingsPath() => RunnerSettings.GetGlobalSettingsPath();

    /// <summary>Non-throwing form of <see cref="GetGlobalSettingsPath"/>, for a UI field.</summary>
    public static bool TryGetGlobalSettingsPath(out string? path, out string? error)
    {
        try
        {
            path = RunnerSettings.GetGlobalSettingsPath();
            error = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            path = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// The four steps of <see cref="RunnerSettings.ResolveSettingsPath"/>, in order, so a
    /// UI can show which file a launch will actually load.
    /// </summary>
    public static IReadOnlyList<SettingsResolutionStep> ResolutionChain { get; } =
    [
        new(1, "Explicit path",
            "The file passed to the runner with --settings. If it does not exist, resolution stops and " +
            "the runtime falls back to environment variables only."),
        new(2, "Next to the crew",
            $"{AppSettingsDocument.FileName} in the directory holding the crew configuration."),
        new(3, "Shared appsettings directory",
            $"appsettings/{AppSettingsDocument.FileName}, searched by walking up from the crew directory " +
            "(the legacy _shared/ location is still accepted for one release)."),
        new(4, "Global per-user file",
            "The file written by `orkeon init`: %APPDATA%\\Orkeon\\appsettings.json on Windows, " +
            "$XDG_CONFIG_HOME/Orkeon/appsettings.json (else ~/.config/Orkeon/appsettings.json) elsewhere."),
    ];

    /// <summary>
    /// Normalizes a user-picked save target the way <c>orkeon init --path</c> does: a
    /// directory receives an <c>appsettings.json</c>, anything else is taken as the file
    /// itself, and the result is absolute.
    /// </summary>
    public static string NormalizeTargetPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = Path.GetFullPath(path);
        return Directory.Exists(full) ? Path.Combine(full, AppSettingsDocument.FileName) : full;
    }
}
