using System.Globalization;
using Microsoft.Extensions.Configuration;
using Orkeon.Constants.FileSystem;
using Orkeon.Compliance.Vfs;
using Orkeon.Constants.Configuration;

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
            var xdg = Environment.GetEnvironmentVariable(EnvironmentVariableNames.XdgConfigHome);
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
    /// The configuration a runner reads before its host exists, composed by
    /// <see cref="ComposeSources(IConfigurationBuilder, string?)"/> exactly as the runner host
    /// composes it: the environment variables without a prefix, the settings file at
    /// <paramref name="settingsPath"/> (when it exists), then the <c>ORKEON_</c> environment
    /// variables. For the CLI paths that read a setting without building a host: <c>orkeon
    /// doctor</c>, whose verdict is then the one a run would reach, <c>orkeon forge</c>, the probe of
    /// <c>orkeon init</c>, the esbuild toolchain (<c>Orkeon:Scripting:Toolchain</c>).
    /// </summary>
    /// <param name="settingsPath">Resolved settings path (<see cref="ResolveSettingsPath"/>), or <see langword="null"/>.</param>
    public static IConfiguration ReadConfiguration(string? settingsPath) =>
        ComposeSources(new ConfigurationBuilder(), settingsPath).Build();

    /// <summary>
    /// <see cref="ComposeSources(IConfigurationBuilder, IEnumerable{string}, bool)"/> over the one
    /// settings file a runner resolved: read when it exists, none otherwise.
    /// </summary>
    /// <param name="builder">The configuration builder, emptied first.</param>
    /// <param name="settingsPath">Resolved settings path (<see cref="ResolveSettingsPath"/>), or <see langword="null"/>.</param>
    /// <returns><paramref name="builder"/>, for chaining.</returns>
    public static IConfigurationBuilder ComposeSources(IConfigurationBuilder builder, string? settingsPath) =>
        ComposeSources(builder, settingsPath is not null && File.Exists(settingsPath) ? [settingsPath] : [], optional: true);

    /// <summary>
    /// The settings sources of every Orkeon host, in their one order (GAP-36): the environment
    /// variables without a prefix, the lowest layer — where the standard <c>OTEL_*</c> variables a
    /// collector or a .NET Aspire AppHost sets reach the OpenTelemetry exporter —, then the settings
    /// files, then the <c>ORKEON_</c> variables, prefix removed, which win. Whatever
    /// <paramref name="builder"/> held is removed first: the default host lays the
    /// <c>appsettings.json</c> and <c>appsettings.{Environment}.json</c> of the current directory —
    /// since .NET 10, <c>&lt;application&gt;.settings.json</c> and its environment twin too — and
    /// its user secrets under the file the resolution chain chose: keys, profiles, MCP servers,
    /// mounts that file never declared, which no diagnostic saw. The runner host, the REPL and
    /// <see cref="ReadConfiguration"/> compose with this method, so <c>orkeon doctor</c> reads what a
    /// run reads. No source watches its file: a run never reloads its settings.
    /// </summary>
    /// <param name="builder">The configuration builder, typically a host's app configuration; emptied first.</param>
    /// <param name="settingsFiles">The settings files, in the order they are laid; a relative path is read from the current directory.</param>
    /// <param name="optional">
    /// Whether a file that does not exist is skipped, or refused when the configuration is built —
    /// the REPL's <c>--settings</c> files.
    /// </param>
    /// <returns><paramref name="builder"/>, for chaining.</returns>
    public static IConfigurationBuilder ComposeSources(IConfigurationBuilder builder, IEnumerable<string> settingsFiles, bool optional)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(settingsFiles);

        builder.Sources.Clear();
        builder.AddEnvironmentVariables();
        foreach (var file in settingsFiles)
        {
            // Absolute: a bare configuration builder reads a relative file from the binary's own
            // folder, and the host's from its content root; the operator meant the current directory.
            builder.AddJsonFile(Path.GetFullPath(file), optional, reloadOnChange: false);
        }

        builder.AddEnvironmentVariables(OrkeonEnvironmentPrefix);
        return builder;
    }

    /// <summary>The prefix of the environment variables every Orkeon host reads as configuration, prefix removed.</summary>
    private const string OrkeonEnvironmentPrefix = EnvironmentVariableNames.SettingsPrefix;

    /// <summary>
    /// What to tell an operator about a settings file the configuration cannot read (GAP-35): the
    /// file — the runtime's own sentence names it — and, for JSON it cannot parse, why and where,
    /// the line and the position counted from 1 as an editor counts them. The runner host and
    /// <c>orkeon-host</c>'s boot configuration say it the same way.
    /// </summary>
    /// <param name="ex">What reading the configuration threw.</param>
    internal static string DescribeUnreadableSettings(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        System.Text.Json.JsonException? json = null;
        for (var inner = ex.InnerException; inner is not null && json is null; inner = inner.InnerException)
            json = inner as System.Text.Json.JsonException;

        if (json is null)
            return ex.InnerException is { } cause ? $"{ex.Message.TrimEnd('.')}: {cause.Message}" : ex.Message;

        // The parser appends its own 0-based place, a line number and a byte position in the line;
        // it is said again below, counted from 1.
        var reason = json.Message;
        var place = reason.IndexOf(" LineNumber:", StringComparison.Ordinal);
        if (place >= 0)
            reason = reason[..place];
        reason = reason.TrimEnd().TrimEnd('.');

        var where = json.LineNumber is { } line
            ? string.Create(CultureInfo.InvariantCulture, $" (line {line + 1}, position {(json.BytePositionInLine ?? 0) + 1})")
            : string.Empty;
        return $"{ex.Message.TrimEnd('.')}: {reason}{where}.";
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
    /// <param name="warnings">
    /// Where the chain's warnings are written — stderr for a runner —, or <see langword="null"/>
    /// to keep them unsaid, for callers such as <c>orkeon doctor</c> that replay the chain and
    /// report the outcome themselves.
    /// </param>
    public static string? ResolveSettingsPath(string? explicitPath, string configDir, TextWriter? warnings)
    {
        // 1. Explicit
        if (!string.IsNullOrEmpty(explicitPath))
            return ResolveExplicitSettingsPath(explicitPath, warnings);

        // 2. Per-example override (next to config.yaml)
        var localSettings = Path.Combine(configDir, ConventionalNames.SettingsFile);
        if (File.Exists(localSettings)) return localSettings;

        // 3. Canonical appsettings/appsettings.json, found by walking up from the config dir.
        var walkedUp = FindSettingsByWalkingUp(configDir);
        if (walkedUp is not null) return walkedUp;

        // 4. Global per-user config (written by `orkeon init`) — works from any cwd.
        var globalSettings = TryGetGlobalSettingsPath();
        if (globalSettings is not null && File.Exists(globalSettings)) return globalSettings;

        warnings?.WriteLine(
            "WARNING: No appsettings.json found. Using environment variables only. " +
            "Run `orkeon init` to create a configuration.");
        return null;
    }

    /// <summary>
    /// Step 1 of the chain: the operator's own <c>--settings</c> path. A path that does not
    /// exist is a mistake worth naming rather than silently falling through to the
    /// conventional locations, so it ends the resolution either way.
    /// </summary>
    private static string? ResolveExplicitSettingsPath(string explicitPath, TextWriter? warnings)
    {
        var full = Path.GetFullPath(explicitPath);
        if (File.Exists(full)) return full;
        warnings?.WriteLine($"WARNING: Explicit settings not found: {full}");
        return null;
    }

    /// <summary>
    /// Step 3 of the chain: walk up from the config directory looking for the canonical
    /// <c>appsettings/appsettings.json</c>, falling back to the legacy
    /// <c>_shared/appsettings.json</c> only when the canonical one is absent (temporary —
    /// drop at the next release). The walk stops at an examples solution root.
    /// </summary>
    private static string? FindSettingsByWalkingUp(string configDir)
    {
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

        return null;
    }

    /// <summary>
    /// Step 4 of the chain: the per-user config path, or null. An unresolvable per-user
    /// directory (bare container without HOME) simply means "no global config": resolution
    /// degrades to env-vars-only instead of crashing.
    /// </summary>
    private static string? TryGetGlobalSettingsPath()
    {
        try { return GetGlobalSettingsPath(); }
        catch (InvalidOperationException) { return null; }
    }


    /// <summary>
    /// The mount strings the configuration of a run declares under <c>Orkeon:FileSystem:Mounts</c>
    /// and <c>Orkeon:FileSystem:InternalMounts</c>: the agent-facing entries, then the Internal ones,
    /// each by ascending index.
    /// <para>
    /// The reserved-root guard used to see only the <c>--mount</c> arguments, so the same
    /// collision written in <c>appsettings.json</c> reached <c>FileSystemRegistry</c> and came
    /// back as "Duplicate virtual paths" out of a DI factory. A mount claims a root the same
    /// way wherever it was declared, so the guard has to be shown every declaration. Both
    /// SECTIONS too: the registry's duplicate check spans them, so a root claimed under
    /// InternalMounts collides exactly as loudly.
    /// </para>
    /// <para>
    /// Read from <see cref="ReadConfiguration"/>, the layers the host composes (GAP-49, decision 2):
    /// the environment without a prefix, the settings file, the <c>ORKEON_</c> environment. The
    /// method used to shadow the configuration binder with a JSON reader of its own, which read the
    /// file alone: every layer and dialect difference between them was a mount the host registers
    /// and the guard does not see, which fails OPEN - the direction a guard must not have. A file
    /// the configuration cannot read is no mounts, never a throw: the host build refuses it next,
    /// naming the file and the place (GAP-35).
    /// </para>
    /// </summary>
    /// <param name="settingsPath">Resolved settings file, or <see langword="null"/>.</param>
    /// <returns>The declared mount strings; empty when there are none.</returns>
    public static IReadOnlyList<string> ReadDeclaredMounts(string? settingsPath) =>
        ReadDeclaredMounts(ReadMountConfiguration(settingsPath));

    /// <summary>
    /// <see cref="ReadDeclaredMounts(string?)"/> over a configuration the caller composed: the
    /// REPL's layers, its command line included.
    /// </summary>
    /// <param name="configuration">The configuration the host is built over, before its own mounts.</param>
    internal static IReadOnlyList<string> ReadDeclaredMounts(IConfiguration configuration) =>
    [
        .. ReadDeclaredAgentFacingMounts(configuration).Select(entry => entry.Spec),
        .. ReadDeclaredInternalMounts(configuration),
    ];

    /// <summary>
    /// The agent-facing mounts the configuration of a run declares (<c>Orkeon:FileSystem:Mounts</c>
    /// only), each at the index the configuration keys it under. The index is what the mount
    /// selection withdraws an entry by (VFS-90), so an entry that is not a mount string — written
    /// <c>null</c> or empty, or emptied by an <c>ORKEON_</c> variable — is skipped but keeps its
    /// place: the entries after it keep the indices the host will see.
    /// <para>
    /// Same layers and same failure stance as <see cref="ReadDeclaredMounts(string?)"/>.
    /// </para>
    /// </summary>
    /// <param name="settingsPath">Resolved settings file, or <see langword="null"/>.</param>
    public static IReadOnlyList<DeclaredMountEntry> ReadDeclaredAgentFacingMounts(string? settingsPath) =>
        ReadDeclaredAgentFacingMounts(ReadMountConfiguration(settingsPath));

    /// <summary><see cref="ReadDeclaredAgentFacingMounts(string?)"/> over a configuration the caller composed.</summary>
    /// <param name="configuration">The configuration the host is built over, before its own mounts.</param>
    internal static IReadOnlyList<DeclaredMountEntry> ReadDeclaredAgentFacingMounts(IConfiguration configuration) =>
        DeclaredEntries(configuration, ConfigurationKeys.FileSystemMounts);

    /// <summary>
    /// The Internal mounts the configuration of a run declares
    /// (<c>Orkeon:FileSystem:InternalMounts</c> only), by ascending index. Same layers and same
    /// failure stance as <see cref="ReadDeclaredMounts(string?)"/>.
    /// </summary>
    /// <param name="settingsPath">Resolved settings file, or <see langword="null"/>.</param>
    public static IReadOnlyList<string> ReadDeclaredInternalMounts(string? settingsPath) =>
        ReadDeclaredInternalMounts(ReadMountConfiguration(settingsPath));

    /// <summary><see cref="ReadDeclaredInternalMounts(string?)"/> over a configuration the caller composed.</summary>
    /// <param name="configuration">The configuration the host is built over, before its own mounts.</param>
    internal static IReadOnlyList<string> ReadDeclaredInternalMounts(IConfiguration configuration) =>
        [.. DeclaredEntries(configuration, ConfigurationKeys.FileSystemInternalMounts).Select(entry => entry.Spec)];

    /// <summary>
    /// The configuration the mount readers of <paramref name="settingsPath"/> read: <see cref="ReadConfiguration"/>,
    /// or an empty one when a settings file cannot be read — no mounts, never a throw, since the
    /// host build refuses that file next, naming it and the place in it (GAP-35).
    /// </summary>
    /// <param name="settingsPath">Resolved settings file, or <see langword="null"/>.</param>
    internal static IConfiguration ReadMountConfiguration(string? settingsPath)
    {
        try
        {
            return ReadConfiguration(settingsPath);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or IOException or UnauthorizedAccessException)
        {
            return new ConfigurationBuilder().Build();
        }
    }

    /// <summary>
    /// The numbered children of <paramref name="section"/>, by ascending index, each with its value:
    /// null or blank for an entry written <c>null</c>, emptied, or withdrawn. The one reading of an
    /// indexed section for the host — what it places its own entries against — and for the guards,
    /// so the two never read two lists.
    /// </summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <param name="section">The section path, e.g. <c>Orkeon:FileSystem:Mounts</c>.</param>
    internal static List<(int Index, string? Value)> IndexedEntries(IConfiguration configuration, string section)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var entries = new List<(int Index, string? Value)>();
        foreach (var child in configuration.GetSection(section).GetChildren())
        {
            if (int.TryParse(child.Key, CultureInfo.InvariantCulture, out var index))
                entries.Add((index, child.Value));
        }

        entries.Sort((a, b) => a.Index.CompareTo(b.Index));
        return entries;
    }

    /// <summary>The entries of <paramref name="section"/> that hold a mount string, at their own index.</summary>
    private static List<DeclaredMountEntry> DeclaredEntries(IConfiguration configuration, string section) =>
        IndexedEntries(configuration, section)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .Select(entry => new DeclaredMountEntry(entry.Index, entry.Value!))
            .ToList();
}
