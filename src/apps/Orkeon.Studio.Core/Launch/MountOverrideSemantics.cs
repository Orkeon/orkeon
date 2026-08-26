using System.Globalization;
using Orkeon.Compliance.Vfs;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Targets;

namespace Orkeon.Studio.Core.Launch;

/// <summary>Where an effective mount entry comes from.</summary>
public enum MountOrigin
{
    /// <summary>From the appsettings file's <c>Orkeon:FileSystem:Mounts</c> array.</summary>
    Settings,

    /// <summary>From a <c>--mount</c> argument.</summary>
    CommandLine,

    /// <summary>
    /// Inserted by the runner itself before any <c>--mount</c> argument: the crew's
    /// configuration directory as <c>/crew</c> (the script's directory as <c>/script</c> on the
    /// scripting path). Exactly one entry — the LLM log directory rides its own configuration
    /// key since ADR-008 and shifts nothing.
    /// </summary>
    AutoInjected,
}

/// <summary>One entry of the mount list the runtime will actually see.</summary>
/// <param name="Index">Position in <c>Orkeon:FileSystem:Mounts</c>.</param>
/// <param name="Value">The mount string in force.</param>
/// <param name="Origin">Which of the three sources won.</param>
/// <param name="ReplacedSettingsMount">
/// The appsettings entry this masks, when there was one at the same index.
/// </param>
public sealed record EffectiveMount(
    int Index,
    string Value,
    MountOrigin Origin,
    string? ReplacedSettingsMount = null)
{
    /// <summary>Configuration key this entry occupies.</summary>
    public string ConfigurationKey => MountOverrideSemantics.ConfigurationKey(Index);

    /// <summary>True when a settings entry was silently replaced by a higher-priority one.</summary>
    public bool OverridesSettings => Origin != MountOrigin.Settings && ReplacedSettingsMount is not null;
}

/// <summary>
/// The mounts the runner inserts <em>ahead of</em> every <c>--mount</c> argument, in the order
/// it inserts them. There is exactly one: the crew's configuration directory as <c>/crew:ro</c>
/// (<c>RunnerExecution.TryBuildHost</c>), or the script's directory as <c>/script:ro</c> on the
/// scripting path (<c>RunCommand</c>). This offset is why the user's first <c>--mount</c> does
/// not land on <c>Orkeon:FileSystem:Mounts:0</c>.
/// <para>
/// The LLM exchange log is <em>not</em> here: since ADR-008 it is registered as an internal
/// mount under its own configuration key, so it no longer shifts anything the user wrote.
/// </para>
/// </summary>
[SuppressVfsCompliance(
    "OUT-OF-SCOPE: predicts the mount strings the CLI will build for a path the user picked " +
    "outside any mount. Nothing is opened — this is string and index arithmetic used to show " +
    "the user which configuration key each --mount will occupy.")]
public sealed record MountAutoInjection
{
    /// <summary>
    /// The crew's virtual root on the engine side. Mirrors <c>RunnerMounts.CrewVirtualRoot</c>;
    /// Studio.Core cannot reference Orkeon.Hosting, so a drift test pins the pair.
    /// </summary>
    public const string CrewVirtualRoot = "/crew";

    /// <summary>Mirrors <c>RunnerMounts.ScriptVirtualRoot</c>. Same drift test.</summary>
    public const string ScriptVirtualRoot = "/script";

    /// <summary>Mirrors <c>RunnerMounts.LlmLogVirtualRoot</c>. Same drift test.</summary>
    public const string LlmLogVirtualRoot = "/llm-logs";

    /// <summary>
    /// The three together: a user <c>--mount</c> claiming one is refused by the engine at
    /// launch, so Studio refuses it in the editor rather than building a command that fails.
    /// </summary>
    public static IReadOnlyList<string> ReservedVirtualRoots { get; } =
        [CrewVirtualRoot, ScriptVirtualRoot, LlmLogVirtualRoot];

    /// <summary>The injected mount strings, in the order the runner inserts them.</summary>
    public required IReadOnlyList<string> Mounts { get; init; }

    /// <summary>
    /// The mounts the runner registers with <c>MountVisibility.Internal</c> — reachable by
    /// the VFS, invisible to agents. They ride their own configuration key, so they shift
    /// nothing and are excluded from <see cref="Count"/>.
    /// </summary>
    public IReadOnlyList<string> InternalMounts { get; init; } = [];

