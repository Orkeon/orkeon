using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Run;
using System.Text.Json;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Teams;
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

    // The last run.finished usage, captured on the process thread (W-08): the history
    // entry is recorded before the dispatcher drains, so it cannot read the progress
    // model — this capture is the join between the event stream and the history.
    private long? _finalTokens;
    private long? _finalCacheHitTokens;
    private long? _finalCacheMissTokens;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IUiDispatcher _dispatcher;
    private readonly IStudioStrings _strings;
    private readonly Func<string, IReadOnlyDictionary<string, string>?>? _environmentForTarget;
    private readonly Func<IReadOnlyList<string>> _declaredMounts;
    private readonly IDirectoryProbe _directories;
    private bool _isRunning;
    private string? _runningTarget;
    private bool _isJournalOpen;
    private readonly IShellOpener? _shellOpener;
    private TargetDescription _team = new();
    /// <summary>The pinned settings file the panel's settings mounts were read from; null for the live declared list.</summary>
    private string? _settingsMountsSource;
    private string? _commandLinePreview;
    private bool _commandLineCopied;
    private readonly IClipboardService _clipboard;
    private string? _statusMessage;
    private ProcessRunResult? _lastResult;
    private BinaryLocation? _binaryLocation;

    /// <summary>Builds the tab over its seams; each one has an in-memory double in the tests.</summary>
    public LaunchTabViewModel(LaunchTabDependencies? dependencies = null)
    {
        var seams = dependencies ?? new LaunchTabDependencies();

        _environmentForTarget = seams.EnvironmentForTarget;
        _declaredMounts = seams.DeclaredMounts ?? (() => []);
        _clipboard = seams.Clipboard ?? new InMemoryClipboardService();
        _directories = seams.Directories ?? PhysicalDirectoryProbe.Instance;
        _runner = seams.ProcessRunner ?? OrkeonProcessRunner.ForCurrentMachine();
        // The run lifecycle is the shared Core session, not a re-implementation: the terminal
        // launcher runs over the very same class, which is what keeps the two in step.
        _session = new RunSession(_runner, seams.HistoryStore);
        _settingsStore = seams.SettingsStore ?? PhysicalAppSettingsStore.Instance;
        _dispatcher = seams.Dispatcher ?? ImmediateUiDispatcher.Instance;
        _strings = seams.Strings ?? EnglishStudioStrings.Instance;
        _strings.CultureChanged += (_, _) =>
        {
            OnPropertiesChanged(nameof(BinaryStatus), nameof(ValidationSummary),
                nameof(CliBanner), nameof(TeamMetaLine), nameof(OpenResultLabel));
            RaiseRunStateChanged();
        };

        _shellOpener = seams.ShellOpener;
        Target = new TargetSelectionViewModel(seams.TargetProbe, seams.Picker, _strings);
        Options = new LaunchOptionsViewModel(seams.Picker, _strings);
        Mounts = new LaunchMountsViewModel(seams.Directories, seams.Picker, _strings);
        Log = new RunLogViewModel(_strings);
        Progress = new RunProgressViewModel(_strings, seams.Clock);
        History = new LaunchHistoryViewModel(seams.HistoryStore, _dispatcher, _strings, _shellOpener, _declaredMounts);

        Target.TargetChanged += OnTargetChanged;
        Options.Changed += OnInputsChanged;
        Mounts.Changed += OnInputsChanged;
        History.ReplayRequested += OnReplayRequested;

        ValidateCommand = new AsyncRelayCommand(() => ValidateAsync(), CanLaunch);
        RunCommand = new AsyncRelayCommand(() => RunAsync(), CanLaunch);
        // The undeclared-folders refusal gates the replay too. A recorded argument list is
        // replayed verbatim precisely so it cannot drift from what ran - but "Settings > Allowed
        // folders" is machine policy, and it changes. Without this, revoking a folder left every
        // past launch of that team one click away from running against it anyway, from a button
        // whose whole promise is that it changes nothing.
        ReplayCommand = new AsyncRelayCommand(
            parameter => parameter is LaunchHistoryEntry entry ? ReplayAsync(entry) : Task.CompletedTask,
            _ => IsBinaryAvailable && !IsRunning && !IsBlockedByUndeclaredFolders);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        OpenAllowedFoldersCommand = new RelayCommand(() => OpenAllowedFoldersRequested?.Invoke(this, EventArgs.Empty));
        CopyCommandLineCommand = new RelayCommand(CopyCommandLine, () => CommandLinePreview is { Length: > 0 });
        ChooseTeamCommand = new RelayCommand(() => ChooseTeamRequested?.Invoke(this, EventArgs.Empty));
        CreateTeamCommand = new RelayCommand(() => CreateTeamRequested?.Invoke(this, EventArgs.Empty));
        ClearLogCommand = new RelayCommand(() => Log.Clear());
        OpenResultCommand = new RelayCommand(OpenResult, () => CanOpenResult);
        CheckOptionsCommand = new RelayCommand(() => CheckOptions());

        // Locate the CLI now rather than only in InitializeAsync. The lookup is a synchronous
        // filesystem probe either way, and settling it here keeps the launch buttons from
        // spending the first frames enabled — offering a run that cannot happen — before the
        // answer lands. InitializeAsync still refreshes it: an install can happen while Studio
        // is open, and the Diagnostic screen re-runs it.
        BinaryLocation = _runner.LocateBinary();

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
            RaiseRunStateChanged();
        }
    }

    /// <summary>
    /// The target of the run in flight — the path its request carried when it started — or null
    /// while nothing runs (STUDIO-28, D-02). Never <see cref="TargetSelectionViewModel.SelectedPath"/>:
    /// the picker stays live during a run, and a folder browsed to since is not the one being read.
    /// My teams asks it before a gesture that moves or hides a team's folder.
    /// </summary>
    public string? RunningTarget
    {
        get => _runningTarget;
        private set => SetProperty(ref _runningTarget, value);
    }

    /// <summary>The exact command line that will be run, shown so nothing is hidden from the user.</summary>
    public string? CommandLinePreview
    {
        get => _commandLinePreview;
        private set
        {
            if (!SetProperty(ref _commandLinePreview, value))
                return;

            // A new command is not the one that was copied: the button reads "copy" again.
            CommandLineCopied = false;
            CopyCommandLineCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Puts <see cref="CommandLinePreview"/> on the clipboard, verbatim — the same text a
    /// terminal takes. The owner's screenshot of 2026-09-20: the command was readable and not
    /// selectable, so reproducing a Studio launch by hand meant retyping a ULID.
    /// </summary>
    public RelayCommand CopyCommandLineCommand { get; }

    /// <summary>True right after a copy, until the command changes; the button's label follows.</summary>
    public bool CommandLineCopied
    {
        get => _commandLineCopied;
        private set => SetProperty(ref _commandLineCopied, value);
    }

    private void CopyCommandLine()
    {
        if (CommandLinePreview is not { Length: > 0 } command)
            return;

        _clipboard.SetText(command);
        CommandLineCopied = true;
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
                OnPropertiesChanged(nameof(ExitCode), nameof(Outcome), nameof(ExitDescription), nameof(HasResult), nameof(ResultMetricsLine));
            RaiseRunStateChanged();
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

    /// <summary>
    /// What the finished run cost, under the result sentence (T-23): tokens, cache hit as a
    /// percentage AND in tokens, wall time. A metric the stream did not measure produces no
    /// chip at all — never a zero, which would read as «it cost nothing».
    /// </summary>
    public string? ResultMetricsLine
    {
        get
        {
            var chips = Orkeon.Studio.Core.Launch.UsageMetricsFormatter.Chips(
                _finalTokens is > 0 ? _finalTokens : null,
                _finalCacheHitTokens,
                _finalCacheMissTokens,
                Progress.FinalDurationMs,
                _strings,
                CultureInfo.CurrentCulture);

            return chips.Count > 0 ? string.Join(" · ", chips) : null;
        }
    }

    /// <summary>Where the co-installed CLI was found, or why it was not.</summary>
    public BinaryLocation? BinaryLocation
    {
        get => _binaryLocation;
        private set
        {
            if (!SetProperty(ref _binaryLocation, value))
                return;

            OnPropertiesChanged(nameof(IsBinaryAvailable), nameof(BinaryStatus), nameof(CliBanner));

            // The probe finishes after the window is up: without this the buttons keep the
            // enabled state they were born with.
            RunCommand.RaiseCanExecuteChanged();
            ValidateCommand.RaiseCanExecuteChanged();
            ReplayCommand.RaiseCanExecuteChanged();
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
    /// Publishes the mounts in force from the appsettings the launch will use, so the
    /// effective-mount table shows what the <c>--mount</c> arguments actually replace and a
    /// team folder the settings already hold is not laid a second time (STUDIO-15 D-05).
    /// <para>
    /// A pinned <c>--settings</c> file is read as is. In automatic mode the CLI walks its own
    /// resolution chain, whose last step is the per-user file <c>orkeon init</c> writes — the
    /// very file « Settings › Authorized folders » edits, read live through the same seam
    /// the launch refusal uses. An adopted team lives under the teams root, where no closer
    /// file exists, so that list is what the run will find; a crew kept next to its own
    /// appsettings.json is the expert's arrangement, and pinning that file shows exactly it.
    /// </para>
    /// </summary>
    public async Task RefreshSettingsMountsAsync(CancellationToken cancellationToken = default)
    {
        if (Options.EffectiveSettingsPath is not { Length: > 0 } path)
        {
            Mounts.SetSettingsMounts(_declaredMounts());
            return;
        }

        if (!_settingsStore.Exists(path))
        {
            Mounts.SetSettingsMounts([]);
            return;
        }

        var (document, _) = await _settingsStore.TryLoadAsync(path, cancellationToken);
        Mounts.SetSettingsMounts(document?.Mounts.RawEntries ?? []);
    }

    /// <summary>
    /// Keeps the panel's settings mounts current before the command line is rebuilt: the
    /// declared list is cheap and read every time; a pinned file is read once per pin
    /// (<see cref="_settingsMountsSource"/>), not on every keystroke of every other field.
    /// </summary>
    private void PublishSettingsMounts()
    {
        var path = Options.EffectiveSettingsPath;
        if (path is not { Length: > 0 })
        {
            _settingsMountsSource = null;
            Mounts.SetSettingsMounts(_declaredMounts());
            return;
        }

        if (string.Equals(path, _settingsMountsSource, StringComparison.Ordinal))
            return;

        _settingsMountsSource = path;
        _ = RefreshSettingsMountsAsync();
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
                ValidationMessages.Add(new ValidationMessageViewModel(ValidationMessage.Error(code, error), _strings));

            OnPropertiesChanged(nameof(HasBlockingErrors), nameof(ValidationSummary));
            return [];
        }

        var options = BuildOptions();
        var messages = RunArgumentsBuilder.Validate(target, options);

        foreach (var message in messages)
            ValidationMessages.Add(new ValidationMessageViewModel(message, _strings));

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
    public Task<ProcessRunResult?> ValidateAsync(CancellationToken cancellationToken = default)
    {
        BeginLaunch();
        return LaunchAsync(validate: true, cancellationToken);
    }

    /// <summary>
    /// Runs the crew for real (spec §5.3). With the dry-run-first option (mock,
    /// expert options), a <c>--validate</c> pass runs first and a failed one stops here —
    /// the dry run is exactly the protection it claims to be.
    /// </summary>
    public async Task<ProcessRunResult?> RunAsync(CancellationToken cancellationToken = default)
    {
        BeginLaunch();

        if (Options.ValidateFirst)
        {
            var validation = await LaunchAsync(validate: true, cancellationToken).ConfigureAwait(true);
            if (validation is not { ExitCode: 0 })
                return validation;
        }

        return await LaunchAsync(validate: false, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Stops the running child process.</summary>
    public void Cancel()
    {
        _session.RequestCancellation();
        StatusMessage = _strings[StudioStringKeys.LaunchCancelling];
    }

    /// <summary>
    /// The arguments the current selection produces, empty when it cannot produce any — no target
    /// is resolved, or the selection carries a blocking validation error. A built list always opens
    /// with the run verb and the target path, so emptiness is unambiguous.
    /// </summary>
    public IReadOnlyList<string> BuildArguments()
    {
        if (Target.Target is not { } target)
            return [];

        var options = BuildOptions();
        if (RunArgumentsBuilder.Validate(target, options).Any(m => m.Severity == ValidationSeverity.Error))
            return [];

        return RunArgumentsBuilder.Build(target, options);
    }

    private RunLaunchOptions BuildOptions(bool validate = false) =>
        Options.ToOptions(Mounts.ToMountArguments(), Mounts.AllowExternalMounts, validate, Mounts.ToMountIdArguments());

    // A launch is an invocation of the orkeon CLI, so an absent CLI is a refusal, not a
    // late failure: a live Run button that answers a click with «the tool was not located»
    // is a button that lied about being available. The banner says what is wrong; the
    // commands must agree with it.
    private bool CanLaunch() =>
        IsBinaryAvailable && !IsRunning && Target.IsResolved && !IsBlockedByUndeclaredFolders && !IsBlockedByUnknownMountIds;

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
                EnvironmentOverrides = EnvironmentFor(target.SelectedPath),
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

        BeginLaunch();
        LoadIntoForm(entry);

        return await ExecuteAsync(
            new RunLaunchRequest
            {
                TargetPath = entry.Target,
                Arguments = entry.Arguments,
                SettingsPath = entry.SettingsPath,
                WorkingDirectory = entry.WorkingDirectory,
                // The argv is replayed verbatim; the profile is resolved fresh — a team
                // re-elected onto another model replays on what it runs on today.
                EnvironmentOverrides = EnvironmentFor(entry.Target),
            },
            dryRun: false,
            cancellationToken);
    }

    /// <summary>The environment an adopted team's model profile lays over the launch; empty otherwise.</summary>
    private IReadOnlyDictionary<string, string> EnvironmentFor(string targetPath) =>
        _environmentForTarget?.Invoke(targetPath)
        ?? new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// A launch starts from a clean screen (STUDIO-17). The journal used to accumulate across
    /// runs, and the previous run's verdict — exit badge, result row, « Open the result » —
    /// stayed on screen for the whole of the next run, so a reader could take the old outcome
    /// for the new one. Called once per user gesture, not per pass: a « validate first »
    /// launch keeps its dry run and its real run in one journal. « Clear the journal » stays
    /// for the rest; « Copy » is how a journal survives the next click.
    /// </summary>
    private void BeginLaunch()
    {
        Log.Clear();
        LastResult = null;
    }

    private async Task<ProcessRunResult> ExecuteAsync(
        RunLaunchRequest request,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        // The target first, so whoever reacts to IsRunning already reads what is running.
        RunningTarget = request.TargetPath;
        IsRunning = true;

        Log.AppendCommand(CommandLineDisplay.Format(request.Arguments));
        StatusMessage = dryRun
            ? _strings[StudioStringKeys.LaunchValidating]
            : _strings[StudioStringKeys.LaunchRunning];

        Progress.Reset(Answer, Reply);
        _finalTokens = _finalCacheHitTokens = _finalCacheMissTokens = null;

        try
        {
            var result = await _session.RunAsync(
                request,
                line =>
                {
                    CaptureFinalUsage(line);
                    _dispatcher.Post(() => Receive(line));
                },
                writer => _input = writer,
                cancellationToken: cancellationToken,
                enrichEntry: entry => entry.WithUsage(_finalTokens, _finalCacheHitTokens, _finalCacheMissTokens));

            LastResult = result;

            // A dry run's outcome is a verdict, not just an exit code: the launcher says whether
            // the crew validated, as the terminal launcher does.
            var outcome = LaunchOutcomeFormatter.Describe(result, dryRun, _strings);
            StatusMessage = outcome;
            Log.AppendOutcome(outcome);

            // The session already recorded the run; the panel only projects its list.
            if (request.RecordInHistory)
            {
                History.Publish(_session.History);
                // The team cards' last-run line reads the same history —
                // the shell refreshes it now, not at the next app start (review D5).
                RunRecorded?.Invoke(this, EventArgs.Empty);
            }

            return result;
        }
        finally
        {
            IsRunning = false;
            RunningTarget = null;
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
    /// Reads the run's closing usage off the raw line, on the process thread — before the
    /// dispatcher, because the history entry is recorded the moment the process exits.
    /// </summary>
    private void CaptureFinalUsage(ProcessOutputLine line)
    {
        if (line.Channel != ProcessOutputChannel.StandardOutput
            || !OrkeonEventParser.TryParse(line.Text, out var orkeonEvent)
            || orkeonEvent!.Kind != RunEventKinds.RunFinished)
        {
            return;
        }

        var tokens = orkeonEvent.GetInt64("tokens");
        _finalTokens = tokens is > 0 ? tokens : null;
        _finalCacheHitTokens = orkeonEvent.GetInt64("cacheHitTokens");
        _finalCacheMissTokens = orkeonEvent.GetInt64("cacheMissTokens");
    }

    /// <summary>
    /// Sends one answer down the run's stdin. False when no child is listening, which the
    /// panel reads as "the question is still open" rather than pretending it was answered.
    /// </summary>
    private bool Answer(string correlationId, string value) =>
        _input is { } writer
        && writer.TryWriteLine(JsonSerializer.Serialize(
            new { kind = RunEventKinds.InputGiven, correlationId, value }));

    /// <summary>
    /// Replies to an agent's <c>send</c> down the run's stdin — the seat at the hub the doc
    /// used to say Studio does not take. Text that parses as JSON travels as that JSON (an
    /// agent may expect a shape); anything else travels as a plain JSON string. The wire form
    /// is the one <c>RunClient.ReplyToAgent</c> writes and the bridge's <c>ReplyFromPeer</c>
    /// requires.
    /// </summary>
    private bool Reply(string correlationId, string text) =>
        _input is { } writer
        && writer.TryWriteLine(JsonSerializer.Serialize(
            new { kind = "reply", correlationId, payload = AsJsonPayload(text) }));

    private static object AsJsonPayload(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return text;
        }
    }

    private static string? GetWorkingDirectory(RunTarget target) => target.WorkingDirectory;

    /// <summary>Raised after a real (non-dry) run landed in the history.</summary>
    public event EventHandler? RunRecorded;

    /// <summary>
    /// Re-reads the selected target's sidecar — the change-folders action may have edited
    /// the very team the launcher points at, and the run must lay the NEW mounts
    /// (review D6: the chips and the command must not disagree, even for a minute).
    /// </summary>
    public void RefreshTeamDescription()
    {
        _team = TeamCatalog.DescribeTarget(Target.SelectedPath, _declaredMounts());
        Mounts.SetTeamMounts(_team.ResolvedMounts);
        Mounts.AllowExternalMounts = ReachesOutsideTheTeam();
        OnPropertiesChanged(nameof(TeamHeadline), nameof(TeamMetaLine), nameof(HasTeamCard),
            nameof(UndeclaredTeamFolders), nameof(IsBlockedByUndeclaredFolders), nameof(UndeclaredFoldersMessage),
            nameof(UnknownTeamMountIds), nameof(IsBlockedByUnknownMountIds), nameof(UnknownMountIdsMessage));
        RunCommand.RaiseCanExecuteChanged();
        ValidateCommand.RaiseCanExecuteChanged();
        // Replay is gated on the same refusal, so it has to be told when the refusal changes -
        // otherwise the button stays enabled until something else happens to refresh it.
        ReplayCommand.RaiseCanExecuteChanged();
        RefreshPreview();
    }

    /// <summary>
    /// Follows a team renamed from My teams (STUDIO-28): a target inside <paramref name="from"/> —
    /// the folder, or a crew file in it — is picked again at the same place under
    /// <paramref name="to"/>, where the team now is. Any other target is left as it is.
    /// </summary>
    public void FollowRenamedTeam(string from, string to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        var folder = TeamCatalog.NormalizePath(from);
        if (Target.SelectedPath is not { Length: > 0 } selected
            || string.Equals(folder, TeamCatalog.NormalizePath(to), Orkeon.Domain.FileSystem.PhysicalPathContainment.Comparison)
            || !Orkeon.Domain.FileSystem.PhysicalPathContainment.IsUnder(TeamCatalog.NormalizePath(selected), folder))
        {
            return;
        }

        var inside = System.IO.Path.GetRelativePath(folder, TeamCatalog.NormalizePath(selected));
        Target.Select(inside == "." ? to : System.IO.Path.Combine(to, inside));
    }

    /// <summary>
    /// Display name of the selected team (sidecar-backed, file name otherwise) — one line,
    /// whatever the sidecar says (STUDIO-16, D-01): a TextBlock renders line breaks even
    /// without wrapping, and a pasted page in <c>name</c> used to fill the whole card.
    /// </summary>
    public string? TeamHeadline => _team.Name is { Length: > 0 } name ? TeamCatalog.NormalizeName(name) : _team.Name;

    /// <summary>"3 agents · setting X" — only the parts the catalog can honestly assert.</summary>
    public string? TeamMetaLine
    {
        get
        {
            var parts = new List<string>(4);
            if (_team.AgentCount is { } agents)
                parts.Add(string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.RunMetaAgents], agents));
            foreach (var mountString in _team.Mounts)
            {
                // "reads /docs · writes to /output" — the sidecar's mounts, in words.
                if (MountDefinition.TryParse(mountString, out var mount, out _) && mount is not null)
                {
                    parts.Add(string.Format(
                        CultureInfo.CurrentCulture,
                        _strings[mount.Rights == MountRights.ReadOnly ? StudioStringKeys.RunMetaReads : StudioStringKeys.RunMetaWrites],
                        mount.VirtualPath));
                }
            }

            // The derived one-paragraph summary, never the whole need (STUDIO-16, D-03): the
            // need is the user's brief, pages long when a README was pasted.
            if (_team.Summary is { Length: > 0 } summary)
                parts.Add(summary);
            if (_team.Profile is { Length: > 0 } profile)
                parts.Add(string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.RunMetaProfile], profile));
            return parts.Count > 0 ? string.Join(" · ", parts) : null;
        }
    }

    /// <summary>
    /// The team's folders that no settings entry allows and that do not live inside the team
    /// itself — spelled as the virtual paths the agents address (ADR-008).
    /// <para>
    /// A team's own <c>/output</c> and <c>/input</c> are created inside the team at adoption and
    /// are never declared; they are the team's plumbing, not a reach outside the allow-list.
    /// Counting them here would make every adopted team unlaunchable.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> UndeclaredTeamFolders =>
        DeclaredMounts.BlockingFolders(_team.Mounts, _declaredMounts(), TeamDirectory());

    /// <summary>
    /// Whether the run is refused. The Settings > Allowed folders screen is the list of what this
    /// machine allows: a team reaching outside it does not start, it says which folder and why.
    /// Discovering that from a run that failed halfway is the outcome this replaces.
    /// </summary>
    public bool IsBlockedByUndeclaredFolders => UndeclaredTeamFolders.Count > 0;

    /// <summary>The refusal, naming the folders and the two ways out.</summary>
    public string UndeclaredFoldersMessage =>
        IsBlockedByUndeclaredFolders
            ? string.Format(
                CultureInfo.CurrentCulture,
                _strings[StudioStringKeys.RunBlockedUndeclared],
                string.Join(", ", UndeclaredTeamFolders))
            : "";

    /// <summary>
    /// The folder declarations the team names by id that this machine does not have (VFS-90,
    /// D-06): a team imported from elsewhere, or an entry removed from the settings since.
    /// </summary>
    public IReadOnlyList<string> UnknownTeamMountIds => Mounts.UnknownTeamMountIds;

    /// <summary>Whether the run is refused because the team refers to a declaration missing here.</summary>
    public bool IsBlockedByUnknownMountIds => UnknownTeamMountIds.Count > 0;

    /// <summary>The refusal, naming the ids and the two ways out.</summary>
    public string UnknownMountIdsMessage =>
        IsBlockedByUnknownMountIds
            ? string.Format(
                CultureInfo.CurrentCulture,
                _strings[StudioStringKeys.RunBlockedUnknownMountId],
                string.Join(", ", UnknownTeamMountIds))
            : "";

    /// <summary>Opens the allowed-folders list — the shell lands on the settings' folders tab.</summary>
    public RelayCommand OpenAllowedFoldersCommand { get; }

    /// <summary>Novice empty state: go pick a team.</summary>
    public RelayCommand ChooseTeamCommand { get; }

    /// <summary>Novice empty state: go make one.</summary>
    public RelayCommand CreateTeamCommand { get; }

    /// <summary>Raised by <see cref="OpenAllowedFoldersCommand"/>.</summary>
    public event EventHandler? OpenAllowedFoldersRequested;

    /// <summary>
    /// Raised by the Novice empty state's choose-a-team button — the shell brings My teams
    /// forward. The screen used to name that place and give no way to reach it.
    /// </summary>
    public event EventHandler? ChooseTeamRequested;

    /// <summary>Raised by its create-a-team sibling — the shell opens the wizard.</summary>
    public event EventHandler? CreateTeamRequested;

    /// <summary>The team card only exists once a target resolves.</summary>
    public bool HasTeamCard => Target.IsResolved;

    // The four states the progress card can be in, in the order the user meets them.
    private enum RunCardState
    {
        Idle,
        Running,
        Succeeded,
        Failed,
    }

    // Title, badge, tone and button label are four readings of one and the same state, so they
    // are derived from a single verdict rather than from four parallel chains of conditions:
    // a fifth reading cannot drift away from the other four.
    private RunCardState CardState
    {
        get
        {
            if (IsRunning)
                return RunCardState.Running;

            if (!HasResult)
                return RunCardState.Idle;

            return Outcome == RunOutcome.Success ? RunCardState.Succeeded : RunCardState.Failed;
        }
    }

    /// <summary>Plain-language state of the progress card: ready / running / finished.</summary>
    public string RunStateTitle =>
        _strings[CardState switch
        {
            RunCardState.Running => StudioStringKeys.RunStateRunning,
            RunCardState.Idle => StudioStringKeys.RunStateIdle,
            _ => StudioStringKeys.RunStateDone,
        }];

    /// <summary>The state badge next to the title.</summary>
    public string RunBadgeText =>
        _strings[CardState switch
        {
            RunCardState.Running => StudioStringKeys.RunBadgeRunning,
            RunCardState.Idle => StudioStringKeys.RunBadgeIdle,
            RunCardState.Succeeded => StudioStringKeys.RunBadgeDone,
            _ => StudioStringKeys.RunBadgeFailed,
        }];

    /// <summary>Tone key the view maps to colours: idle | running | ok | fail.</summary>
    public string RunBadgeTone => CardState switch
    {
        RunCardState.Running => "running",
        RunCardState.Idle => "idle",
        RunCardState.Succeeded => "ok",
        _ => "fail",
    };

    /// <summary>Launch now / Running… / Relaunch — the mock's single primary button.</summary>
    public string RunButtonLabel =>
        _strings[CardState switch
        {
            RunCardState.Running => StudioStringKeys.RunButtonRunning,
            RunCardState.Idle => StudioStringKeys.RunButtonLaunch,
            _ => StudioStringKeys.RunButtonRelaunch,
        }];

    /// <summary>Localized banner when the CLI is missing; the raw locator detail stays expert.</summary>
    public string? CliBanner => IsBinaryAvailable ? null : _strings[StudioStringKeys.RunCliMissing];

    /// <summary>Whether the technical journal is unfolded (novice folds it by default).</summary>
    public bool IsJournalOpen
    {
        get => _isJournalOpen;
        set => SetProperty(ref _isJournalOpen, value);
    }

    /// <summary>Opens the result — one Explorer window per folder the run could write to.</summary>
    public RelayCommand OpenResultCommand { get; }

    /// <summary>A result folder exists once the run finished and a writable mount is known.</summary>
    public bool CanOpenResult => HasResult && _shellOpener is not null && ResultFolders().Count > 0;

    /// <summary>"Open the result", or "Open the {n} result folders" when the run could write to several.</summary>
    public string OpenResultLabel => ResultFolders() is { Count: > 1 } many
        ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.RunOpenResults], many.Count)
        : _strings[StudioStringKeys.RunOpenResult];

    /// <summary>
    /// The physical folders the run could have written to: every mount **in force** with write
    /// rights — the team's own folders and the per-launch ones first, then the declared entries
    /// kept for this run — each folder once. Read from the effective table rather than from the
    /// three raw lists: since VFS-90 a settings entry may be withdrawn for the run (another
    /// entry of its root was selected), and the folder of an entry the run never mounted is not
    /// a result.
    /// </summary>
    internal IReadOnlyList<string> ResultFolders()
    {
        IEnumerable<string> candidates = Mounts.HasAutoInjection
            ? Mounts.EffectiveMounts
                .Where(m => m.IsMounted)
                .OrderBy(m => m.Origin == MountOrigin.Settings ? 1 : 0)
                .Select(m => m.Value)
            : Mounts.TeamMounts
                .Concat(Mounts.LaunchMounts.Mounts.Select(m => m.MountString))
                .Concat(Mounts.SettingsMounts);

        var folders = new List<string>();
        var seen = new HashSet<string>(
            Orkeon.Domain.FileSystem.PhysicalPathContainment.Comparison == StringComparison.OrdinalIgnoreCase
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        foreach (var mountString in candidates)
        {
            if (MountDefinition.TryParse(mountString, out var mount, out _)
                && mount is { Rights: MountRights.ReadWrite, PhysicalPath.Length: > 0 }
                && seen.Add(MountDefinition.NormalizeFolder(mount.PhysicalPath)))
            {
                folders.Add(mount.PhysicalPath);
            }
        }

        return folders;
    }

    /// <summary>The first folder of <see cref="ResultFolders"/> — where the team's own deliverables land.</summary>
    internal string? ResultFolder()
    {
        var folders = ResultFolders();
        return folders.Count > 0 ? folders[0] : null;
    }

    private void OpenResult()
    {
        // One window per writable folder (owner's request of 2026-09-20): a team writing to
        // /output and /rapports gets both, and a folder the run never mounted gets none.
        foreach (var folder in ResultFolders())
            _shellOpener?.Open(folder);
    }

    /// <summary>
    /// The adopted team's own folder, resolved by <see cref="DeclaredMounts.TeamDirectoryOf"/> —
    /// the screen asks the question, Studio.Core owns the answer.
    /// </summary>
    private string? TeamDirectory() => DeclaredMounts.TeamDirectoryOf(Target.SelectedPath, _directories);

    /// <summary>
    /// Whether one of the team's folders is a real folder outside the team (STUDIO-14, D-12).
    /// <para>
    /// The engine whitelists a <c>--mount</c> base path for the file tools only under
    /// <c>--allow-external-mounts</c>; a team's own <c>./output</c>, resolved under the team
    /// folder — which is the launch's working directory — needs no such flag, a declared folder
    /// elsewhere on the disk does. So the flag follows the sidecar instead of waiting for the
    /// user to find an expert checkbox: it goes on when a team folder reaches outside the team
    /// and off when none does. The checkbox itself stays, for the per-launch mounts.
    /// </para>
    /// </summary>
    private bool ReachesOutsideTheTeam()
    {
        var teamDirectory = TeamDirectory();
        return _team.Mounts.Any(mount => !DeclaredMounts.IsInsideTeam(mount, teamDirectory));
    }

    private void RaiseRunStateChanged()
    {
        OnPropertiesChanged(nameof(RunStateTitle), nameof(RunBadgeText), nameof(RunBadgeTone),
            nameof(RunButtonLabel), nameof(CanOpenResult), nameof(OpenResultLabel));
        OpenResultCommand.RaiseCanExecuteChanged();
    }

    private void OnTargetChanged(object? sender, EventArgs e)
    {
        Options.Target = Target.Target;
        _team = TeamCatalog.DescribeTarget(Target.SelectedPath, _declaredMounts());
        Mounts.SetTeamMounts(_team.ResolvedMounts);
        Mounts.AllowExternalMounts = ReachesOutsideTheTeam();
        OnPropertiesChanged(nameof(TeamHeadline), nameof(TeamMetaLine), nameof(HasTeamCard),
            nameof(UndeclaredTeamFolders), nameof(IsBlockedByUndeclaredFolders), nameof(UndeclaredFoldersMessage),
            nameof(UnknownTeamMountIds), nameof(IsBlockedByUnknownMountIds), nameof(UnknownMountIdsMessage));
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
        // The settings in force decide which team mounts go on the command line at all, so
        // they are refreshed before the arguments are built.
        PublishSettingsMounts();

        // The mount table's appended rows depend on the mount the runner injects ahead of the
        // --mount arguments, which depends on the target and on whether LLM logging is on.
        Mounts.AutoInjection = Target.Target is { } target
            ? MountAutoInjection.For(target, BuildOptions())
            : null;

        // The builder throws on an option the target's shape cannot carry, so the preview is built
        // from the arguments only once the same validation has passed; otherwise the message list is
        // what tells the user why there is nothing to show.
        var arguments = BuildArguments();
        CommandLinePreview = arguments.Count == 0 ? null : CommandLineDisplay.Format(arguments);

        CheckOptions();
    }
}

/// <summary>
/// The seams <see cref="LaunchTabViewModel"/> is built over: the runner that spawns the CLI, the
/// probes it reads the machine through, the stores it persists to, and the UI services it answers
/// on. Every one is optional — left unset, the tab falls back to the real physical collaborator —
/// and every one has an in-memory double in the tests. Grouped into one record so the tab's
/// constructor stays narrow and so a new seam does not move the existing ones.
/// </summary>
public sealed record LaunchTabDependencies
{
    /// <summary>Spawns the co-installed <c>orkeon</c> CLI; defaults to the current machine's.</summary>
    public OrkeonProcessRunner? ProcessRunner { get; init; }

    /// <summary>Tells what a picked path is: a crew file, a script, a directory.</summary>
    public ITargetProbe? TargetProbe { get; init; }

    /// <summary>Reads the disk tree behind the mount panel; defaults to the physical one.</summary>
    public IDirectoryProbe? Directories { get; init; }

    /// <summary>Opens the file and folder dialogs.</summary>
    public IPathPicker? Picker { get; init; }

    /// <summary>Persists the replayable run history; null keeps the run out of any history.</summary>
    public ILaunchHistoryStore? HistoryStore { get; init; }

    /// <summary>Reads the appsettings the launch points at; defaults to the physical store.</summary>
    public IAppSettingsStore? SettingsStore { get; init; }

    /// <summary>Marshals back onto the UI thread; defaults to running the callback inline.</summary>
    public IUiDispatcher? Dispatcher { get; init; }

    /// <summary>The localized strings; defaults to the English set.</summary>
    public IStudioStrings? Strings { get; init; }

    /// <summary>The ORKEON_* environment a given target's model profile rides on.</summary>
    public Func<string, IReadOnlyDictionary<string, string>?>? EnvironmentForTarget { get; init; }

    /// <summary>Opens a result folder in the shell; null hides the open-result button.</summary>
    public IShellOpener? ShellOpener { get; init; }

    /// <summary>Reads the settings' allowed folders live — a snapshot would go stale.</summary>
    public Func<IReadOnlyList<string>>? DeclaredMounts { get; init; }

    /// <summary>The clipboard behind "copy the command"; in-memory when absent (the tests).</summary>
    public IClipboardService? Clipboard { get; init; }

    /// <summary>The clock a run's elapsed time is read on (STUDIO-34); the system's when absent.</summary>
    public TimeProvider? Clock { get; init; }
}
