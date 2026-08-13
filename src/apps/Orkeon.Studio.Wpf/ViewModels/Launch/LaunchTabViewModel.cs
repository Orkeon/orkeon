using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Launch;
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
    private readonly IAppSettingsStore _settingsStore;
    private readonly IUiDispatcher _dispatcher;
    private CancellationTokenSource? _cancellation;
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
        IUiDispatcher? dispatcher = null)
    {
        _runner = processRunner ?? OrkeonProcessRunner.ForCurrentMachine();
        _settingsStore = settingsStore ?? PhysicalAppSettingsStore.Instance;
        _dispatcher = dispatcher ?? ImmediateUiDispatcher.Instance;

        Target = new TargetSelectionViewModel(targetProbe, picker);
        Options = new LaunchOptionsViewModel(picker);
        Mounts = new LaunchMountsViewModel(directories, picker);
        Log = new RunLogViewModel();
        History = new LaunchHistoryViewModel(historyStore, _dispatcher);

        Target.TargetChanged += OnTargetChanged;
        Options.Changed += OnInputsChanged;
        Mounts.Changed += OnInputsChanged;
        History.ReplayRequested += OnReplayRequested;

        ValidateCommand = new AsyncRelayCommand(() => ValidateAsync(), CanLaunch);
        RunCommand = new AsyncRelayCommand(() => RunAsync(), CanLaunch);
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

    /// <summary>The replayable launch history (spec §5.3).</summary>
    public LaunchHistoryViewModel History { get; }

    /// <summary>Runs the crew with <c>--validate</c>: strict load, no LLM probe, no kickoff.</summary>
    public AsyncRelayCommand ValidateCommand { get; }

    /// <summary>Runs the crew for real.</summary>
    public AsyncRelayCommand RunCommand { get; }

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
        null => "The orkeon CLI has not been located yet.",
        { Found: true, Path: { } path } => string.Create(CultureInfo.InvariantCulture, $"orkeon: {path}"),
        var location => location.Error ?? "The orkeon CLI was not found.",
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
                ? "Ready to launch."
                : string.Create(CultureInfo.InvariantCulture, $"{errors} error(s), {warnings} warning(s).");
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
        _cancellation?.Cancel();
        StatusMessage = "Cancelling: the CLI is asked to stop, and is killed if it does not.";
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
            StatusMessage = "No target resolved.";
            return null;
        }

        if (HasBlockingErrors)
        {
            StatusMessage = string.Create(
                CultureInfo.InvariantCulture,
                $"Not launched: {ValidationMessages.Count(m => m.IsError)} error(s) must be fixed first.");
            return null;
        }

        var arguments = RunArgumentsBuilder.Build(target, BuildOptions(validate));
        var workingDirectory = GetWorkingDirectory(target);

        var entry = LaunchHistoryEntry.Starting(
            target.SelectedPath,
            arguments,
            Options.EffectiveSettingsPath,
            workingDirectory);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellation = cancellation;
        IsRunning = true;

        Log.AppendNotice(CommandLineDisplay.Format(arguments));
        StatusMessage = validate ? "Validating…" : "Running…";

        try
        {
            var result = await _runner.RunAsync(
                arguments,
                workingDirectory,
                line => _dispatcher.Post(() => Log.Append(line)),
                gracePeriod: null,
                cancellation.Token);

            LastResult = result;
            StatusMessage = result.Description;
            Log.AppendNotice(result.Description);

            await History.RecordAsync(entry.WithResult(result), CancellationToken.None);
            return result;
        }
        finally
        {
            IsRunning = false;
            _cancellation = null;
        }
    }

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

    private void OnReplayRequested(object? sender, LaunchReplayEventArgs e)
    {
        var entry = e.Entry;

        // Replaying re-detects the target rather than trusting the stored path blindly: the crew may
        // have moved or changed shape since the run was recorded.
        Target.Select(entry.Target);

        if (entry.SettingsPath is { Length: > 0 } settingsPath)
        {
            Options.SettingsPath = settingsPath;
            Options.SettingsMode = SettingsSelectionMode.ExplicitPath;
        }

        StatusMessage = string.Create(
            CultureInfo.InvariantCulture,
            $"Replay prepared from the run of {entry.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm}.");
    }

    private void RefreshPreview()
    {
        // The builder throws on an option the target's shape cannot carry, so the preview is built
        // from the arguments only once the same validation has passed; otherwise the message list is
        // what tells the user why there is nothing to show.
        var arguments = BuildArguments();
        CommandLinePreview = arguments is null ? null : CommandLineDisplay.Format(arguments);

        CheckOptions();
    }
}
