namespace Orkeon.Constants.FileSystem;

/// <summary>
/// File and folder names Orkeon relies on by convention rather than by configuration.
/// <para>
/// Each is written by one component and looked for by another, across projects that cannot
/// reference each other (ADR-009). Nothing validates them: a settings file the tooling writes
/// under one name and the runner looks for under another simply does not load, and the run
/// proceeds on defaults without a word.
/// </para>
/// </summary>
public static class ConventionalNames
{
    /// <summary>
    /// The settings file, wherever it sits: beside a crew, in the per-user configuration
    /// directory, or next to the daemon. Written by <c>orkeon init</c>
    /// and resolved by every runner.
    /// </summary>
    public const string SettingsFile = "appsettings.json";

    /// <summary>
    /// The state directory a workspace accumulates — forge sessions, the codebase index cache.
    /// Hidden by convention, and never a mount root: it holds Orkeon's own bookkeeping, not the
    /// agents' material.
    /// </summary>
    public const string StateDirectory = ".orkeon";

    /// <summary>
    /// The crew settings file of a multi-file YAML crew directory: <c>name</c>, <c>goal</c>,
    /// <c>process</c>, <c>links</c>, <c>mounts</c>. Read by the YAML loader through the VFS and,
    /// before any host exists, by the runners' mount pre-read (VFS-90) — two readers that must
    /// agree on the name or the pre-read silently sees no <c>mounts:</c> block.
    /// </summary>
    public const string CrewSettingsFile = "config.yaml";

    /// <summary>
    /// The name accepted in place of <see cref="CrewSettingsFile"/> when a crew directory has
    /// none: the flat layout's crew file doubles as the settings file.
    /// </summary>
    public const string CrewSettingsFallbackFile = "crew.yaml";

    /// <summary>
    /// The sidecar at the root of a team folder: the team's name, its description,
    /// its model profile, its schedule and its mounts. Written by <c>orkeon usecases export</c>;
    /// <c>orkeon forge rename</c> renames the team in it and leaves every other field alone. Read by
    /// clients that list teams: a sidecar written under another name is a team without a name, a
    /// description or its folders.
    /// </summary>
    public const string TeamSidecarFile = "team.json";

    /// <summary>
    /// A promoted team's launcher on Windows, at the root of its folder — what its scheduled task
    /// runs. Written by <c>orkeon forge promote</c>, and written again by clients for the teams
    /// they adopt, from their sidecar: two writers, and a launcher written under another
    /// name is one the operating system never runs.
    /// </summary>
    public const string WindowsTeamLauncher = "run.cmd";

    /// <summary>
    /// The same launcher everywhere else — what the systemd user unit and the cron line run. Same
    /// two writers as <see cref="WindowsTeamLauncher"/>.
    /// </summary>
    public const string PosixTeamLauncher = "run.sh";

    /// <summary>
    /// A promoted team's schedule folder, at the root of its folder: the artifacts of the three
    /// scheduler families and what this machine's scheduler registered — what the operating
    /// system of one machine knows of the team. Written by <c>orkeon forge promote</c> and
    /// <c>forge schedule</c>; an export or import of a team leaves it behind:
    /// <c>forge schedule</c> writes it again on the machine that installs.
    /// </summary>
    public const string ScheduleDirectory = "schedule";

    /// <summary>
    /// The record of what <c>forge schedule</c> registered, inside <see cref="ScheduleDirectory"/>:
    /// the schedule, the system family, the names, the folder and the date. Written by
    /// the CLI, which acts on these names alone; clients read whether it is there — a team
    /// whose registration the engine must remove before the folder goes.
    /// </summary>
    public const string ScheduleInstallationFile = "installed.json";

    /// <summary>
    /// The flat YAML crew layout: a directory holding these three files is a crew, as opposed to
    /// the multi-file layout with its <c>agents/</c> and <c>tasks/</c> sub-folders.
    /// <para>
    /// Exposed as the SET, not as three names. The detector and the layout classifier live in
    /// projects that cannot reference each other and each used to carry its own copy of the
    /// array; a fourth marker added to one and not the other would have gone unnoticed, which is
    /// the failure mode a set makes impossible rather than merely unlikely.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> FlatCrewLayoutFiles { get; } = ["crew.yaml", "agents.yaml", "tasks.yaml"];
}
