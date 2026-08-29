using Orkeon.Constants.FileSystem;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Hosting;

/// <summary>
/// Shared settings resolution logic for Orkeon runners.
/// </summary>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: probes user-supplied appsettings.json locations on the physical disk before any VFS mount exists.")]
public static class RunnerSettings
{
    /// <summary>
    /// Test seam: overrides <see cref="GetGlobalSettingsPath"/> so tests never depend on
    /// (or leak into) the machine's real per-user configuration directory.
    /// </summary>
    internal static string? GlobalSettingsPathOverride { get; set; }

    /// <summary>
    /// Test seam: overrides <see cref="Environment.GetFolderPath(Environment.SpecialFolder, Environment.SpecialFolderOption)"/>
    /// inside <see cref="GetGlobalSettingsPath"/>. On desktop OSes <c>GetFolderPath</c>
    /// virtually never returns an empty string (Unix falls back to <c>getpwuid</c>), so the
    /// bare-container branches below are unreachable without this seam.
    /// </summary>
    internal static Func<Environment.SpecialFolder, string>? SpecialFolderPathOverride { get; set; }

    private static string GetSpecialFolderPath(Environment.SpecialFolder folder) =>
        SpecialFolderPathOverride?.Invoke(folder)
        ?? Environment.GetFolderPath(folder, Environment.SpecialFolderOption.Create);

    /// <summary>
    /// The global per-user configuration path written by <c>orkeon init</c>:
    /// <c>%APPDATA%\Orkeon\appsettings.json</c> on Windows,
    /// <c>$XDG_CONFIG_HOME/Orkeon/appsettings.json</c> (else
    /// <c>~/.config/Orkeon/appsettings.json</c>) on Linux <b>and</b> macOS — macOS does
    /// not use <c>~/Library/Application Support</c>, which is where
    /// <see cref="Environment.SpecialFolder.ApplicationData"/> points since .NET 8.
    /// Never the install directory — that is what keeps upgrades safe. The returned
    /// path is always absolute.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No per-user configuration directory can be determined (no ApplicationData folder
    /// and no resolvable home directory) — e.g. a bare container without <c>HOME</c>.
    /// </exception>
    public static string GetGlobalSettingsPath()
    {
        if (GlobalSettingsPathOverride is not null)
            return GlobalSettingsPathOverride;

        // On a fresh HOME (bare container, first run), plain GetFolderPath returns "" when
        // the folder does not exist yet — and Path.Combine("", …) silently degrades to a
        // RELATIVE path that init would write into the cwd and resolution would never find
        // again. SpecialFolderOption.Create creates the folder and keeps the path absolute.
        var appData = GetSpecialFolderPath(Environment.SpecialFolder.ApplicationData);

        // Since .NET 8, ApplicationData maps to ~/Library/Application Support on macOS —
        // but Orkeon documents (and the release smokes assert) the XDG convention on every
        // Unix: $XDG_CONFIG_HOME, else ~/.config, exactly like Linux. Caught by the first
        // macos-latest smoke run: init wrote where neither the docs nor resolution looked.
        if (OperatingSystem.IsMacOS())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            appData = !string.IsNullOrEmpty(xdg) && Path.IsPathRooted(xdg)
                ? xdg
                : ""; // falls through to the home-derived ~/.config below
        }

        if (string.IsNullOrEmpty(appData))
        {
            // Exotic fallback: derive the conventional location from the home directory.
            var home = GetSpecialFolderPath(Environment.SpecialFolder.UserProfile);
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

            appData = OperatingSystem.IsWindows()
                ? Path.Combine(home, "AppData", "Roaming")
                : Path.Combine(home, ".config");
        }

