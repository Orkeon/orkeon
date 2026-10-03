using System.Text;
using Orkeon.Compliance.Vfs;
using Orkeon.Constants.Cli;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Profiles;

namespace Orkeon.Studio.Core.Teams;

/// <summary>What a team's launchers are written against besides the team's own folder (STUDIO-50).</summary>
public sealed record TeamLauncherContext
{
    /// <summary>The model settings: the team's setting is the host profile its launchers name.</summary>
    public ModelProfileSet Profiles { get; init; } = ModelProfileSet.Empty;

    /// <summary>The settings' <c>Orkeon:FileSystem:Mounts</c>, which a sidecar entry may name by id (VFS-90).</summary>
    public IReadOnlyList<string> DeclaredMounts { get; init; } = [];

    /// <summary>
    /// The settings file Studio writes its model settings into: the launchers name it with
    /// <c>--settings</c> when it exists, and a run without it resolves its own.
    /// </summary>
    public string? SettingsPath { get; init; }
}

/// <summary>One <c>--mount</c> of a team's launchers (STUDIO-50).</summary>
/// <param name="TeamFolder">
/// A folder inside the team, anchored to the launcher's own folder — the team stays relocatable,
/// as <c>forge promote</c> writes it —; null for <paramref name="Literal"/>.
/// </param>
/// <param name="Tail">What follows that folder in the mount string: <c>:/output:rw</c>, sub-path rights included.</param>
/// <param name="Literal">The mount string as a Studio launch passes it, for a folder outside the team.</param>
public sealed record TeamLaunchMount(string? TeamFolder, string Tail, string? Literal);

/// <summary>The run a team's launchers spell: every argument after <c>orkeon</c> (STUDIO-50).</summary>
public sealed record TeamLaunch
{
    /// <summary>The team's name in the launchers' header — its folder through the folder rule, as the engine writes it.</summary>
    public required string TeamName { get; init; }

    /// <summary>
    /// The team's folder, where the launchers live: what <c>run.cmd</c>'s command is measured
    /// with — beyond 8 191 characters, <c>cmd</c> runs nothing (STUDIO-51).
    /// </summary>
    public required string TeamDirectory { get; init; }

    /// <summary>Whether the crew is a script, run from <c>crew/crew.ork.ts</c>; else the YAML crew of <c>crew/</c>.</summary>
    public bool IsScript { get; init; }

    /// <summary>The <c>--settings</c> file, or null to let the run resolve its own.</summary>
    public string? SettingsPath { get; init; }

    /// <summary>The <c>--llm-profile</c> — the host profile of the team's model setting —, or null to run on the default.</summary>
    public string? LlmProfile { get; init; }

    /// <summary>The <c>--mount</c> values, in the sidecar's order.</summary>
    public IReadOnlyList<TeamLaunchMount> Mounts { get; init; } = [];

    /// <summary>The <c>--mount-id</c> values: the settings declarations the team names.</summary>
    public IReadOnlyList<string> MountIds { get; init; } = [];

    /// <summary>The brief's sample <c>--var</c> values (YAML crews only).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Variables { get; init; } = [];

    /// <summary>The brief's sample <c>--initial-context</c> (YAML crews only).</summary>
    public string? InitialContext { get; init; }

    /// <summary>What <c>orkeon run</c> targets, relative to the team folder.</summary>
    public string RunTarget => IsScript ? "crew/crew.ork.ts" : TeamLaunchers.CrewDirectoryName;
}

/// <summary>What <see cref="TeamLaunchers.Regenerate"/> did with a team's launchers (STUDIO-51).</summary>
public enum TeamLaunchersOutcome
{
    /// <summary>Written: a launcher said something else, or was not there.</summary>
    Written,

    /// <summary>Both launchers already said this: left as they were.</summary>
    Unchanged,

    /// <summary>The folder holds no promoted team, or no companion file: nothing written.</summary>
    NotATeam,

    /// <summary>The disk refused: the launchers are as they were, or half written.</summary>
    DiskRefused,
}

