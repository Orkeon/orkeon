using Orkeon.Constants.Configuration;
using Orkeon.Constants.FileSystem;
using System.Globalization;
using Orkeon.Compliance.Vfs;
using Orkeon.Domain.FileSystem;
using Orkeon.Studio.Core.FileSystem;
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

/// <summary>How a settings entry fares when several declare one virtual root (VFS-90).</summary>
public enum EffectiveMountSelection
{
    /// <summary>The only entry of its root, or a launch mount: in force as it is.</summary>
    InForce,

    /// <summary>Kept because a <c>--mount-id</c> names it; the other entries of its root are not mounted.</summary>
    SelectedById,

    /// <summary>Not mounted for this run: another entry of its root was selected, or a <c>--mount</c> took the root.</summary>
    NotSelected,

    /// <summary>One of several entries of its root and nothing selects one: the runner refuses the launch (D-04) unless the crew's <c>mounts:</c> block does.</summary>
    Conflict,
}

/// <summary>One entry of the mount list the runtime will actually see.</summary>
/// <param name="Index">Position in <c>Orkeon:FileSystem:Mounts</c>.</param>
/// <param name="Value">The mount string in force.</param>
/// <param name="Origin">Which of the three sources won.</param>
/// <param name="ReplacedSettingsMount">
/// The appsettings entry this takes the place of, when the settings declared the same
/// virtual root: the runner writes a <c>--mount</c> at that entry's own index.
/// </param>
public sealed record EffectiveMount(
    int Index,
    string Value,
    MountOrigin Origin,
    string? ReplacedSettingsMount = null)
{
    /// <summary>Configuration key this entry occupies.</summary>
    public string ConfigurationKey => MountOverrideSemantics.ConfigurationKey(Index);

    /// <summary>True when a settings entry on the same root was replaced by a higher-priority one.</summary>
    public bool OverridesSettings => Origin != MountOrigin.Settings && ReplacedSettingsMount is not null;

    /// <summary>How the entry fares among the entries of its root (VFS-90).</summary>
    public EffectiveMountSelection Selection { get; init; } = EffectiveMountSelection.InForce;

    /// <summary>How many settings entries declare this entry's root; 1 for an ordinary root.</summary>
    public int SharedRootCount { get; init; } = 1;

    /// <summary>True when the entry is mounted for the run.</summary>
    public bool IsMounted => Selection is EffectiveMountSelection.InForce or EffectiveMountSelection.SelectedById;
}

