using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Run.Launcher;

/// <summary>
/// Everything the launcher screen renders, and nothing it draws.
/// <para>
/// It composes the three pieces of a launch — which crew
/// (<see cref="TargetSelectionModel"/>), with which options
/// (<see cref="LaunchOptionsModel"/>), spawned how (<see cref="RunSession"/>) — and answers
/// the questions the screen asks between them: which command line this adds up to, what a
/// dry run would say, which mounts the appsettings file already declares, and what a replay
/// from the recent list would run.
/// </para>
/// </summary>
internal sealed class RunLauncherViewModel
{
    private readonly IAppSettingsReader _settingsReader;
    private readonly ITargetProbe _targetProbe;
    private readonly IDirectoryProbe _directories;

    /// <summary>
    /// Creates the launcher over explicit collaborators (the test seam). <paramref name="teamsRoot"/>
    /// is the teams root a real run stamps its team's last run under (STUDIO-31, D-05; STUDIO-61)
    /// and the root a team's own settings file is anchored on (STUDIO-62); null stamps nothing and
    /// finds no team file. <paramref name="targetProbe"/> is the disk the team file is looked up on
    /// — the detector's, in production; the real disk when absent.
    /// </summary>
    public RunLauncherViewModel(
        RunTargetDetector detector,
        OrkeonProcessRunner runner,
        ILaunchHistoryStore? history = null,
        MountValidator? mountValidator = null,
        IAppSettingsReader? settingsReader = null,
        TeamsRootResolution? teamsRoot = null,
        ITargetProbe? targetProbe = null)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(runner);