        var path = Path.Combine(appData, "Orkeon", ConventionalNames.SettingsFile);
        if (!Path.IsPathRooted(path))
        {
            throw new InvalidOperationException(
                $"The per-user configuration directory resolved to a relative path ('{path}'). " +
                "Set the HOME (Linux/macOS) or APPDATA (Windows) environment variable, or pass " +
                "an explicit path (`orkeon init --path <file>` / `--settings <file>`).");
        }
        return path;
    }

    /// <summary>
    /// Resolves the appsettings.json path using a fallback chain:
    ///   1. Explicit --settings arg
    ///   2. appsettings.json next to config.yaml
    ///   3. examples/appsettings/appsettings.json (walk up from config dir;
    ///      examples/_shared/appsettings.json is a deprecated fallback, kept one
    ///      release for compatibility — remove at the next release).
    ///   4. Global per-user config written by <c>orkeon init</c>
    ///      (see <see cref="GetGlobalSettingsPath"/>).
    /// </summary>
    /// <param name="explicitPath">Explicit <c>--settings</c> path, or <see langword="null"/>.</param>
    /// <param name="configDir">Directory the crew config lives in (resolution anchor).</param>
    /// <param name="quiet">
    /// Suppresses the stderr diagnostics — for callers such as <c>orkeon doctor</c> that
    /// replay the chain and report the outcome themselves.
    /// </param>
    public static string? ResolveSettingsPath(string? explicitPath, string configDir, bool quiet = false)
    {
        // 1. Explicit
        if (!string.IsNullOrEmpty(explicitPath))
        {
            var full = Path.GetFullPath(explicitPath);
            if (File.Exists(full)) return full;
            if (!quiet)
                Console.Error.WriteLine($"WARNING: Explicit settings not found: {full}");
            return null;
        }

        // 2. Per-example override (next to config.yaml)
        var localSettings = Path.Combine(configDir, ConventionalNames.SettingsFile);
        if (File.Exists(localSettings)) return localSettings;

        // 3. Walk up to find the canonical appsettings/appsettings.json.
        //    Fall back to the legacy _shared/appsettings.json only if the
        //    canonical one is absent (temporary — drop at the next release).
        var dir = new DirectoryInfo(configDir);
        while (dir != null)
        {
            var canonical = Path.Combine(dir.FullName, "appsettings", ConventionalNames.SettingsFile);
            if (File.Exists(canonical)) return canonical;

            // DEPRECATED: legacy _shared location, kept one release for compatibility.
            var legacyShared = Path.Combine(dir.FullName, "_shared", ConventionalNames.SettingsFile);
            if (File.Exists(legacyShared)) return legacyShared;

            if (File.Exists(Path.Combine(dir.FullName, "Orkeon.Examples.sln"))) break;
            dir = dir.Parent;
        }

        // 4. Global per-user config (written by `orkeon init`) — works from any cwd.
        //    An unresolvable per-user directory (bare container without HOME) simply means
        //    "no global config": resolution degrades to env-vars-only instead of crashing.
        string? globalSettings;
        try { globalSettings = GetGlobalSettingsPath(); }
        catch (InvalidOperationException) { globalSettings = null; }
        if (globalSettings is not null && File.Exists(globalSettings)) return globalSettings;

        if (!quiet)
        {
            Console.Error.WriteLine(
                "WARNING: No appsettings.json found. Using environment variables only. " +
                "Run `orkeon init` to create a configuration.");
        }
        return null;
    }

    /// <summary>
    /// The mount strings a settings file declares under <c>Orkeon:FileSystem:Mounts</c> and
    /// <c>Orkeon:FileSystem:InternalMounts</c>.
    /// <para>
    /// The reserved-root guard used to see only the <c>--mount</c> arguments, so the same
    /// collision written in <c>appsettings.json</c> reached <c>FileSystemRegistry</c> and came
    /// back as "Duplicate virtual paths" out of a DI factory. A mount claims a root the same
    /// way whichever of the two it was written in, so the guard has to be shown both. Both
    /// SECTIONS too: the registry's duplicate check spans them, so a root claimed under
    /// InternalMounts collides exactly as loudly.
    /// </para>
    /// <para>
    /// Read with the JSON reader rather than a configuration builder on purpose: this runs
    /// before the host exists, it must not apply environment or command-line overlays, and a
    /// malformed file is not this method's business to report - the host build says it better,
    /// with the path and the parse position. Anything it cannot read is no mounts, never a
    /// throw.
    /// </para>
    /// <para>
    /// The reader is deliberately configured to accept what
    /// <c>JsonConfigurationFileParser</c> accepts - comments and trailing commas - and to match
    /// property names the way the binder does, case-insensitively. This method shadows that
    /// binder: every dialect difference between them is a file whose mounts the host will
    /// register and this guard will not see, which fails OPEN. That is the failure direction a
    /// guard must not have.
    /// </para>
    /// </summary>
    /// <param name="settingsPath">Resolved settings file, or <see langword="null"/>.</param>
    /// <returns>The declared mount strings, in file order; empty when there are none.</returns>
    public static IReadOnlyList<string> ReadDeclaredMounts(string? settingsPath)
    {
        if (settingsPath is not { Length: > 0 } || !File.Exists(settingsPath))
            return [];

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(settingsPath),
                new System.Text.Json.JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                });

            if (!TryGetSection(document.RootElement, "Orkeon", out var orkeon)
                || !TryGetSection(orkeon, "FileSystem", out var fileSystem))
            {
                return [];
            }

            return
            [
                .. ReadMountArray(fileSystem, "Mounts"),
                .. ReadMountArray(fileSystem, "InternalMounts"),
            ];
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// A child object by name, case-insensitively, and only when both it and its parent really
    /// are objects.
    /// <para>
    /// <c>JsonElement.TryGetProperty</c> THROWS <see cref="InvalidOperationException"/> on an
    /// element that is not an object - it does not return false. So <c>{"Orkeon": null}</c>, a
    /// file the configuration binder accepts without complaint, used to take the whole process
    /// down with a raw stack trace from a method documented as "never a throw", at a call site
    /// outside any try.
    /// </para>
    /// </summary>
    private static bool TryGetSection(System.Text.Json.JsonElement parent, string name, out System.Text.Json.JsonElement section)
    {
        section = default;
        if (parent.ValueKind != System.Text.Json.JsonValueKind.Object)
            return false;

        foreach (var property in parent.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (property.Value.ValueKind != System.Text.Json.JsonValueKind.Object)
                return false;
            section = property.Value;
            return true;
        }

        return false;
    }

    /// <summary>The non-empty strings in a named array property, case-insensitively.</summary>
    private static IReadOnlyList<string> ReadMountArray(System.Text.Json.JsonElement parent, string name)
    {
        foreach (var property in parent.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (property.Value.ValueKind != System.Text.Json.JsonValueKind.Array)
                return [];

            return [.. property.Value.EnumerateArray()
                .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(s => s.Length > 0)];
        }

        return [];
    }
}