/// <summary>What <see cref="TeamLaunchers.Regenerate"/> did, and whether the <c>run.cmd</c> it wrote runs the team (STUDIO-51).</summary>
/// <param name="Outcome">What became of the launchers.</param>
/// <param name="WindowsRefusal">
/// When the team's <c>orkeon</c> command is longer than a <c>cmd</c> command holds: its length and its
/// longest option — the <c>run.cmd</c> written launches nothing and says why, <c>run.sh</c> is
/// complete. Null when <c>run.cmd</c> runs the team, or nothing was written.
/// </param>
public sealed record TeamLaunchersResult(TeamLaunchersOutcome Outcome, TeamLauncherCommandLength? WindowsRefusal = null);

/// <summary>
/// The launchers of an adopted team — <c>run.cmd</c> and <c>run.sh</c>, what the operating
/// system's scheduler runs (STUDIO-27) — written again from the companion file (STUDIO-50), so a
/// scheduled team runs as Studio launches it: on the team's model setting, <c>--llm-profile</c>
/// the host profile that setting is in the settings file (STUDIO-48), and with the team's folders,
/// the arguments a Studio launch passes for them (<see cref="LaunchMountPlan"/>). <c>forge
/// promote</c> writes the first ones, with only the folders inside the team; Studio writes them
/// again at adoption and whenever the team's setting or folders change, whole — an edit made by
/// hand is lost then, and the file says so. Never a key: the run reads it where the settings file
/// names it (STUDIO-49).
/// <para>
/// The text is the composer's (<see cref="TeamLauncherScript"/>, STUDIO-51), the one <c>forge
/// promote</c> writes with: the frame, the header <c>forge rename</c> finds the team's name in,
/// each value as <c>cmd</c> and the runner read it back. This class says what to launch — the
/// option names are the runner's (<see cref="RunOptionNames"/>) — and writes the files;
/// <c>TeamLaunchersTests</c> reads them through models of <c>cmd</c> and of the C runtime, a real
/// shell, then the runner's own grammar.
/// </para>
/// <para>
/// A registration the operating system holds names the launcher by its path (the Windows task
/// through <c>cmd.exe /d /v:off /s /c</c>, the unit's <c>ExecStart</c>, the cron line), never its
/// arguments: a launcher written again is what the next scheduled run executes, and nothing has to
/// be reinstalled.
/// </para>
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; a team's launchers are files of the user's " +
    "team folder on the physical disk, written before — and outside — any VFS mount.")]
public static class TeamLaunchers
{
    /// <summary>The launcher on Windows — the engine's name for it.</summary>
    public const string WindowsLauncherName = ConventionalNames.WindowsTeamLauncher;

    /// <summary>The launcher everywhere else.</summary>
    public const string PosixLauncherName = ConventionalNames.PosixTeamLauncher;

    /// <summary>The promoted crew's folder inside the team.</summary>
    internal const string CrewDirectoryName = "crew";

    /// <summary>
    /// Whether <paramref name="teamDirectory"/> holds a promoted team whose launchers Studio
    /// writes: a <c>crew/</c> folder and at least one launcher. A team built by hand keeps its own
    /// files, and nothing is created where no launcher was.
    /// </summary>
    public static bool IsPromotedTeam(string teamDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        return Directory.Exists(Path.Combine(teamDirectory, CrewDirectoryName))
            && (File.Exists(Path.Combine(teamDirectory, PosixLauncherName))
                || File.Exists(Path.Combine(teamDirectory, WindowsLauncherName)));
    }

    /// <summary>
    /// The run <paramref name="teamDirectory"/>'s launchers spell, read from its companion file and
    /// its <c>forge.json</c>, or null when the folder holds no promoted team or no companion file.
    /// </summary>
    /// <param name="teamDirectory">The team folder.</param>
    /// <param name="context">The model settings, the settings' mounts and the settings file.</param>
    public static TeamLaunch? Describe(string teamDirectory, TeamLauncherContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);
        ArgumentNullException.ThrowIfNull(context);

        if (!IsPromotedTeam(teamDirectory))
            return null;

        // The companion file is what the launchers are written from: a folder without one keeps
        // the launchers the engine wrote.
        var team = TeamCatalog.Describe(teamDirectory, context.DeclaredMounts);
        if (team.Metadata is null)
            return null;

