using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.Run;
using System.Text.Json;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Validation;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Common;

namespace Orkeon.Studio.Wpf.ViewModels.Launch;

/// <summary>
/// The Launch tab (spec §5): pick a target, pick an appsettings, set the options that apply to that
/// target's shape, then run the co-installed <c>orkeon</c> CLI as a child process with its output
/// streamed live, cancellable, and its exit code interpreted.
/// <para>
/// Running out-of-process is the spec's deliberate choice: a crashing crew cannot take the UI with
/// it, and the behaviour is identical to the CLI by construction rather than by re-implementation.
/// </para>
/// </summary>
public sealed class LaunchTabViewModel : ObservableObject
{
    private readonly OrkeonProcessRunner _runner;
    private readonly RunSession _session;
    private volatile IProcessInputWriter? _input;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IUiDispatcher _dispatcher;
    private readonly IStudioStrings _strings;
    private bool _isRunning;
    private string? _commandLinePreview;
    private string? _statusMessage;
    private ProcessRunResult? _lastResult;
    private BinaryLocation? _binaryLocation;

    /// <summary>Builds the tab over its seams; each one has an in-memory double in the tests.</summary>
    public LaunchTabViewModel(
        OrkeonProcessRunner? processRunner = null,
        ITargetProbe? targetProbe = null,
        IDirectoryProbe? directories = null,
        IPathPicker? picker = null,
        ILaunchHistoryStore? historyStore = null,
        IAppSettingsStore? settingsStore = null,
        IUiDispatcher? dispatcher = null,
        IStudioStrings? strings = null)
    {
        _runner = processRunner ?? OrkeonProcessRunner.ForCurrentMachine();
        // The run lifecycle is the shared Core session, not a re-implementation: the terminal
        // launcher runs over the very same class, which is what keeps the two in step.
        _session = new RunSession(_runner, historyStore);
        _settingsStore = settingsStore ?? PhysicalAppSettingsStore.Instance;
        _dispatcher = dispatcher ?? ImmediateUiDispatcher.Instance;
        _strings = strings ?? EnglishStudioStrings.Instance;
        _strings.CultureChanged += (_, _) =>
            OnPropertiesChanged(nameof(BinaryStatus), nameof(ValidationSummary));

        Target = new TargetSelectionViewModel(targetProbe, picker, _strings);
        Options = new LaunchOptionsViewModel(picker, _strings);
        Mounts = new LaunchMountsViewModel(directories, picker, _strings);
        Log = new RunLogViewModel(_strings);
        Progress = new RunProgressViewModel(_strings);
        History = new LaunchHistoryViewModel(historyStore, _dispatcher);

        Target.TargetChanged += OnTargetChanged;
        Options.Changed += OnInputsChanged;
        Mounts.Changed += OnInputsChanged;
        History.ReplayRequested += OnReplayRequested;

        ValidateCommand = new AsyncRelayCommand(() => ValidateAsync(), CanLaunch);
        RunCommand = new AsyncRelayCommand(() => RunAsync(), CanLaunch);
        ReplayCommand = new AsyncRelayCommand(
            parameter => parameter is LaunchHistoryEntry entry ? ReplayAsync(entry) : Task.CompletedTask,
            _ => !IsRunning);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        ClearLogCommand = new RelayCommand(() => Log.Clear());
        CheckOptionsCommand = new RelayCommand(() => CheckOptions());

        RefreshPreview();
    }

    /// <summary>The target picker (spec §5.1).</summary>
    public TargetSelectionViewModel Target { get; }

    /// <summary>The run options (spec §5.2).</summary>
    public LaunchOptionsViewModel Options { get; }

    /// <summary>The launch-time mount panel (spec §5.2).</summary>
    public LaunchMountsViewModel Mounts { get; }

    /// <summary>The streamed output panel (spec §5.3).</summary>
    public RunLogViewModel Log { get; }

    /// <summary>
    /// What the run reported, as progress rather than scrollback (BUS-06). The raw
    /// <see cref="Log"/> stays available behind it — demoted, not removed.
    /// </summary>
    public RunProgressViewModel Progress { get; }

    /// <summary>The replayable launch history (spec §5.3).</summary>
    public LaunchHistoryViewModel History { get; }

    /// <summary>Runs the crew with <c>--validate</c>: strict load, no LLM probe, no kickoff.</summary>
    public AsyncRelayCommand ValidateCommand { get; }