/// <summary>
/// The mounts the runner inserts <em>ahead of</em> every <c>--mount</c> argument, in the order
/// it inserts them. There is exactly one: the crew's configuration directory as <c>/crew:ro</c>
/// (<c>RunnerExecution.TryBuildHost</c>), or the script's directory as <c>/script:ro</c> on the
/// scripting path (<c>RunCommand</c>). Its root is reserved — the settings can never declare
/// it — so it is always appended after the declared entries, and every <c>--mount</c> on a
/// new root lands after it.
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
    /// The roots the engine mounts for itself: a user <c>--mount</c> claiming one is refused at
    /// launch, so Studio refuses it in the editor rather than building a command that fails.
    /// <para>
    /// Read from the satellite (ADR-009), not copied. The four used to be declared here again
    /// and pinned to the engine's by a drift test asserting pairwise equality — which cannot
    /// catch an omission: <c>/sandbox</c> was missing for as long as the test was green.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> ReservedVirtualRoots { get; } = RunnerVirtualRoots.All;

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
            ? new[] { $"{FileSystemMount.Quote(logDirectory)}:{RunnerVirtualRoots.LlmLogs}:rw" }
            : [];

        return new MountAutoInjection { Mounts = mounts, InternalMounts = internalMounts };
    }

    /// <summary>
    /// The runner's own mount for the target: the crew's directory as <c>/crew</c>, a
    /// script's directory as <c>/script</c>. Both are names, never the folder's own path —
    /// mirrors <c>RunnerVirtualRoots</c> on the engine side, pinned by a drift test.
    /// </summary>
    private static string DescribeTargetMount(RunTarget target)
    {
        if (target.Dialect == RunTargetDialect.Script)
            return $"{FileSystemMount.Quote(DirectoryOf(target.RunPath))}:{RunnerVirtualRoots.Script}:ro";

        var configDirectory = target.Kind == RunTargetKind.MultiFileCrewDirectory
            ? Path.TrimEndingDirectorySeparator(FullPath(target.RunPath))
            : DirectoryOf(target.RunPath);

        // Quoted the way the runner quotes it: Studio predicts the CLI's own command line,
        // and a prediction that spells a path differently from the thing it predicts is not
        // a prediction. The drift test pins the pair.
        return $"{FileSystemMount.Quote(configDirectory)}:{RunnerVirtualRoots.Crew}:ro";
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
/// auto-injected mount first, then every <c>--mount</c> argument in order — and places each
/// entry of it in <c>Orkeon:FileSystem:Mounts</c> <em>by virtual root</em>
/// (<c>RunnerHost.ConfigureAppConfiguration</c>, STUDIO-15 D-01): on a root the appsettings
/// already declare, the entry is written at that declaration's index and replaces it for the
/// run; on a new root it is appended after the highest declared index. Settings entries no
/// launch mount names stay in force. Two consequences a UI must not hide: a team folder the
/// settings already declare under the same name is not a collision, it is the same mount
/// twice; and the same root bound to another folder is a replacement the table has to show.
/// </summary>
public static class MountOverrideSemantics
{
    /// <summary>Configuration path of the mount array.</summary>
    public const string ConfigurationSection = ConfigurationKeys.FileSystemMounts;

    /// <summary>Configuration path whitelisted by <c>--allow-external-mounts</c>.</summary>
    public const string ExternalMountsConfigurationSection = "PathSecurity:AdditionalAllowedDirectories";

    /// <summary>UI-ready statement of the override rule, independent of any one launch.</summary>
    public const string Explanation =
        "The runner inserts its own mount first — the crew's configuration directory as " +
        "'/crew' (the script's directory as '/script' for a .ork.ts crew) — then places each " +
        "--mount argument by its virtual root in 'Orkeon:FileSystem:Mounts': on a root the " +
        "appsettings already declare, the --mount replaces every settings entry of that root for " +
        "this run; on a new root, it is appended after every declared entry. Several settings " +
        "entries may declare one root when each carries an id: a --mount-id (or the crew's " +
        "mounts: block) keeps one and the others are not mounted for the run. Settings entries " +
        "no --mount names stay in force. The LLM log directory is mounted separately, hidden " +
        "from agents, and shifts nothing.";

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
    /// <see cref="Explanation"/> followed by the mounts this particular launch injects — the
    /// ones every <c>--mount</c> on a new root lands after.
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
            string.Join(", ", autoInjection.Mounts));
    }

    /// <summary>Configuration key of the mount at the given index.</summary>
    public static string ConfigurationKey(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return string.Create(CultureInfo.InvariantCulture, $"{ConfigurationSection}:{index}");
    }

    /// <summary>
    /// The mount list the runtime will see for this launch, in the order of the final array:
    /// the appsettings entries at their own indices — each replaced in place by the launch
    /// mount that claims the same virtual root, if any — then the launch mounts on new roots,
    /// appended in the order the runner writes them (the auto-injected one, then every
    /// <c>--mount</c>). Mirrors <c>RunnerHost.ConfigureAppConfiguration</c> entry for entry.
    /// </summary>
    /// <param name="commandLineMounts">The <c>--mount</c> arguments, in order.</param>
    /// <param name="settingsMounts">The appsettings <c>Orkeon:FileSystem:Mounts</c> array, in order.</param>
    /// <param name="autoInjection">
    /// The mounts the runner inserts ahead of <paramref name="commandLineMounts"/> — build it
    /// with <see cref="MountAutoInjection.For"/>. Required: it is the first entry appended
    /// after the declared ones, so without it every appended index is wrong by one.
    /// </param>
    /// <param name="mountIds">
    /// The <c>--mount-id</c> arguments (VFS-90): among several settings entries of one root,
    /// the one carrying a named id is kept and the others are not mounted; with none, and no
    /// <c>--mount</c> on the root, every entry of that root is a <see cref="EffectiveMountSelection.Conflict"/>.
    /// </param>
    public static IReadOnlyList<EffectiveMount> ComputeEffectiveMounts(
        IReadOnlyList<string> commandLineMounts,
        IReadOnlyList<string> settingsMounts,
        MountAutoInjection autoInjection,
        IReadOnlyList<string>? mountIds = null)
    {
        ArgumentNullException.ThrowIfNull(commandLineMounts);
        ArgumentNullException.ThrowIfNull(settingsMounts);
        ArgumentNullException.ThrowIfNull(autoInjection);

        var effective = new List<EffectiveMount>(settingsMounts.Count + autoInjection.Count + commandLineMounts.Count);
        var indexByRoot = new Dictionary<string, int>(StringComparer.Ordinal);
        var indicesByRoot = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var index = 0; index < settingsMounts.Count; index++)
        {
            effective.Add(new EffectiveMount(index, settingsMounts[index], MountOrigin.Settings));

            // The first declaration of a root is the one a launch mount replaces — the same
            // choice the runner makes; the others of that root are withdrawn for the run.
            if (TryGetVirtualRoot(settingsMounts[index]) is { } root)
            {
                indexByRoot.TryAdd(root, index);
                if (!indicesByRoot.TryGetValue(root, out var indices))
                    indicesByRoot[root] = indices = [];
                indices.Add(index);
            }
        }

        var launchMounts = autoInjection.Mounts
            .Select(mount => (Value: mount, Origin: MountOrigin.AutoInjected))
            .Concat(commandLineMounts.Select(mount => (Value: mount, Origin: MountOrigin.CommandLine)));

        foreach (var (value, origin) in launchMounts)
        {
            if (TryGetVirtualRoot(value) is { } root && indexByRoot.TryGetValue(root, out var index))
            {
                // Written at the declared entry's own key. A second launch mount on the same
                // root overwrites that key again (the runner's last write wins), and the row
                // keeps naming the settings entry the root was taken from.
                var replaced = effective[index].Origin == MountOrigin.Settings
                    ? effective[index].Value
                    : effective[index].ReplacedSettingsMount;
                effective[index] = new EffectiveMount(index, value, origin, replaced);
                continue;
            }

            effective.Add(new EffectiveMount(effective.Count, value, origin));
        }

        ApplySelection(effective, indicesByRoot, mountIds ?? []);
        return effective;
    }

    /// <summary>
    /// Mirrors <c>MountSelection.Resolve</c> on the engine side, for the rows the table
    /// shows: a root several settings entries declare ends with one of them in force — the
    /// first, overwritten by a <c>--mount</c>; the one a <c>--mount-id</c> names; or none, and
    /// the runner refuses unless the crew's own <c>mounts:</c> block picks (which Studio
    /// cannot see from here, so the rows read as a conflict rather than as a certainty).
    /// </summary>
    private static void ApplySelection(
        List<EffectiveMount> effective,
        Dictionary<string, List<int>> indicesByRoot,
        IReadOnlyList<string> mountIds)
    {
        var selectedIds = new HashSet<string>(
            mountIds.Select(id => Orkeon.Domain.Common.MountId.TryParse(id, out var parsed) ? parsed.ToString() : id),
            StringComparer.OrdinalIgnoreCase);

        foreach (var (_, indices) in indicesByRoot)
        {
            if (indices.Count < 2)
                continue;

            var first = indices[0];
            if (effective[first].Origin != MountOrigin.Settings)
            {
                // A --mount took the root: the first entry was overwritten, the others withdrawn.
                foreach (var index in indices.Skip(1))
                    effective[index] = effective[index] with { Selection = EffectiveMountSelection.NotSelected, SharedRootCount = indices.Count };
                effective[first] = effective[first] with { SharedRootCount = indices.Count };
                continue;
            }

            var selected = indices.FirstOrDefault(
                index => IdOf(effective[index].Value) is { } id && selectedIds.Contains(id), -1);
            foreach (var index in indices)
            {
                EffectiveMountSelection selection;
                if (selected < 0)
                    selection = EffectiveMountSelection.Conflict;
                else if (index == selected)
                    selection = EffectiveMountSelection.SelectedById;
                else
                    selection = EffectiveMountSelection.NotSelected;
                effective[index] = effective[index] with { Selection = selection, SharedRootCount = indices.Count };
            }
        }
    }

    private static string? IdOf(string mountString) => FileSystemMount.TryGetId(mountString)?.ToString();

    /// <summary>
    /// The virtual root a mount string claims, without its trailing slash, or null for a
    /// string the parser refuses — which the runner then reports with its own message, and
    /// which this prediction files as an appended entry, the way the runner writes it.
    /// </summary>
    private static string? TryGetVirtualRoot(string mountString)
    {
        if (!MountDefinition.TryParse(mountString, out var mount, out _) || mount is null)
            return null;

        return mount.VirtualPath.Length > 1 ? mount.VirtualPath.TrimEnd('/') : mount.VirtualPath;
    }
}