        var record = ForgeSessionCatalog.ReadTeamLaunchRecord(teamDirectory);
        var isScript = record.Format is not null
            ? record.IsScript
            : File.Exists(Path.Combine(teamDirectory, CrewDirectoryName, "crew.ork.ts"));

        // The setting Studio launches the team on, when it is a host profile: a setting renamed or
        // removed since, or one offered to no crew (« no model », a name without a letter), runs on
        // the default — as a Studio launch of a team naming no setting it knows does.
        var llmProfile = team.Profile is { Length: > 0 } setting
            && HostLlmProfiles.Offered(context.Profiles).TryGetValue(setting, out var id)
                ? id
                : null;

        // The same plan a Studio launch passes: a settings declaration by its id, the team's own
        // folders and its copies as --mount. An id this machine does not declare goes too: the
        // runner refuses it at start, as Studio refuses to launch the team.
        var plan = LaunchMountPlan.For(team.ResolvedMounts);
        var mounts = team.ResolvedMounts
            .Where(mount => mount.Source is not (TeamMountSource.Settings or TeamMountSource.UnknownId))
            .Select(mount => mount.Source == TeamMountSource.InsideTeam && Anchored(teamDirectory, mount.Effective) is { } anchored
                ? anchored
                : new TeamLaunchMount(null, "", mount.Effective))
            .ToList();