    /// <summary>Runs the crew for real.</summary>
    public AsyncRelayCommand RunCommand { get; }

    /// <summary>
    /// Re-runs a <see cref="LaunchHistoryEntry"/> exactly as it was recorded. It takes the entry
    /// as its command parameter, which is what the history panel supplies.
    /// </summary>
    public AsyncRelayCommand ReplayCommand { get; }

    /// <summary>Stops the child process: graceful signal first, kill after the grace period.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>Empties the log panel.</summary>
    public RelayCommand ClearLogCommand { get; }

    /// <summary>Re-checks the options against the detected shape without launching anything.</summary>
    public RelayCommand CheckOptionsCommand { get; }

    /// <summary>What the current selection would reject, before anything is spawned.</summary>
    public ObservableCollection<ValidationMessageViewModel> ValidationMessages { get; } = [];

    /// <summary>Whether a child process is currently running.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetProperty(ref _isRunning, value))
                return;

            RunCommand.RaiseCanExecuteChanged();
            ValidateCommand.RaiseCanExecuteChanged();
            ReplayCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>The exact command line that will be run, shown so nothing is hidden from the user.</summary>
    public string? CommandLinePreview
    {
        get => _commandLinePreview;
        private set => SetProperty(ref _commandLinePreview, value);
    }

    /// <summary>The outcome of the last action.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>The result of the last run, exit code and interpretation included.</summary>
    public ProcessRunResult? LastResult
    {
        get => _lastResult;
        private set
        {
            if (SetProperty(ref _lastResult, value))
                OnPropertiesChanged(nameof(ExitCode), nameof(Outcome), nameof(ExitDescription), nameof(HasResult));
        }
    }

    /// <summary>Whether a run has completed at least once.</summary>
    public bool HasResult => LastResult is not null;

    /// <summary>The exit code of the last run.</summary>
    public int? ExitCode => LastResult?.ExitCode;

    /// <summary>How the last exit code is classified.</summary>
    public RunOutcome? Outcome => LastResult?.Outcome;

    /// <summary>The plain-language reading of the last exit code.</summary>
    public string? ExitDescription => LastResult?.Description;

    /// <summary>Where the co-installed CLI was found, or why it was not.</summary>
    public BinaryLocation? BinaryLocation
    {
        get => _binaryLocation;
        private set
        {
            if (SetProperty(ref _binaryLocation, value))
                OnPropertiesChanged(nameof(IsBinaryAvailable), nameof(BinaryStatus));
        }
    }

    /// <summary>Whether the <c>orkeon</c> binary was located.</summary>
    public bool IsBinaryAvailable => BinaryLocation?.Found ?? false;

    /// <summary>The status line about the co-installed CLI.</summary>
    public string BinaryStatus => BinaryLocation switch
    {
        null => _strings[StudioStringKeys.LaunchBinaryNotLocated],
        { Found: true, Path: { } path } => string.Create(CultureInfo.InvariantCulture, $"orkeon: {path}"),
        var location => location.Error ?? _strings[StudioStringKeys.LaunchBinaryNotFound],
    };

    /// <summary>Whether the current selection would be refused before any process is spawned.</summary>
    public bool HasBlockingErrors => ValidationMessages.Any(m => m.IsError);

