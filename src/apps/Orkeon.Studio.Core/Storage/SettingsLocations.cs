using System.Globalization;
using Orkeon.Compliance.Vfs;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Core.Storage;

/// <summary>One step of the settings resolution chain, for display by the UIs.</summary>
/// <param name="Order">1-based rank; the first step that matches wins.</param>
/// <param name="Title">Short label.</param>
/// <param name="Description">What the runtime actually looks for at this step.</param>
public sealed record SettingsResolutionStep(int Order, string Title, string Description);

/// <summary>
/// Where a settings file can be written, and where the runtime will look for one.
/// <para>
/// <see cref="GetGlobalSettingsPath"/> reproduces <c>Orkeon.Hosting.RunnerSettings</c>'s own
/// resolution rather than calling it: referencing <c>Orkeon.Hosting</c> for this one method
/// pulled the entire runtime — ONNX, tree-sitter, the local embedding model — into every
/// self-contained Studio publish (STUDIO-01 §7). The copy is pinned against the real thing by
/// <c>ConstantDriftTests</c> in <c>Orkeon.Studio.Core.Tests</c>, which does reference Hosting.
/// </para>
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; it resolves user-chosen appsettings.json " +
    "locations on the physical disk, before any VFS mount exists.")]
public static class SettingsLocations
{
    /// <summary>Directory the per-user configuration lives in, under the config root.</summary>
    private const string ProductDirectoryName = "Orkeon";

    /// <summary>
    /// The per-user global file: <c>%APPDATA%\Orkeon\appsettings.json</c> on Windows,
    /// <c>$XDG_CONFIG_HOME/Orkeon/appsettings.json</c> (else <c>~/.config/…</c>) elsewhere —
    /// macOS included, which is why <see cref="Environment.SpecialFolder.ApplicationData"/>
    /// (<c>~/Library/Application Support</c> since .NET 8) is deliberately not used there.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No per-user configuration directory can be determined (a bare container without
    /// <c>HOME</c>). Use <see cref="TryGetGlobalSettingsPath"/> to surface that as a message.
    /// </exception>
    public static string GetGlobalSettingsPath()
    {
        // SpecialFolderOption.Create matters: on a fresh HOME plain GetFolderPath returns "",
        // and Path.Combine("", …) silently yields a RELATIVE path that would be written into
        // the current directory and never found again.
        var configRoot = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);

        if (OperatingSystem.IsMacOS())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            configRoot = !string.IsNullOrEmpty(xdg) && Path.IsPathRooted(xdg)
                ? xdg
                : ""; // falls through to the home-derived ~/.config below
        }

        if (string.IsNullOrEmpty(configRoot))
            configRoot = DeriveConfigRootFromHome();

        var path = Path.Combine(configRoot, ProductDirectoryName, AppSettingsDocument.FileName);
        if (!Path.IsPathRooted(path))
        {
            throw new InvalidOperationException(
                $"The per-user configuration directory resolved to a relative path ('{path}'). " +
                "Set the HOME (Linux/macOS) or APPDATA (Windows) environment variable, or pass " +
                "an explicit path (`orkeon init --path <file>` / `--settings <file>`).");
        }

        return path;
    }

    /// <summary>Exotic fallback: derive the conventional config root from the home directory.</summary>
    private static string DeriveConfigRootFromHome()
    {
        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.Create);
        if (string.IsNullOrEmpty(home))
            home = Environment.GetEnvironmentVariable("HOME");

        if (string.IsNullOrEmpty(home))
        {
            throw new InvalidOperationException(
                "Cannot determine the per-user configuration directory: neither the " +
                "ApplicationData folder nor a home directory is available. Set the HOME " +
                "(Linux/macOS) or APPDATA (Windows) environment variable, or pass an " +
                "explicit path (`orkeon init --path <file>` / `--settings <file>`).");
        }

        return OperatingSystem.IsWindows()
            ? Path.Combine(home, "AppData", "Roaming")
            : Path.Combine(home, ".config");
    }

    /// <summary>Non-throwing form of <see cref="GetGlobalSettingsPath"/>, for a UI field.</summary>
    public static bool TryGetGlobalSettingsPath(out string? path, out string? error)
    {
        try
        {
            path = GetGlobalSettingsPath();
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
    /// The four steps of <c>RunnerSettings.ResolveSettingsPath</c>, in order, so a UI can show
    /// which file a launch will actually load.
    /// </summary>
    public static IReadOnlyList<SettingsResolutionStep> ResolutionChain { get; } =
        ResolutionChainFor(EnglishStudioStrings.Instance);

    /// <summary>The resolution chain with its wording resolved through a culture port (STUDIO-11).</summary>
    public static IReadOnlyList<SettingsResolutionStep> ResolutionChainFor(IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        return
        [
            new(1, strings[StudioStringKeys.ResolutionStep1Title],
                strings[StudioStringKeys.ResolutionStep1Description]),
            new(2, strings[StudioStringKeys.ResolutionStep2Title],
                string.Format(CultureInfo.InvariantCulture,
                    strings[StudioStringKeys.ResolutionStep2Description], AppSettingsDocument.FileName)),
            new(3, strings[StudioStringKeys.ResolutionStep3Title],
                string.Format(CultureInfo.InvariantCulture,
                    strings[StudioStringKeys.ResolutionStep3Description], AppSettingsDocument.FileName)),
            new(4, strings[StudioStringKeys.ResolutionStep4Title],
                strings[StudioStringKeys.ResolutionStep4Description]),
        ];
    }

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