        return new TeamLaunch
        {
            TeamName = FolderSlug.From(Path.GetFileName(Path.TrimEndingDirectorySeparator(teamDirectory))) ?? FolderSlug.TeamFallback,
            TeamDirectory = Path.GetFullPath(teamDirectory),
            IsScript = isScript,
            SettingsPath = context.SettingsPath is { Length: > 0 } settings && File.Exists(settings) ? Path.GetFullPath(settings) : null,
            LlmProfile = llmProfile,
            Mounts = mounts,
            MountIds = [.. plan.MountIds, .. plan.UnknownIds.Except(plan.MountIds, StringComparer.Ordinal)],
            Variables = isScript ? [] : record.SampleVariables,
            InitialContext = isScript || string.IsNullOrWhiteSpace(record.SampleInitialContext) ? null : record.SampleInitialContext,
        };
    }

    /// <summary>
    /// Writes <paramref name="teamDirectory"/>'s two launchers again from its companion file — a file
    /// already saying the same thing is left as it is —, in UTF-8 without a BOM, which <c>cmd</c>
    /// would read as text. Tolerant, like the rest of the teams folder: the result says when the
    /// folder holds no promoted team or the disk refused, and when the <c>run.cmd</c> written
    /// launches nothing because the team's command is longer than <c>cmd</c> holds (STUDIO-51).
    /// </summary>
    /// <param name="teamDirectory">The team folder.</param>
    /// <param name="context">The model settings, the settings' mounts and the settings file.</param>
    public static TeamLaunchersResult Regenerate(string teamDirectory, TeamLauncherContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            if (Describe(teamDirectory, context) is not { } launch)
                return new TeamLaunchersResult(TeamLaunchersOutcome.NotATeam);

            var spec = Spec(launch);
            var posix = Path.Combine(teamDirectory, PosixLauncherName);
            var written = WriteIfChanged(posix, TeamLauncherScript.Posix(spec));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(posix,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            written |= WriteIfChanged(Path.Combine(teamDirectory, WindowsLauncherName), TeamLauncherScript.Windows(spec));
            var command = TeamLauncherScript.MeasureWindows(spec);
            return new TeamLaunchersResult(
                written ? TeamLaunchersOutcome.Written : TeamLaunchersOutcome.Unchanged,
                command.Fits ? null : command);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new TeamLaunchersResult(TeamLaunchersOutcome.DiskRefused);
        }
    }

    /// <summary><c>run.sh</c>'s text for <paramref name="launch"/>.</summary>
    public static string ComposePosix(TeamLaunch launch) => TeamLauncherScript.Posix(Spec(launch));

    /// <summary><c>run.cmd</c>'s text for <paramref name="launch"/>.</summary>
    public static string ComposeWindows(TeamLaunch launch) => TeamLauncherScript.Windows(Spec(launch));

    /// <summary>What the composer writes for <paramref name="launch"/>: the launchers Studio writes, which say it.</summary>
    private static TeamLauncherSpec Spec(TeamLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);

        return new TeamLauncherSpec
        {
            TeamName = launch.TeamName,
            TeamDirectory = launch.TeamDirectory,
            WrittenByStudio = true,
            IsScript = launch.IsScript,
            Segments = Segments(launch),
        };
    }

    /// <summary>
    /// The <c>orkeon run</c> invocation both launchers spell, one segment per option: the crew and
    /// its settings, the model, the folders, then the sample inputs. Several values go after ONE
    /// flag — the runner refuses a repeated option —; a single-value option is written
    /// <c>--option=value</c>, so a value starting with <c>-</c> stays its value (STUDIO-51).
    /// </summary>
    private static List<IReadOnlyList<TeamLauncherArgument>> Segments(TeamLaunch launch)
    {
        var segments = new List<IReadOnlyList<TeamLauncherArgument>>
        {
            new[] { TeamLauncherArgument.Word(RunArgumentsBuilder.RunVerb), TeamLauncherArgument.InTeam("", launch.RunTarget, "") },
        };

        if (launch.SettingsPath is { } settings)
            segments.Add([TeamLauncherArgument.Assign(RunOptionNames.Flag(RunOptionNames.Settings), settings)]);

        if (launch.LlmProfile is { } profile)
            segments.Add([TeamLauncherArgument.Assign(RunOptionNames.Flag(RunOptionNames.LlmProfile), profile)]);

        // A folder inside the team between the mount grammar's quotes, anchored to the launcher's
        // folder — a path nobody controls when the launcher is written, a C:\ one on Windows —; any
        // other mount as a Studio launch passes it.
        if (launch.Mounts.Count > 0)
        {
            segments.Add(
            [
                TeamLauncherArgument.Word(RunOptionNames.Flag(RunOptionNames.Mount)),
                .. launch.Mounts.Select(mount => mount.TeamFolder is { } folder
                    ? TeamLauncherArgument.InTeam("\"", folder, "\"" + mount.Tail)
                    : TeamLauncherArgument.Literal(mount.Literal!)),
            ]);
        }

        if (launch.MountIds.Count > 0)
        {
            segments.Add(
            [
                TeamLauncherArgument.Word(RunOptionNames.Flag(RunOptionNames.MountId)),
                .. launch.MountIds.Select(TeamLauncherArgument.Literal),
            ]);
        }

        if (launch.IsScript)
            return segments;

        if (launch.Variables.Count > 0)
        {
            segments.Add(
            [
                TeamLauncherArgument.Word(RunOptionNames.Flag(RunOptionNames.Var)),
                .. launch.Variables.Select(pair => TeamLauncherArgument.Literal($"{pair.Key}={pair.Value}")),
            ]);
        }

        if (launch.InitialContext is { } initialContext)
            segments.Add([TeamLauncherArgument.Assign(RunOptionNames.Flag(RunOptionNames.InitialContext), initialContext)]);

        return segments;
    }

    /// <summary>
    /// A folder inside the team as a mount anchored to the launcher's folder — or null when the
    /// entry does not read as one, and then it goes as recorded.
    /// </summary>
    private static TeamLaunchMount? Anchored(string teamDirectory, string mountString)
    {
        if (!TeamMountPaths.TryGetRelativeFolder(TeamMountPaths.Relativize(teamDirectory, mountString), out var folder))
            return null;

        try
        {
            var mount = FileSystemMount.Parse(mountString);
            var tail = new StringBuilder()
                .Append(':').Append(FileSystemMount.Quote(mount.VirtualPath))
                .Append(':').Append(FileSystemMount.FormatRights(mount.DefaultRights));
            foreach (var item in mount.Overrides)
                tail.Append(';').Append(FileSystemMount.Quote(item.RelativePath)).Append(':').Append(FileSystemMount.FormatRights(item.Rights));

            return new TeamLaunchMount(folder, tail.ToString(), null);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Writes <paramref name="text"/> unless the file already says it; whether it wrote.</summary>
    private static bool WriteIfChanged(string path, string text)
    {
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), text, StringComparison.Ordinal))
            return false;

        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }
}
