using System.Globalization;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.ViewModels.Forge;

/// <summary>Payload of <see cref="ForgeTabViewModel.RelaunchRequested"/>: the adopted folder.</summary>
public sealed class ForgeRelaunchEventArgs(string path) : EventArgs
{
    /// <summary>Absolute path of the promoted folder.</summary>
    public string Path { get; } = path;
}

/// <summary>
/// The "Résoudre" screen (UX study, SPEC-ORKEON-FORGE §12): a conversation and a dossier,
/// four milestones, and the start page whose first enemy is the blank page. Owns the
/// <see cref="ForgeClient"/> — every capability here is a projection of the CLI's event
/// stream; a capability absent from the stream does not exist on this screen either.
/// </summary>
public sealed class ForgeTabViewModel : ObservableObject
{
    private readonly ForgeClient _client;
    private readonly IUiDispatcher _dispatcher;
    private readonly IStudioStrings _strings;
    private readonly IPathPicker? _picker;
    private readonly string _workspace;
    private ForgeSessionModel _model = new();
    private string _needText = "";
    private string _statusMessage = "";
    private string _footer = "";
    private bool _hasActiveSession;
    private bool _isEngineRunning;
    private bool _isDetailsOpen;
    private string? _lastStderr;

    /// <summary>Builds the screen; every collaborator is optional so tests inject doubles.</summary>
    public ForgeTabViewModel(
        ForgeClient? client = null,
        IPathPicker? picker = null,
        IUiDispatcher? dispatcher = null,
        IStudioStrings? strings = null,
        string? workspaceDirectory = null,
        Func<string, IReadOnlyList<ForgeSolutionSummary>>? catalog = null)
    {
        _client = client ?? ForgeClient.ForCurrentMachine();
        _picker = picker;
        _dispatcher = dispatcher ?? ImmediateUiDispatcher.Instance;
        _strings = strings ?? EnglishStudioStrings.Instance;
        _workspace = workspaceDirectory ?? Environment.CurrentDirectory;
        var load = catalog ?? ForgeSessionCatalog.List;

        Conversation = new ConversationViewModel(SendUserMessage);
        Dossier = new DossierViewModel(_strings);
        Milestones = new ForgeMilestoneViewModel();
        Solutions = new ForgeSolutionsViewModel(() => load(_workspace));
        RawLog = new RunLogViewModel(_strings);

        StartCommand = new AsyncRelayCommand(
            () => StartSessionAsync(new ForgeStartRequest { Need = NeedText.Trim(), WorkingDirectory = _workspace }),
            () => !IsEngineRunning && NeedText.Trim().Length > 0);
        UseExampleCommand = new RelayCommand(p => NeedText = p as string ?? NeedText);
        ResumeCommand = new AsyncRelayCommand(p => ResumeAsync(p as ForgeSolutionViewModel), _ => !IsEngineRunning);
        RelaunchCommand = new RelayCommand(
            p => { if (p is ForgeSolutionViewModel { PromotedTo: { } path }) RelaunchRequested?.Invoke(this, new ForgeRelaunchEventArgs(path)); });
        DecideCommand = new RelayCommand(p => Decide(p as string), _ => _model.DecisionPending && IsEngineRunning);
        StopCommand = new RelayCommand(() => _client.RequestCancellation(), () => IsEngineRunning);
        StoreCommand = new AsyncRelayCommand(() => PromoteAsync(schedule: null), CanPromote);
        ScheduleCommand = new AsyncRelayCommand(
            () => PromoteAsync($"daily@{Dossier.ScheduleTime.Trim()}"), CanPromote);

        Solutions.Refresh();
    }

    /// <summary>Raised when the user wants an adopted solution in the launcher.</summary>
    public event EventHandler<ForgeRelaunchEventArgs>? RelaunchRequested;

    /// <summary>Raised when a session becomes active — the shell brings the screen forward.</summary>
    public event EventHandler? SessionActivated;

    /// <summary>The conversation column.</summary>
    public ConversationViewModel Conversation { get; }

    /// <summary>The dossier column.</summary>
    public DossierViewModel Dossier { get; }

    /// <summary>The milestone banner.</summary>
    public ForgeMilestoneViewModel Milestones { get; }