        Target = new TargetSelectionModel(detector);
        Options = new LaunchOptionsModel(mountValidator);
        TeamsRoot = teamsRoot;
        Session = new RunSession(runner, history, teamsRoot?.Path);
        _settingsReader = settingsReader ?? PhysicalAppSettingsReader.Instance;
        _targetProbe = targetProbe ?? PhysicalTargetProbe.Instance;
        _directories = new TargetProbeDirectories(_targetProbe);
    }

    /// <summary>
    /// Creates the launcher over the real machine: real disk, real processes, real history file —
    /// and the teams root the <c>ORKEON_STUDIO_TEAMS_ROOT</c> variable names (STUDIO-61), read
    /// through <paramref name="environment"/>, the process environment unless a test hands one.
    /// The TUI parses no startup option and has no preferences file: the variable alone, else the
    /// default. A run from here then stamps its team's last run as the WPF Launch tab does.
    /// </summary>
    public static RunLauncherViewModel ForCurrentMachine(Func<string, string?>? environment = null) =>
        new(
            new RunTargetDetector(PhysicalTargetProbe.Instance),
            OrkeonProcessRunner.ForCurrentMachine(),
            TryCreateHistoryStore(),
            teamsRoot: TeamsRootLocator.Resolve(environment),
            targetProbe: PhysicalTargetProbe.Instance);

    /// <summary>
    /// The teams root in force and where it came from (STUDIO-61): the variable or the default
    /// for <see cref="ForCurrentMachine"/>; null when the launcher was built without one, and a
    /// run then stamps no team.
    /// </summary>
    public TeamsRootResolution? TeamsRoot { get; }

    /// <summary>The target picker.</summary>
    public TargetSelectionModel Target { get; }

    /// <summary>The options form.</summary>
    public LaunchOptionsModel Options { get; }

    /// <summary>The run lifecycle.</summary>
    public RunSession Session { get; }

    /// <summary>
    /// Working directory of the child process. Null follows the target: the directory holding
    /// the crew, which is also where the CLI's settings resolution starts looking.
    /// </summary>
    public string? WorkingDirectoryOverride { get; set; }

    /// <summary>Mounts already declared by the settings file the run will read, shown read-only.</summary>
    public IReadOnlyList<string> SettingsMounts { get; private set; } = [];

    /// <summary>
    /// The settings file the run will read: the pin, else the team's own file (STUDIO-62), else
    /// null — the CLI's own chain. The team file is looked up again for the current target first.
    /// </summary>
    public string? ResolvedSettingsPath
    {
        get
        {
            SyncTeamSettingsPath();
            return Options.ResolvedSettingsPath;
        }
    }

    /// <summary>
    /// The settings label of the form: « Settings: auto », or « Settings: auto (team file: &lt;path&gt;) »
    /// when the workshop's file for the selected team will be read (STUDIO-62) — nothing is pinned
    /// and the team sits right under the teams root with its <c>settings/&lt;slug&gt;/appsettings.json</c>.
    /// </summary>
    public string DescribeSettings()
    {
        SyncTeamSettingsPath();
        return Options.UseAutomaticSettings && Options.TeamSettingsPath is { Length: > 0 } teamFile
            ? $"Settings: auto (team file: {teamFile})"
            : "Settings: auto";
    }

    /// <summary>Why <see cref="SettingsMounts"/> is empty, when it is empty for a reason.</summary>
    public string? SettingsMountsNotice { get; private set; }

    /// <summary>Options the form leaves enabled for the detected target.</summary>
    public IReadOnlyList<RunOption> AvailableOptions => LaunchOptionsModel.AvailableOptions(Target.Target);

    /// <summary>True when <paramref name="option"/> applies to the detected target.</summary>
    public bool IsOptionAvailable(RunOption option) => LaunchOptionsModel.IsAvailable(Target.Target, option);

    /// <summary>A launch can be prepared: a target was resolved and no run is in flight.</summary>
    public bool CanLaunch => Target.IsResolved && !Session.IsRunning;

    /// <summary>The directory the child process will run in.</summary>
    public string? EffectiveWorkingDirectory =>
        !string.IsNullOrWhiteSpace(WorkingDirectoryOverride)
            ? WorkingDirectoryOverride
            : DirectoryOf(Target.Target);

    /// <summary>Builds the argument list for the current form.</summary>
    /// <param name="dryRun">True to add <c>--validate</c>.</param>
    /// <exception cref="InvalidOperationException">No target is resolved, or an option is invalid.</exception>
    public IReadOnlyList<string> BuildArguments(bool dryRun = false)
    {
        var target = RequireTarget();
        SyncTeamSettingsPath();
        return RunArgumentsBuilder.Build(target, Options.ToLaunchOptions(target, dryRun));
    }

    /// <summary>
    /// The command line equivalent to what will be spawned, for the "this is what runs" field.
    /// Returns the reason instead when the form does not yet add up to one.
    /// </summary>
    public string DescribeCommandLine(bool dryRun = false)
    {
        if (Target.Target is null)
            return "Select a crew to see the command line.";

        try
        {
            return CommandLineDisplay.Format(BuildArguments(dryRun));
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Everything a UI should show before launching: the option/target mismatches and
    /// malformed values from Core, plus the launch mounts' own findings.
    /// </summary>
    public IReadOnlyList<ValidationMessage> Validate(bool dryRun = false)
    {
        var messages = new List<ValidationMessage>();
        SyncTeamSettingsPath();

        if (Target.Target is { } target)
        {
            messages.AddRange(RunArgumentsBuilder.Validate(target, Options.ToLaunchOptions(target, dryRun)));

            // The detector's notices — the root folders a crew/ sub-folder set aside
            // (STUDIO-59) — read as information, like the directory-run advice.
            foreach (var notice in Target.Detection?.Notices ?? [])
                messages.Add(ValidationMessage.Information(notice.Code, notice.Text, target.RunPath));
        }

        messages.AddRange(Options.ValidateMounts());
        return messages;
    }

    /// <summary>True when <see cref="Validate"/> found nothing that would break the launch.</summary>
    public bool HasBlockingErrors(bool dryRun = false) =>
        Validate(dryRun).Any(message => message.Severity == ValidationSeverity.Error);

    /// <summary>
    /// The mount list the runtime will see: the runner's auto-injected mounts, then the launch
    /// mounts, then whatever settings mounts the two did not reach. Empty until a target is
    /// resolved — the auto-injected mounts, and therefore every index, depend on it.
    /// </summary>
    public IReadOnlyList<EffectiveMount> EffectiveMounts =>
        Target.Target is { } target ? Options.ComputeEffectiveMounts(target, SettingsMounts) : [];

    /// <summary>Why <see cref="EffectiveMounts"/> is empty while no crew is selected.</summary>
    public const string EffectiveMountsUnknownNotice =
        "Select a crew first: the runner injects its own mounts ahead of every --mount, so which " +
        "configuration key each mount occupies depends on the crew being launched.";

    /// <summary>
    /// Re-reads the mounts of the settings file the run will read: the pinned one, else the
    /// team's own file (STUDIO-62). With neither nothing is read: which file wins is then the
    /// CLI's decision, and guessing it here would be a second implementation of the resolution
    /// chain — the screen shows the chain instead.
    /// </summary>
    public void RefreshSettingsMounts()
    {
        var path = ResolvedSettingsPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            SettingsMounts = [];
            SettingsMountsNotice =
                "Settings are resolved by the CLI at launch, so the mounts already declared are not known here. " +
                "Pin a file with --settings to list them.";
            return;
        }

        if (!_settingsReader.TryRead(path, out var json, out var readError))
        {
            SettingsMounts = [];
            SettingsMountsNotice = readError;
            return;
        }

        if (!AppSettingsDocument.TryParse(json, out var document, out var parseError))
        {
            SettingsMounts = [];
            SettingsMountsNotice = parseError;
            return;
        }

        SettingsMounts = document.Mounts.RawEntries;
        SettingsMountsNotice = SettingsMounts.Count == 0
            ? $"'{path}' declares no mount."
            : null;
    }

    /// <summary>Spawns the run the form describes.</summary>
    /// <param name="dryRun">True for the "Validate" button: <c>--validate</c>, not recorded in the history.</param>
    /// <param name="onOutput">Called for each output line while the child runs.</param>
    /// <param name="cancellationToken">Cancels the run; so does <see cref="RunSession.RequestCancellation"/>.</param>
    public Task<ProcessRunResult> LaunchAsync(
        bool dryRun = false,
        Action<ProcessOutputLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var target = RequireTarget();

        var request = new RunLaunchRequest
        {
            TargetPath = target.SelectedPath,
            Arguments = BuildArguments(dryRun),
            // Recorded as a pin is: the entry replays on the file the run read (STUDIO-62).
            SettingsPath = Options.ResolvedSettingsPath,
            WorkingDirectory = EffectiveWorkingDirectory,
            RecordInHistory = !dryRun,
        };

        return Session.RunAsync(request, onOutput, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Re-runs a past launch exactly as it was: the recorded argument list is replayed
    /// verbatim, so a replay cannot drift from what actually ran because the form has since
    /// been edited.
    /// </summary>
    public Task<ProcessRunResult> ReplayAsync(
        LaunchHistoryEntry entry,
        Action<ProcessOutputLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Arguments.Count == 0)
            throw new InvalidOperationException($"The history entry for '{entry.Target}' recorded no arguments to replay.");

        var request = new RunLaunchRequest
        {
            TargetPath = entry.Target,
            Arguments = entry.Arguments,
            SettingsPath = entry.SettingsPath,
            WorkingDirectory = entry.WorkingDirectory,
        };

        return Session.RunAsync(request, onOutput, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Loads a past launch back into the form, so the user sees what a replay will run and
    /// can adjust it. Returns false when its target no longer resolves — a crew that moved.
    /// </summary>
    public bool ApplyHistoryEntry(LaunchHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        WorkingDirectoryOverride = entry.WorkingDirectory;
        var resolved = Target.Select(entry.Target).IsResolved;
        SyncTeamSettingsPath();

        // An entry recorded on the team's own file was not a pin (STUDIO-62): the form stays
        // automatic, as it was when the entry was recorded.
        var recordedTeamFile = entry.SettingsPath is { Length: > 0 } recorded
            && Options.TeamSettingsPath is { Length: > 0 } teamFile
            && string.Equals(recorded, teamFile, Orkeon.Domain.FileSystem.PhysicalPathContainment.Comparison);
        Options.UseAutomaticSettings = recordedTeamFile || string.IsNullOrWhiteSpace(entry.SettingsPath);
        Options.ExplicitSettingsPath = recordedTeamFile ? null : entry.SettingsPath;
        RefreshSettingsMounts();

        return resolved;
    }

    /// <summary>
    /// Looks the team's settings file up for the current target (STUDIO-62): the workshop's
    /// <c>settings/&lt;slug&gt;/appsettings.json</c> beside the teams root, for a team folder right
    /// under it; null without a root, outside it, or when the file is missing.
    /// </summary>
    private void SyncTeamSettingsPath() =>
        Options.TeamSettingsPath = TeamSettingsFile.Find(TeamsRoot?.Path, Target.SelectedPath, _directories, _targetProbe.FileExists);

    /// <summary>One display line per past launch, newest first.</summary>
    public IReadOnlyList<string> DescribeHistory() =>
    [
        .. Session.History.Entries.Select(entry =>
            $"{entry.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm}  [{DescribeOutcome(entry)}]  {entry.Target}"),
    ];

    private static string DescribeOutcome(LaunchHistoryEntry entry) =>
        entry.ExitCode is { } code ? $"{entry.Outcome} {code}" : entry.Outcome.ToString();

    private RunTarget RequireTarget() =>
        Target.Target ?? throw new InvalidOperationException(
            "No crew is selected: pick a .yaml/.ork.ts file or a crew directory first.");

    /// <summary>
    /// The directory the CLI is launched from — <see cref="RunTarget.WorkingDirectory"/>, the
    /// same rule the WPF launcher uses.
    /// <para>
    /// This used to derive it from <c>RunPath</c>, which was correct only while the run path's
    /// parent was the folder the user picked. The ADR-008 detector change made <c>RunPath</c>
    /// descend into a promoted team's <c>crew/</c>, and the fix landed in the WPF launcher
    /// alone: the TUI kept starting the CLI inside <c>crew/</c>, where the sidecar, the
    /// appsettings and the team's own output folder are not.
    /// </para>
    /// </summary>
    private static string? DirectoryOf(RunTarget? target) => target?.WorkingDirectory;

    private static LaunchHistoryFileStore? TryCreateHistoryStore() =>
        LaunchHistoryFileStore.TryGetDefaultPath(out var path, out _)
            ? new LaunchHistoryFileStore(path!)
            : null;

    /// <summary>
    /// The target probe seen as a directory probe, for the one question the team file lookup asks
    /// — is the target a folder — over the same declared disk the detector reads. Nothing is ever
    /// created through it.
    /// </summary>
    private sealed class TargetProbeDirectories(ITargetProbe probe) : IDirectoryProbe
    {
        public bool Exists(string path) => probe.DirectoryExists(path);

        public void Create(string path) =>
            throw new NotSupportedException("The launcher's team file lookup never creates a directory.");
    }
}