    /// <summary>How many entries the user's own mounts are shifted by.</summary>
    public int Count => Mounts.Count;

    /// <summary>
    /// The auto-injected mounts for <paramref name="target"/> launched with
    /// <paramref name="options"/>.
    /// </summary>
    /// <param name="target">The detected run target.</param>
    /// <param name="options">
    /// The launch options; only <see cref="RunLaunchOptions.LlmLogEnabled"/> and
    /// <see cref="RunLaunchOptions.LlmLogPath"/> affect the result.
    /// </param>
    /// <remarks>
    /// The default log directory is the relative <c>llm-logs</c>, which the CLI resolves
    /// against <em>its own</em> working directory; the path shown here is therefore only exact
    /// when Studio launches the CLI in its own working directory. The count — the part the
    /// index arithmetic depends on — is exact either way.
    /// </remarks>
    public static MountAutoInjection For(RunTarget target, RunLaunchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        var effective = options ?? new RunLaunchOptions();
        var mounts = new List<string> { DescribeTargetMount(target) };
        var internalMounts = ResolveLlmLogDirectory(effective) is { } logDirectory
            ? new[] { $"{logDirectory}:{LlmLogVirtualRoot}:rw" }
            : [];

        return new MountAutoInjection { Mounts = mounts, InternalMounts = internalMounts };
    }

    /// <summary>
    /// The runner's own mount for the target: the crew's directory as <c>/crew</c>, a
    /// script's directory as <c>/script</c>. Both are names, never the folder's own path —
    /// mirrors <c>RunnerMounts</c> on the engine side, pinned by a drift test.
    /// </summary>
    private static string DescribeTargetMount(RunTarget target)
    {
        if (target.Dialect == RunTargetDialect.Script)
            return $"{DirectoryOf(target.RunPath)}:{ScriptVirtualRoot}:ro";

        var configDirectory = target.Kind == RunTargetKind.MultiFileCrewDirectory
            ? Path.TrimEndingDirectorySeparator(FullPath(target.RunPath))
            : DirectoryOf(target.RunPath);

        return $"{configDirectory}:{CrewVirtualRoot}:ro";
    }

    /// <summary>
    /// The log directory the runner would mount, or <see langword="null"/> when LLM exchange
    /// logging is off. Mirrors <c>RunCommandOptions.ResolvedLlmLogPath</c>, including its
    /// default directory name.
    /// </summary>
    private static string? ResolveLlmLogDirectory(RunLaunchOptions options)
    {
        if (!options.LlmLogEnabled && string.IsNullOrWhiteSpace(options.LlmLogPath))
            return null;

        return FullPath(string.IsNullOrWhiteSpace(options.LlmLogPath) ? "llm-logs" : options.LlmLogPath);
    }

    private static string DirectoryOf(string path) => Path.GetDirectoryName(FullPath(path)) ?? FullPath(path);

    /// <summary>
    /// Absolute form of a path, falling back to the text itself for a path the platform
    /// refuses to resolve — this is display and index arithmetic, never file access.
    /// </summary>
    private static string FullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}

/// <summary>
/// What <c>--mount</c> really does at launch. The runner builds ONE list — its own
/// auto-injected mounts first, then every <c>--mount</c> argument in order — and writes that
/// whole list as the configuration overrides <c>Orkeon:FileSystem:Mounts:{i}</c>
/// (<c>RunnerHost.ConfigureAppConfiguration</c>). Two consequences a UI must not hide: the
/// appsettings entry at index 0 is <em>always</em> masked by the auto-injected mount, and the
/// user's first <c>--mount</c> lands at index 1, replacing whatever the appsettings array held
/// there. The lists are never merged.
/// </summary>
public static class MountOverrideSemantics
{
    /// <summary>Configuration path of the mount array.</summary>
    public const string ConfigurationSection = "Orkeon:FileSystem:Mounts";

    /// <summary>Configuration path whitelisted by <c>--allow-external-mounts</c>.</summary>
    public const string ExternalMountsConfigurationSection = "PathSecurity:AdditionalAllowedDirectories";

