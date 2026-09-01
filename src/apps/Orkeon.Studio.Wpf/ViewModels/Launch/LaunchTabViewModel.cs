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
    private bool _isJournalOpen;
    private readonly IShellOpener? _shellOpener;
    private TargetDescription _team = new();
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
        IStudioStrings? strings = null,
        Func<string, IReadOnlyDictionary<string, string>?>? environmentForTarget = null,
        IShellOpener? shellOpener = null,
        Func<IReadOnlyList<string>>? declaredMounts = null)
    {
        _environmentForTarget = environmentForTarget;
        _declaredMounts = declaredMounts ?? (() => []);
        _directories = directories ?? PhysicalDirectoryProbe.Instance;
        _runner = processRunner ?? OrkeonProcessRunner.ForCurrentMachine();
        // The run lifecycle is the shared Core session, not a re-implementation: the terminal
        // launcher runs over the very same class, which is what keeps the two in step.
        _session = new RunSession(_runner, historyStore);
        _settingsStore = settingsStore ?? PhysicalAppSettingsStore.Instance;
        _dispatcher = dispatcher ?? ImmediateUiDispatcher.Instance;
        _strings = strings ?? EnglishStudioStrings.Instance;
        _strings.CultureChanged += (_, _) =>
        {
            OnPropertiesChanged(nameof(BinaryStatus), nameof(ValidationSummary),
                nameof(CliBanner), nameof(TeamMetaLine));
            RaiseRunStateChanged();
        };

        _shellOpener = shellOpener;
        Target = new TargetSelectionViewModel(targetProbe, picker, _strings);
        Options = new LaunchOptionsViewModel(picker, _strings);
        Mounts = new LaunchMountsViewModel(directories, picker, _strings);
        Log = new RunLogViewModel(_strings);
        Progress = new RunProgressViewModel(_strings);
        History = new LaunchHistoryViewModel(historyStore, _dispatcher, _strings, _shellOpener);

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
    public Task<ProcessRunResult?> ValidateAsync(CancellationToken cancellationToken = default) =>
        LaunchAsync(validate: true, cancellationToken);

    /// <summary>
    /// Runs the crew for real (spec §5.3). With the dry-run-first option (mock,
    /// expert options), a <c>--validate</c> pass runs first and a failed one stops here —
    /// the dry run is exactly the protection it claims to be.
    /// </summary>
    public async Task<ProcessRunResult?> RunAsync(CancellationToken cancellationToken = default)
    {
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

    // A launch is an invocation of the orkeon CLI, so an absent CLI is a refusal, not a
    // late failure: a live Run button that answers a click with «the tool was not located»
    // is a button that lied about being available. The banner says what is wrong; the
    // commands must agree with it.
    private bool CanLaunch() => IsBinaryAvailable && !IsRunning && Target.IsResolved && !IsBlockedByUndeclaredFolders;

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

    private async Task<ProcessRunResult> ExecuteAsync(
        RunLaunchRequest request,
        bool dryRun,
        CancellationToken cancellationToken)
    {
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
        _team = TeamCatalog.DescribeTarget(Target.SelectedPath);
        Mounts.SetTeamMounts(_team.Mounts);
        OnPropertiesChanged(nameof(TeamHeadline), nameof(TeamMetaLine), nameof(HasTeamCard),
            nameof(UndeclaredTeamFolders), nameof(IsBlockedByUndeclaredFolders), nameof(UndeclaredFoldersMessage));
        RunCommand.RaiseCanExecuteChanged();
        ValidateCommand.RaiseCanExecuteChanged();
        // Replay is gated on the same refusal, so it has to be told when the refusal changes -
        // otherwise the button stays enabled until something else happens to refresh it.
        ReplayCommand.RaiseCanExecuteChanged();
        RefreshPreview();
    }

    /// <summary>Display name of the selected team (sidecar-backed, file name otherwise).</summary>
    public string? TeamHeadline => _team.Name;

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

            if (_team.Description is { Length: > 0 } description)
                parts.Add(description);
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

    /// <summary>Plain-language state of the progress card: ready / running / finished.</summary>
    public string RunStateTitle =>
        _strings[IsRunning ? StudioStringKeys.RunStateRunning
            : HasResult ? StudioStringKeys.RunStateDone
            : StudioStringKeys.RunStateIdle];

    /// <summary>The state badge next to the title.</summary>
    public string RunBadgeText =>
        _strings[IsRunning ? StudioStringKeys.RunBadgeRunning
            : !HasResult ? StudioStringKeys.RunBadgeIdle
            : Outcome == RunOutcome.Success ? StudioStringKeys.RunBadgeDone
            : StudioStringKeys.RunBadgeFailed];

    /// <summary>Tone key the view maps to colours: idle | running | ok | fail.</summary>
    public string RunBadgeTone =>
        IsRunning ? "running" : !HasResult ? "idle" : Outcome == RunOutcome.Success ? "ok" : "fail";

    /// <summary>Launch now / Running… / Relaunch — the mock's single primary button.</summary>
    public string RunButtonLabel =>
        _strings[IsRunning ? StudioStringKeys.RunButtonRunning
            : HasResult ? StudioStringKeys.RunButtonRelaunch
            : StudioStringKeys.RunButtonLaunch];

    /// <summary>Localized banner when the CLI is missing; the raw locator detail stays expert.</summary>
    public string? CliBanner => IsBinaryAvailable ? null : _strings[StudioStringKeys.RunCliMissing];

    /// <summary>Whether the technical journal is unfolded (novice folds it by default).</summary>
    public bool IsJournalOpen
    {
        get => _isJournalOpen;
        set => SetProperty(ref _isJournalOpen, value);
    }

    /// <summary>Opens the result — the physical folder of the first writable mount.</summary>
    public RelayCommand OpenResultCommand { get; private set; } = null!;

    /// <summary>A result folder exists once the run finished and a writable mount is known.</summary>
    public bool CanOpenResult => HasResult && _shellOpener is not null && ResultFolder() is not null;

    /// <summary>The physical directory results land in, parsed from the mount strings.</summary>
    internal string? ResultFolder()
    {
        foreach (var mountString in Mounts.TeamMounts
                     .Concat(Mounts.SettingsMounts)
                     .Concat(Mounts.LaunchMounts.Mounts.Select(m => m.MountString)))
        {
            if (MountDefinition.TryParse(mountString, out var mount, out _)
                && mount is { Rights: MountRights.ReadWrite, PhysicalPath.Length: > 0 })
            {
                return mount.PhysicalPath;
            }
        }

        return null;
    }

    private void OpenResult()
    {
        if (ResultFolder() is { } folder)
            _shellOpener?.Open(folder);
    }

    /// <summary>
    /// The adopted team's own folder, resolved by <see cref="DeclaredMounts.TeamDirectoryOf"/> —
    /// the screen asks the question, Studio.Core owns the answer.
    /// </summary>
    private string? TeamDirectory() => DeclaredMounts.TeamDirectoryOf(Target.SelectedPath, _directories);

    private void RaiseRunStateChanged()
    {
        OnPropertiesChanged(nameof(RunStateTitle), nameof(RunBadgeText), nameof(RunBadgeTone),
            nameof(RunButtonLabel), nameof(CanOpenResult));
        OpenResultCommand.RaiseCanExecuteChanged();
    }

    private void OnTargetChanged(object? sender, EventArgs e)
    {
        Options.Target = Target.Target;
        _team = TeamCatalog.DescribeTarget(Target.SelectedPath);
        Mounts.SetTeamMounts(_team.Mounts);
        OnPropertiesChanged(nameof(TeamHeadline), nameof(TeamMetaLine), nameof(HasTeamCard),
            nameof(UndeclaredTeamFolders), nameof(IsBlockedByUndeclaredFolders), nameof(UndeclaredFoldersMessage));
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