    /// <summary>Locates the CLI and loads the history. Called once when the window opens.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        BinaryLocation = _runner.LocateBinary();
        await History.LoadAsync(cancellationToken);
    }

    /// <summary>
    /// Reads the mounts declared in the appsettings the launch will use, so the effective-mount table
    /// can show what the <c>--mount</c> arguments actually replace.
    /// </summary>
    public async Task RefreshSettingsMountsAsync(CancellationToken cancellationToken = default)
    {
        if (Options.EffectiveSettingsPath is not { Length: > 0 } path || !_settingsStore.Exists(path))
        {
            Mounts.SetSettingsMounts([]);
            return;
        }

        var (document, _) = await _settingsStore.TryLoadAsync(path, cancellationToken);
        Mounts.SetSettingsMounts(document?.Mounts.RawEntries ?? []);
    }

    /// <summary>
    /// Re-runs the Core launch validation over the current selection and republishes the messages.
    /// This is the check that keeps an option the CLI would silently ignore from being passed.
    /// </summary>
    public IReadOnlyList<ValidationMessage> CheckOptions()
    {
        ValidationMessages.Clear();

        if (Target.Target is not { } target)
        {
            if (Target.Detection is { IsResolved: false, Error: { Length: > 0 } error, ErrorCode: { } code })
                ValidationMessages.Add(new ValidationMessageViewModel(ValidationMessage.Error(code, error)));

            OnPropertiesChanged(nameof(HasBlockingErrors), nameof(ValidationSummary));
            return [];
        }

        var options = BuildOptions();
        var messages = RunArgumentsBuilder.Validate(target, options);

        foreach (var message in messages)
            ValidationMessages.Add(new ValidationMessageViewModel(message));

        foreach (var message in Mounts.LaunchMounts.ValidationMessages)
            ValidationMessages.Add(message);

        OnPropertiesChanged(nameof(HasBlockingErrors), nameof(ValidationSummary));
        RunCommand.RaiseCanExecuteChanged();
        ValidateCommand.RaiseCanExecuteChanged();
        return messages;
    }

    /// <summary>The one-line verdict next to the Run button.</summary>
    public string ValidationSummary
    {
        get
        {
            var errors = ValidationMessages.Count(m => m.IsError);
            var warnings = ValidationMessages.Count(m => m.Severity == ValidationSeverity.Warning);

            return errors == 0 && warnings == 0
                ? _strings[StudioStringKeys.LaunchReady]
                : string.Format(
                    CultureInfo.InvariantCulture,
                    _strings[StudioStringKeys.ConfigErrorsWarnings], errors, warnings);
        }
    }

    /// <summary>Runs the crew with <c>--validate</c> (spec §5.2).</summary>
    public Task<ProcessRunResult?> ValidateAsync(CancellationToken cancellationToken = default) =>
        LaunchAsync(validate: true, cancellationToken);

    /// <summary>Runs the crew for real (spec §5.3).</summary>
    public Task<ProcessRunResult?> RunAsync(CancellationToken cancellationToken = default) =>
        LaunchAsync(validate: false, cancellationToken);

    /// <summary>Stops the running child process.</summary>
    public void Cancel()
    {
        _session.RequestCancellation();
        StatusMessage = _strings[StudioStringKeys.LaunchCancelling];
    }

    /// <summary>The arguments the current selection produces, or null when it cannot produce any.</summary>
    public IReadOnlyList<string>? BuildArguments()
    {
        if (Target.Target is not { } target)
            return null;

        var options = BuildOptions();
        return RunArgumentsBuilder.Validate(target, options).Any(m => m.Severity == ValidationSeverity.Error)
            ? null
            : RunArgumentsBuilder.Build(target, options);
    }

    private RunLaunchOptions BuildOptions(bool validate = false) =>
        Options.ToOptions(Mounts.ToMountArguments(), Mounts.AllowExternalMounts, validate);

    private bool CanLaunch() => !IsRunning && Target.IsResolved;

    private async Task<ProcessRunResult?> LaunchAsync(bool validate, CancellationToken cancellationToken)
    {
        CheckOptions();

        if (Target.Target is not { } target)
        {
            StatusMessage = _strings[StudioStringKeys.LaunchNoTarget];
            return null;
        }

        if (HasBlockingErrors)
        {
            StatusMessage = string.Format(
                CultureInfo.InvariantCulture,
                _strings[StudioStringKeys.LaunchNotLaunchedErrors],
                ValidationMessages.Count(m => m.IsError));
            return null;
        }

        var arguments = RunArgumentsBuilder.Build(target, BuildOptions(validate));
        var workingDirectory = GetWorkingDirectory(target);

        return await ExecuteAsync(
            new RunLaunchRequest
            {
                TargetPath = target.SelectedPath,
                Arguments = arguments,
                SettingsPath = Options.EffectiveSettingsPath,
                WorkingDirectory = workingDirectory,
                // A dry run is not a launch: recording it would fill the replayable history with
                // entries that never ran a crew. The terminal launcher makes the same exclusion.
                RecordInHistory = !validate,
            },
            validate,
            cancellationToken);
    }

    /// <summary>
    /// Re-runs a past launch exactly as it was: the recorded argument list is replayed verbatim,
    /// so a replay cannot drift from what actually ran because the form has since been edited.
    /// The form is still refreshed from the entry, so the user sees what is running.
    /// </summary>
    public async Task<ProcessRunResult?> ReplayAsync(
        LaunchHistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Arguments.Count == 0)
        {
            StatusMessage = string.Format(
                CultureInfo.InvariantCulture,
                _strings[StudioStringKeys.LaunchNothingToReplay],
                entry.Target);
            return null;
        }

        LoadIntoForm(entry);

        return await ExecuteAsync(
            new RunLaunchRequest
            {
                TargetPath = entry.Target,
                Arguments = entry.Arguments,
                SettingsPath = entry.SettingsPath,
                WorkingDirectory = entry.WorkingDirectory,
            },
            dryRun: false,
            cancellationToken);
    }

    private async Task<ProcessRunResult> ExecuteAsync(
        RunLaunchRequest request,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        IsRunning = true;

        Log.AppendNotice(CommandLineDisplay.Format(request.Arguments));
        StatusMessage = dryRun
            ? _strings[StudioStringKeys.LaunchValidating]
            : _strings[StudioStringKeys.LaunchRunning];

        Progress.Reset(Answer);

        try
        {
            var result = await _session.RunAsync(
                request,
                line => _dispatcher.Post(() => Receive(line)),
                writer => _input = writer,
                cancellationToken: cancellationToken);

            LastResult = result;

            // A dry run's outcome is a verdict, not just an exit code: the launcher says whether
            // the crew validated, as the terminal launcher does.
            var outcome = LaunchOutcomeFormatter.Describe(result, dryRun, _strings);
            StatusMessage = outcome;
            Log.AppendNotice(outcome);

            // The session already recorded the run; the panel only projects its list.
            if (request.RecordInHistory)
                History.Publish(_session.History);

            return result;
        }
        finally
        {
            IsRunning = false;
            _input = null;
        }
    }

    /// <summary>
    /// Routes one output line: a protocol event feeds the progress panel, anything else lands
    /// in the raw log. A line the panel cannot read is never dropped — what the stream said
    /// stays visible, which is the rule the terminal launcher already followed.
    /// </summary>
    private void Receive(ProcessOutputLine line)
    {
        if (line.Channel == ProcessOutputChannel.StandardOutput && Progress.TryApply(line.Text))
            return;

        Log.Append(line);
    }

    /// <summary>
    /// Sends one answer down the run's stdin. False when no child is listening, which the
    /// panel reads as "the question is still open" rather than pretending it was answered.
    /// </summary>
    private bool Answer(string correlationId, string value) =>
        _input is { } writer
        && writer.TryWriteLine(JsonSerializer.Serialize(
            new { kind = RunEventKinds.InputGiven, correlationId, value }));

    private static string? GetWorkingDirectory(RunTarget target) =>
        target.Kind is RunTargetKind.MultiFileCrewDirectory or RunTargetKind.ScriptDirectory
            ? target.SelectedPath
            : System.IO.Path.GetDirectoryName(target.SelectedPath);

    private void OnTargetChanged(object? sender, EventArgs e)
    {
        Options.Target = Target.Target;
        RunCommand.RaiseCanExecuteChanged();
        ValidateCommand.RaiseCanExecuteChanged();
        RefreshPreview();
    }

    private void OnInputsChanged(object? sender, EventArgs e) => RefreshPreview();

    /// <summary>
    /// One click replays: the history panel's button runs the recorded command line, it does not
    /// merely fill the form in and wait for a second click on Run.
    /// </summary>
    private void OnReplayRequested(object? sender, LaunchReplayEventArgs e) =>
        ReplayCommand.Execute(e.Entry);

    /// <summary>
    /// Shows a past launch in the form, so the user can see and then adjust what was replayed.
    /// The target is re-detected rather than trusted blindly — the crew may have moved — but the
    /// replay itself runs the recorded arguments whatever this finds.
    /// </summary>
    private void LoadIntoForm(LaunchHistoryEntry entry)
    {
        Target.Select(entry.Target);

        if (entry.SettingsPath is { Length: > 0 } settingsPath)
        {
            Options.SettingsPath = settingsPath;
            Options.SettingsMode = SettingsSelectionMode.ExplicitPath;
        }
    }

    private void RefreshPreview()
    {
        // The mount table's indices depend on the mounts the runner injects ahead of the --mount
        // arguments, which depend on the target and on whether LLM logging is on.
        Mounts.AutoInjection = Target.Target is { } target
            ? MountAutoInjection.For(target, BuildOptions())
            : null;

        // The builder throws on an option the target's shape cannot carry, so the preview is built
        // from the arguments only once the same validation has passed; otherwise the message list is
        // what tells the user why there is nothing to show.
        var arguments = BuildArguments();
        CommandLinePreview = arguments is null ? null : CommandLineDisplay.Format(arguments);

        CheckOptions();
    }
}
