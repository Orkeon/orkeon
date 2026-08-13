using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Targets;
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

    /// <summary>Creates the launcher over explicit collaborators (the test seam).</summary>
    public RunLauncherViewModel(
        RunTargetDetector detector,
        OrkeonProcessRunner runner,
        ILaunchHistoryStore? history = null,
        MountValidator? mountValidator = null,
        IAppSettingsReader? settingsReader = null)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(runner);

        Target = new TargetSelectionModel(detector);
        Options = new LaunchOptionsModel(mountValidator);
        Session = new RunSession(runner, history);
        _settingsReader = settingsReader ?? PhysicalAppSettingsReader.Instance;
    }

    /// <summary>Creates the launcher over the real machine: real disk, real processes, real history file.</summary>
    public static RunLauncherViewModel ForCurrentMachine() =>
        new(new RunTargetDetector(), OrkeonProcessRunner.ForCurrentMachine(), TryCreateHistoryStore());

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

    /// <summary>Mounts already declared by the pinned appsettings file, shown read-only.</summary>
    public IReadOnlyList<string> SettingsMounts { get; private set; } = [];

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

        if (Target.Target is { } target)
            messages.AddRange(RunArgumentsBuilder.Validate(target, Options.ToLaunchOptions(target, dryRun)));

        messages.AddRange(Options.ValidateMounts());
        return messages;
    }

    /// <summary>True when <see cref="Validate"/> found nothing that would break the launch.</summary>
    public bool HasBlockingErrors(bool dryRun = false) =>
        Validate(dryRun).Any(message => message.Severity == ValidationSeverity.Error);

    /// <summary>The mount list the runtime will see: launch mounts at their index, settings mounts elsewhere.</summary>
    public IReadOnlyList<EffectiveMount> EffectiveMounts => Options.ComputeEffectiveMounts(SettingsMounts);

    /// <summary>
    /// Re-reads the pinned appsettings file's mounts. In automatic mode nothing is read:
    /// which file wins is the CLI's decision, and guessing it here would be a second
    /// implementation of the resolution chain — the screen shows the chain instead.
    /// </summary>
    public void RefreshSettingsMounts()
    {
        var path = Options.EffectiveSettingsPath;

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
            SettingsPath = Options.EffectiveSettingsPath,
            WorkingDirectory = EffectiveWorkingDirectory,
            RecordInHistory = !dryRun,
        };

        return Session.RunAsync(request, onOutput, cancellationToken);
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

        return Session.RunAsync(request, onOutput, cancellationToken);
    }

    /// <summary>
    /// Loads a past launch back into the form, so the user sees what a replay will run and
    /// can adjust it. Returns false when its target no longer resolves — a crew that moved.
    /// </summary>
    public bool ApplyHistoryEntry(LaunchHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        WorkingDirectoryOverride = entry.WorkingDirectory;
        Options.UseAutomaticSettings = string.IsNullOrWhiteSpace(entry.SettingsPath);
        Options.ExplicitSettingsPath = entry.SettingsPath;
        RefreshSettingsMounts();

        return Target.Select(entry.Target).IsResolved;
    }

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

    private static string? DirectoryOf(RunTarget? target)
    {
        if (target is null)
            return null;

        // A directory target runs from itself; a file target runs from the directory holding it,
        // which is also where the CLI starts looking for an appsettings.json.
        if (target.RunPath == target.SelectedPath && target.Kind is RunTargetKind.MultiFileCrewDirectory)
            return target.RunPath;

        var directory = Path.GetDirectoryName(target.RunPath);
        return string.IsNullOrEmpty(directory) ? null : directory;
    }

    private static LaunchHistoryFileStore? TryCreateHistoryStore() =>
        LaunchHistoryFileStore.TryGetDefaultPath(out var path, out _)
            ? new LaunchHistoryFileStore(path!)
            : null;
}