    /// <summary>UI-ready statement of the override rule, independent of any one launch.</summary>
    public const string Explanation =
        "The runner injects its own mount first — the crew's configuration directory as " +
        "'/crew' (the script's directory as '/script' for a .ork.ts crew) — then appends each " +
        "--mount argument, and writes the whole list as 'Orkeon:FileSystem:Mounts:{index}'. So " +
        "the appsettings mount at index 0 is always replaced by the auto-injected one, the " +
        "first --mount replaces the appsettings mount at index 1, and the two lists are never " +
        "merged. Appsettings entries past the last written index stay in force. The LLM log " +
        "directory is mounted separately, hidden from agents, and shifts nothing.";

    /// <summary>UI-ready statement of what <c>--allow-external-mounts</c> adds.</summary>
    public const string ExternalMountsExplanation =
        "--allow-external-mounts additionally whitelists each --mount base path under " +
        "'PathSecurity:AdditionalAllowedDirectories', letting mounts point outside the working " +
        "directory (same effect as ORKEON_ALLOW_EXTERNAL_MOUNTS=1).";

    /// <summary><see cref="Explanation"/> resolved through a culture port (STUDIO-11).</summary>
    public static string ExplanationFor(IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);
        return strings[StudioStringKeys.MountSemanticsExplanation];
    }

    /// <summary><see cref="ExternalMountsExplanation"/> resolved through a culture port (STUDIO-11).</summary>
    public static string ExternalMountsExplanationFor(IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);
        return strings[StudioStringKeys.MountSemanticsExternalMounts];
    }

    /// <summary>
    /// <see cref="Explanation"/> followed by the mounts this particular launch injects and the
    /// index its first <c>--mount</c> will therefore occupy.
    /// </summary>
    public static string Explain(MountAutoInjection autoInjection) =>
        Explain(autoInjection, EnglishStudioStrings.Instance);

    /// <summary>Same as <see cref="Explain(MountAutoInjection)"/> through a culture port (STUDIO-11).</summary>
    public static string Explain(MountAutoInjection autoInjection, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(autoInjection);
        ArgumentNullException.ThrowIfNull(strings);

        return ExplanationFor(strings) + string.Format(
            CultureInfo.InvariantCulture,
            strings[StudioStringKeys.MountSemanticsThisLaunch],
            autoInjection.Count,
            string.Join(", ", autoInjection.Mounts),
            ConfigurationKey(autoInjection.Count));
    }

    /// <summary>Configuration key of the mount at the given index.</summary>
    public static string ConfigurationKey(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return string.Create(CultureInfo.InvariantCulture, $"{ConfigurationSection}:{index}");
    }

    /// <summary>
    /// The mount list the runtime will see for this launch: the auto-injected entries first,
    /// then the command-line ones, then whatever appsettings entries the two did not reach.
    /// </summary>
    /// <param name="commandLineMounts">The <c>--mount</c> arguments, in order.</param>
    /// <param name="settingsMounts">The appsettings <c>Orkeon:FileSystem:Mounts</c> array, in order.</param>
    /// <param name="autoInjection">
    /// The mounts the runner inserts ahead of <paramref name="commandLineMounts"/> — build it
    /// with <see cref="MountAutoInjection.For"/>. Required: without it the indices are wrong by
    /// one or two, which is exactly the mistake this type exists to prevent.
    /// </param>
    public static IReadOnlyList<EffectiveMount> ComputeEffectiveMounts(
        IReadOnlyList<string> commandLineMounts,
        IReadOnlyList<string> settingsMounts,
        MountAutoInjection autoInjection)
    {
        ArgumentNullException.ThrowIfNull(commandLineMounts);
        ArgumentNullException.ThrowIfNull(settingsMounts);
        ArgumentNullException.ThrowIfNull(autoInjection);

        var written = autoInjection.Count + commandLineMounts.Count;
        var count = Math.Max(written, settingsMounts.Count);
        var effective = new List<EffectiveMount>(count);

        for (var index = 0; index < count; index++)
        {
            var fromSettings = index < settingsMounts.Count ? settingsMounts[index] : null;

            if (index < autoInjection.Count)
                effective.Add(new EffectiveMount(index, autoInjection.Mounts[index], MountOrigin.AutoInjected, fromSettings));
            else if (index < written)
                effective.Add(new EffectiveMount(index, commandLineMounts[index - autoInjection.Count], MountOrigin.CommandLine, fromSettings));
            else
                effective.Add(new EffectiveMount(index, fromSettings!, MountOrigin.Settings));
        }

        return effective;
    }
}