    /// <summary>"Mes solutions".</summary>
    public ForgeSolutionsViewModel Solutions { get; }

    /// <summary>Level 3: the raw stream — stderr, stray lines, and every protocol line.</summary>
    public RunLogViewModel RawLog { get; }

    /// <summary>The problem being typed on the start page.</summary>
    public string NeedText
    {
        get => _needText;
        set
        {
            if (SetProperty(ref _needText, value))
                StartCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>The four example problems of the start page (UX study §4.1).</summary>
    public IReadOnlyList<string> Examples =>
    [
        _strings[StudioStringKeys.ForgeExample1],
        _strings[StudioStringKeys.ForgeExample2],
        _strings[StudioStringKeys.ForgeExample3],
        _strings[StudioStringKeys.ForgeExample4],
    ];

    /// <summary>False = start page, true = conversation + dossier.</summary>
    public bool HasActiveSession
    {
        get => _hasActiveSession;
        private set
        {
            if (SetProperty(ref _hasActiveSession, value))
                OnPropertyChanged(nameof(IsStartPage));
        }
    }

    /// <summary>The start page's visibility.</summary>
    public bool IsStartPage => !HasActiveSession;

    /// <summary>Whether the engine child is alive.</summary>
    public bool IsEngineRunning
    {
        get => _isEngineRunning;
        private set
        {
            if (SetProperty(ref _isEngineRunning, value))
            {
                Conversation.CanSend = value;
                StartCommand.RaiseCanExecuteChanged();
                ResumeCommand.RaiseCanExecuteChanged();
                DecideCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
                StoreCommand.RaiseCanExecuteChanged();
                ScheduleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Plain status sentence under the footer (errors land here in clear words).</summary>
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    /// <summary>The footer: solution name and attempt counter — the only permanent machinery.</summary>
    public string Footer { get => _footer; private set => SetProperty(ref _footer, value); }

    /// <summary>Level 3 panel, folded by default, never required (UX study §6).</summary>
    public bool IsDetailsOpen { get => _isDetailsOpen; set => SetProperty(ref _isDetailsOpen, value); }

    /// <summary>Session slug — level 3 only.</summary>
    public string? SessionSlug => _model.Slug;

    /// <summary>Starts a new session from the typed problem.</summary>
    public AsyncRelayCommand StartCommand { get; }

    /// <summary>Fills the need box from an example.</summary>
    public RelayCommand UseExampleCommand { get; }

    /// <summary>Resumes a listed session where it stopped.</summary>
    public AsyncRelayCommand ResumeCommand { get; }

    /// <summary>Hands an adopted solution to the launcher.</summary>
    public RelayCommand RelaunchCommand { get; }

    /// <summary>Sends an arbitration: accept, refine or abort.</summary>
    public RelayCommand DecideCommand { get; }

    /// <summary>Stops the engine; the session stays resumable.</summary>
    public RelayCommand StopCommand { get; }

    /// <summary>Adopt: store the solution in a folder of the user's choice.</summary>
    public AsyncRelayCommand StoreCommand { get; }

    /// <summary>Adopt: store it with a generated daily schedule (installed by the user, never by Studio).</summary>
    public AsyncRelayCommand ScheduleCommand { get; }

    private bool CanPromote() =>
        !IsEngineRunning
        && _model.Promotion is null
        && (_model.Stage is "ready" || string.Equals(_model.FinishedStatus, "ready", StringComparison.Ordinal))
        && _model.Slug is not null
        && _picker is not null;

    private async Task StartSessionAsync(ForgeStartRequest request)
    {
        _model = new ForgeSessionModel();
        Conversation.Clear();
        RawLog.Clear();
        OpenSession();

        // The typed need is the first user turn — echo it like the engine's transcript does.
        if (!string.IsNullOrWhiteSpace(request.Need))
            _model.AddUserMessage(request.Need!);
        Refresh();

        await RunEngineAsync(request).ConfigureAwait(false);
    }

    private async Task ResumeAsync(ForgeSolutionViewModel? solution)
    {
        if (solution is null || !solution.CanResume)
            return;

        _model = new ForgeSessionModel();
        Conversation.Clear();
        RawLog.Clear();

        // The stream never replays the past; the session's artifacts carry it.
        ForgeSessionHydrator.Hydrate(_model, solution.Summary.Directory);
        OpenSession();
        Refresh();

        await RunEngineAsync(new ForgeStartRequest
        {
            ResumeSlug = solution.Slug,
            WorkingDirectory = _workspace,
        }).ConfigureAwait(false);
    }

    private async Task RunEngineAsync(ForgeStartRequest request)
    {
        IsEngineRunning = true;
        _lastStderr = null;
        try
        {
            var result = await _client.RunAsync(request, OnEvent, OnRaw, CancellationToken.None).ConfigureAwait(false);
            _dispatcher.Post(() => FinishRun(result));
        }
        finally
        {
            _dispatcher.Post(() =>
            {
                IsEngineRunning = false;
                Solutions.Refresh();
                Refresh();
            });
        }
    }

    private async Task PromoteAsync(string? schedule)
    {
        var slug = _model.Slug;
        if (slug is null || _picker is null)
            return;

        var folder = _picker.PickFolder(_strings[StudioStringKeys.ForgeStorePickTitle]);
        if (string.IsNullOrWhiteSpace(folder))
            return;

        // The engine refuses a non-empty destination; give the solution its own sub-folder.
        var destination = System.IO.Path.Combine(folder, slug);
        IsEngineRunning = true;
        try
        {
            var result = await _client.PromoteAsync(slug, destination, schedule, _workspace, OnEvent, OnRaw)
                .ConfigureAwait(false);
            _dispatcher.Post(() => FinishRun(result));
        }
        finally
        {
            _dispatcher.Post(() =>
            {
                IsEngineRunning = false;
                Solutions.Refresh();
                Refresh();
            });
        }
    }

    private void OpenSession()
    {
        HasActiveSession = true;
        SessionActivated?.Invoke(this, EventArgs.Empty);
    }

    private void OnEvent(ForgeEvent forgeEvent) => _dispatcher.Post(() =>
    {
        _model.Feed(forgeEvent);
        RawLog.AppendNotice(forgeEvent.Root.GetRawText());
        Refresh();
    });

    private void OnRaw(ProcessOutputLine line) => _dispatcher.Post(() =>
    {
        if (line.Channel == ProcessOutputChannel.StandardError && !string.IsNullOrWhiteSpace(line.Text))
            _lastStderr = line.Text;
        RawLog.Append(line);
    });

    private void FinishRun(ProcessRunResult result)
    {
        if (result.Outcome == RunOutcome.NotStarted)
        {
            StatusMessage = result.Description;
            return;
        }

        // The engine's refusals (no LLM, bad session) speak on stderr, not in events:
        // surface the sentence instead of a bare exit code.
        if (result.ExitCode != 0 && _model.FinishedStatus is null && _lastStderr is { } stderr)
            StatusMessage = stderr;
    }

    private bool SendUserMessage(string text)
    {
        if (!_client.SendMessage(text))
            return false;

        _model.AddUserMessage(text);
        Refresh();
        return true;
    }

    private void Decide(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _client.SendDecision(value!);
    }

    private void Refresh()
    {
        Conversation.Sync(_model.Messages);
        Milestones.Current = _model.Milestone;
        Dossier.Update(_model, IsEngineRunning);
        DecideCommand.RaiseCanExecuteChanged();
        StoreCommand.RaiseCanExecuteChanged();
        ScheduleCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(SessionSlug));

        var title = _model.Title ?? _model.Slug ?? "";
        var attempt = string.Format(
            CultureInfo.CurrentCulture, _strings[StudioStringKeys.ForgeAttempt], _model.Iteration);
        Footer = title.Length > 0 ? $"{title} · {attempt}" : attempt;

        StatusMessage = _model.FinishedStatus switch
        {
            "ready" => _strings[StudioStringKeys.ForgeStatusReady],
            "failed" => _strings[StudioStringKeys.ForgeStatusFailed],
            "abandoned" or "paused" => _strings[StudioStringKeys.ForgeStatusStopped],
            _ => StatusMessage,
        };
    }
}
