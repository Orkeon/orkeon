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
/// The frame is the engine's own (<c>ForgePromoter</c>): the header <c>forge rename</c> finds the
/// team's name in, the symlinks resolved, the working directory set to the team's folder, the
/// sample inputs of the brief. The option names are the runner's (<see cref="RunOptionNames"/>), and
/// <c>TeamLaunchersTests</c> parses what the shell hands the runner with its own grammar.
/// </para>
/// <para>
/// A registration the operating system holds names the launcher by its path (the task's
/// <c>Command</c>, the unit's <c>ExecStart</c>, the cron line), never its arguments: a launcher
/// written again is what the next scheduled run executes, and nothing has to be reinstalled.
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

    /// <summary>The second line of every launcher Studio writes, after the header.</summary>
    internal const string StudioNote =
        "Orkeon Studio writes this file again from studio-team.json whenever the team's model setting "
        + "or folders change: an edit made here is lost then.";

    /// <summary>
    /// The launchers' header after each shell's comment marker — the engine's spelling
    /// (<c>ForgePromoter.LauncherHeader</c>), which <c>forge rename</c> finds the team's name in
    /// (STUDIO-28): the same words, or a renamed team's launchers would keep its former name.
    /// </summary>
    internal static string Header(string teamName) => $"Generated by Orkeon Forge for the team '{teamName}'.";

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
    /// already saying the same thing is left as it is. Tolerant, like the rest of the teams folder:
    /// false when the folder holds no promoted team or the disk refused.
    /// </summary>
    /// <param name="teamDirectory">The team folder.</param>
    /// <param name="context">The model settings, the settings' mounts and the settings file.</param>
    public static bool Regenerate(string teamDirectory, TeamLauncherContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            if (Describe(teamDirectory, context) is not { } launch)
                return false;

            var posix = Path.Combine(teamDirectory, PosixLauncherName);
            WriteIfChanged(posix, ComposePosix(launch));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(posix,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            WriteIfChanged(Path.Combine(teamDirectory, WindowsLauncherName), ComposeWindows(launch));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary><c>run.sh</c>'s text for <paramref name="launch"/>.</summary>
    public static string ComposePosix(TeamLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);

        var builder = new StringBuilder();
        builder.Append("#!/usr/bin/env sh\n");
        builder.Append("# ").Append(Header(launch.TeamName)).Append('\n');
        builder.Append("# ").Append(StudioNote).Append('\n');
        builder.Append(launch.IsScript
            ? "# The crew lives in crew/crew.ork.ts — edit it there, this script only launches it.\n"
            : "# The --var/--initial-context lines below are the test sample.\n");
        // Symlinks resolved and the team's folder made the working directory, as the engine's
        // launcher does it: a launcher linked onto PATH, or started by a scheduler in the system
        // directory, still runs the team from its own folder.
        builder.Append("SELF=\"$0\"\n");
        builder.Append("while [ -L \"$SELF\" ]; do\n");
        builder.Append("  LINK=\"$(readlink \"$SELF\")\"\n");
        builder.Append("  case \"$LINK\" in\n");
        builder.Append("    /*) SELF=\"$LINK\" ;;\n");
        builder.Append("    *) SELF=\"$(dirname \"$SELF\")/$LINK\" ;;\n");
        builder.Append("  esac\n");
        builder.Append("done\n");
        builder.Append("DIR=\"$(cd \"$(dirname \"$SELF\")\" && pwd)\" || exit 1\n");
        builder.Append("cd \"$DIR\" || exit 1\n");
        builder.Append("exec orkeon");
        foreach (var segment in Segments(launch, posix: true))
            builder.Append(" \\\n  ").Append(segment);

        return builder.Append('\n').ToString();
    }

    /// <summary><c>run.cmd</c>'s text for <paramref name="launch"/>.</summary>
    public static string ComposeWindows(TeamLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);

        var builder = new StringBuilder();
        builder.Append("@echo off\r\n");
        builder.Append("rem ").Append(Header(launch.TeamName)).Append("\r\n");
        builder.Append("rem ").Append(StudioNote).Append("\r\n");
        builder.Append(launch.IsScript
            ? "rem The crew lives in crew\\crew.ork.ts — edit it there, this script only launches it.\r\n"
            : "rem The --var/--initial-context values below are the test sample.\r\n");
        builder.Append("cd /d \"%~dp0\" || exit /b 1\r\n");
        builder.Append("orkeon");
        foreach (var segment in Segments(launch, posix: false))
            builder.Append(' ').Append(segment);

        return builder.Append("\r\n").ToString();
    }

    /// <summary>
    /// The <c>orkeon run</c> invocation both launchers spell, one segment per option: the crew and
    /// its settings, the model, the folders, then the sample inputs. Several values go after ONE
    /// flag — the runner refuses a repeated option.
    /// </summary>
    private static IEnumerable<string> Segments(TeamLaunch launch, bool posix)
    {
        yield return $"run {AnchoredToken(launch.RunTarget, posix)}";

        if (launch.SettingsPath is { } settings)
            yield return $"{RunOptionNames.Flag(RunOptionNames.Settings)} {Literal(settings, posix)}";

        if (launch.LlmProfile is { } profile)
            yield return $"{RunOptionNames.Flag(RunOptionNames.LlmProfile)} {Literal(profile, posix)}";

        if (launch.Mounts.Count > 0)
            yield return $"{RunOptionNames.Flag(RunOptionNames.Mount)} {string.Join(' ', launch.Mounts.Select(mount => MountToken(mount, posix)))}";

        if (launch.MountIds.Count > 0)
            yield return $"{RunOptionNames.Flag(RunOptionNames.MountId)} {string.Join(' ', launch.MountIds.Select(id => Literal(id, posix)))}";

        if (launch.IsScript)
            yield break;

        if (launch.Variables.Count > 0)
            yield return $"{RunOptionNames.Flag(RunOptionNames.Var)} {string.Join(' ', launch.Variables.Select(pair => Literal($"{pair.Key}={pair.Value}", posix)))}";

        if (launch.InitialContext is { } initialContext)
            yield return $"{RunOptionNames.Flag(RunOptionNames.InitialContext)} {Literal(initialContext, posix)}";
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

    /// <summary>
    /// One shell token carrying a whole mount: a folder inside the team behind the launcher's own
    /// folder, between the mount grammar's quotes (\" survives both shells as a literal '"') — the
    /// anchor expands to a path nobody controls when the launcher is written, a <c>C:\</c> one on
    /// Windows —; any other mount quoted literally.
    /// </summary>
    private static string MountToken(TeamLaunchMount mount, bool posix)
    {
        if (mount.TeamFolder is not { } folder)
            return Literal(mount.Literal!, posix);

        return posix
            ? $"\"\\\"$DIR/{ShDoubleQuoted(folder)}\\\"{ShDoubleQuoted(mount.Tail)}\""
            : $"\"\\\"%~dp0{CmdDoubleQuoted(folder)}\\\"{CmdDoubleQuoted(mount.Tail)}\"";
    }

    /// <summary>A path inside the team, anchored to the launcher's folder; double-quoted so the shell expands the anchor.</summary>
    private static string AnchoredToken(string relative, bool posix) =>
        posix ? $"\"$DIR/{relative}\"" : $"\"%~dp0{relative}\"";

    /// <summary>A literal value, quoted the way the target shell reads it back.</summary>
    private static string Literal(string value, bool posix) =>
        posix ? $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'" : CmdQuoted(value);

    /// <summary>
    /// A literal for <c>cmd</c>: double quotes, a quote and the backslashes before it escaped the way
    /// the runner's command-line parser reads them back, and every <c>%</c> doubled — a batch file
    /// expands what it reads between two of them, quotes or not.
    /// </summary>
    private static string CmdQuoted(string value)
    {
        var builder = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1).Append('"');
            }
            else
            {
                builder.Append('\\', backslashes).Append(character);
                if (character == '%')
                    builder.Append('%');
            }

            backslashes = 0;
        }

        // Trailing backslashes must not escape the closing quote.
        return builder.Append('\\', backslashes * 2).Append('"').ToString();
    }

    /// <summary>Text inside a POSIX double-quoted token: what the shell would expand or end the token on, escaped.</summary>
    private static string ShDoubleQuoted(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character is '\\' or '"' or '$' or '`')
                builder.Append('\\');
            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>Text inside a <c>cmd</c> token: a quote escaped for the runner's parser, a <c>%</c> doubled.</summary>
    private static string CmdDoubleQuoted(string text) =>
        text.Replace("%", "%%", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static void WriteIfChanged(string path, string text)
    {
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), text, StringComparison.Ordinal))
            return;

        File.WriteAllText(path, text);
    }
}
