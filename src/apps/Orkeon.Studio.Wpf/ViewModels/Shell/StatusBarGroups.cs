using System.ComponentModel;
using System.Globalization;
using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Run;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.ViewModels.Shell;

/// <summary>
/// One launcher's group on the status bar — the Run screen's or the Test screen's (STUDIO-34).
/// <para>
/// It is on the bar exactly while the launcher's child process lives, and everything it says is
/// read off that run's progress model (STUDIO-30): the state, the meters the run reported, the
/// tools and delegations at work. A segment the run did not measure is null — the view shows
/// nothing for it, never a zero — and during a run the model named is the one the meter
/// reported, not a profile the bar supposed.
/// </para>
/// </summary>
public sealed class StatusBarRunGroupViewModel : ObservableObject
{
    private readonly LaunchTabViewModel? _launcher;
    private readonly IStudioStrings _strings;
    private readonly Func<bool> _isExpert;
    private RunProgressModel _model = new();
    private bool _isActive;
    private string? _team;

    internal StatusBarRunGroupViewModel(
        StatusBarActivity activity,
        LaunchTabViewModel? launcher,
        IStudioStrings strings,
        Func<bool> isExpert,
        Action<StatusBarActivity> open)
    {
        Activity = activity;
        _launcher = launcher;
        _strings = strings;
        _isExpert = isExpert;
        OpenCommand = new RelayCommand(() => open(activity), CanOpen);

        if (launcher is null)
            return;

        Watch(launcher.Progress.Model);
        launcher.PropertyChanged += OnLauncherChanged;
        launcher.Progress.PropertyChanged += OnProgressChanged;
    }

    private enum RunState
    {
        Running,
        Waiting,
        Succeeded,
        Failed,
    }

    /// <summary>Which launcher this group reports on.</summary>
    public StatusBarActivity Activity { get; }

    /// <summary>Whether the launcher's child process is alive: the group is on the bar exactly that long.</summary>
    public bool IsActive
    {
        get => _isActive;
        private set => SetProperty(ref _isActive, value);
    }

    /// <summary>running | waiting | ok | fail — the tone the view maps to a colour.</summary>
    public string Tone => State switch
    {
        RunState.Waiting => "waiting",
        RunState.Succeeded => "ok",
        RunState.Failed => "fail",
        _ => "running",
    };

    /// <summary>
    /// The state in words: running, waiting for an answer, or — between the run's own end and
    /// the process exit — succeeded or failed.
    /// </summary>
    public string StateText => _strings[State switch
    {
        RunState.Waiting => StudioStringKeys.StatusBarWaiting,
        RunState.Succeeded => StudioStringKeys.RunBadgeDone,
        RunState.Failed => StudioStringKeys.RunBadgeFailed,
        _ => StudioStringKeys.RunBadgeRunning,
    }];

    /// <summary>Whether the run waits on this window — a question, or an agent's request for a reply.</summary>
    public bool IsWaiting => State == RunState.Waiting;

    /// <summary>What went up to the models (↑), grouped for reading; null until the meter carries it.</summary>
    public string? TokensUp => StatusBarText.Count(_model.Cost?.PromptTokens);

    /// <summary>What came back (↓); null until the meter carries it.</summary>
    public string? TokensDown => StatusBarText.Count(_model.Cost?.CompletionTokens);

    /// <summary>Whether ↑ is on the bar — in both modes, once measured.</summary>
    public bool ShowsTokensUp => TokensUp is not null;

    /// <summary>Whether ↓ is on the bar — in both modes, once measured.</summary>
    public bool ShowsTokensDown => TokensDown is not null;

    /// <summary>
    /// The team the run is for, read when the run started and kept: the launcher can be aimed at
    /// another team while this one runs, and the group goes on naming the team that runs.
    /// </summary>
    public string? Team => _team;

    /// <summary>Whose turn it is — the tasks started and not finished, by agent; null when the run announced none.</summary>
    public string? CurrentTask => StatusBarText.Join(
        _model.RunningTasks.Select(task => task.AgentRole ?? task.TaskId), ", ");

    /// <summary>How long the run has gone, moved by the bar's beat between events; null without a start to count from.</summary>
    public string? Duration => _model.Elapsed is { } elapsed
        ? UsageMetricsFormatter.Duration((long)elapsed.TotalMilliseconds, _strings, CultureInfo.CurrentCulture)
        : null;

    /// <summary>The prompt-cache chip, in the one recipe every usage chip shares; null while unmeasured.</summary>
    public string? Cache => _model.Cost is { CacheHitTokens: { } hit, CacheMissTokens: { } miss }
        && UsageMetricsFormatter.Chips(null, hit, miss, null, _strings, CultureInfo.CurrentCulture) is [var chip]
        ? chip
        : null;

    /// <summary>What the vendor billed, as billed (DD-1: a real cost, never an estimate); null when it billed nothing.</summary>
    public string? BilledCost => _model.Cost is { Amount: { } amount } cost
        ? StatusBarText.Join([amount.ToString("0.######", CultureInfo.CurrentCulture), cost.Currency], " ")
        : null;

    /// <summary>How many tools are at work, and the first of them; null when none is.</summary>
    public string? Tools => _model.ActiveTools.Count > 0
        ? string.Format(
            CultureInfo.CurrentCulture,
            _strings[StudioStringKeys.StatusBarTools],
            _model.ActiveTools.Count,
            _model.ActiveTools[0].ToolName)
        : null;

    /// <summary>Every tool at work, oldest first, each with the run's own clock at its call — the list on hover.</summary>
    public string? ToolsDetail => _model.ActiveTools.Count > 0
        ? StatusBarText.Join(
            [_strings[StudioStringKeys.StatusBarToolsTitle], .. _model.ActiveTools.Select(DescribeTool)],
            Environment.NewLine)
        : null;

    /// <summary>How many delegations are under way; null when none is.</summary>
    public string? Delegations => _model.ActiveDelegations.Count > 0
        ? string.Format(
            CultureInfo.CurrentCulture,
            _strings[StudioStringKeys.StatusBarDelegations],
            _model.ActiveDelegations.Count)
        : null;

    /// <summary>Whom the work went to, one line per delegation — the list on hover.</summary>
    public string? DelegationsDetail => _model.ActiveDelegations.Count > 0
        ? StatusBarText.Join(
            [
                _strings[StudioStringKeys.StatusBarDelegationsTitle],
                .. _model.ActiveDelegations.Select(delegation => $"→ {delegation.ToRole ?? "—"}"),
            ],
            Environment.NewLine)
        : null;

    /// <summary>«provider · model» as the meter reported them (STUDIO-29); null until it did.</summary>
    public string? ReportedModel => _model.Cost is { } cost
        ? StatusBarText.Join([cost.Provider, cost.Model], StatusBarText.Separator)
        : null;

    /// <summary>
    /// The expert's segments, in the order D-02 lists them, as one line the bar can trim when the
    /// window is narrow; the whole of it, lists included, is <see cref="DetailsTip"/>.
    /// </summary>
    public string Details => StatusBarText.Join(
        [Team, CurrentTask, Duration, Cache, BilledCost, Tools, Delegations, ReportedModel],
        StatusBarText.Separator) ?? string.Empty;

    /// <summary>The same segments one per line, with the tool and delegation lists written out.</summary>
    public string DetailsTip => StatusBarText.Join(
        [
            Team, CurrentTask, Duration, Cache,
            BilledCost is { } billed
                ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.StatusBarCostBilled], billed)
                : null,
            ToolsDetail, DelegationsDetail, ReportedModel,
        ],
        Environment.NewLine) ?? string.Empty;

    /// <summary>The expert's segments show in expert mode, when anything was measured (D-03).</summary>
    public bool ShowsDetails => _isExpert() && Details.Length > 0;

    /// <summary>Asks the shell for this activity's screen (D-04).</summary>
    public RelayCommand OpenCommand { get; }

    private RunState State
    {
        get
        {
            if (_model.Finished)
                return _model.Success == true ? RunState.Succeeded : RunState.Failed;

            return _model.IsWaitingForAnswer ? RunState.Waiting : RunState.Running;
        }
    }

    /// <summary>The beat: the elapsed time moved without an event.</summary>
    internal void RefreshClock() => OnPropertiesChanged(nameof(Duration), nameof(Details), nameof(DetailsTip));

    /// <summary>The window switched between novice and expert.</summary>
    internal void RefreshMode()
    {
        OnPropertyChanged(nameof(ShowsDetails));
        OpenCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Everything the group says, said again — a new run, a new event, a new language.</summary>
    internal void RefreshAll() => OnPropertiesChanged(
        nameof(Tone), nameof(StateText), nameof(IsWaiting),
        nameof(TokensUp), nameof(TokensDown), nameof(ShowsTokensUp), nameof(ShowsTokensDown),
        nameof(Team), nameof(CurrentTask), nameof(Duration), nameof(Cache), nameof(BilledCost),
        nameof(Tools), nameof(ToolsDetail), nameof(Delegations), nameof(DelegationsDetail),
        nameof(ReportedModel), nameof(Details), nameof(DetailsTip), nameof(ShowsDetails));

    /// <summary>
    /// The Test screen is the expert's: a novice has no screen to land on, so the group stays on
    /// the bar — nothing is hidden — and does not answer the click rather than go nowhere.
    /// </summary>
    private bool CanOpen() => Activity != StatusBarActivity.Test || _isExpert();

    /// <summary>«pdf_reader · since 10:31:02», local time, from the run's own clock; the bare name when the call carried none.</summary>
    private string DescribeTool(RunToolInFlight tool)
    {
        if (tool.StartedAt is not { } started)
            return tool.ToolName;

        var since = string.Format(
            CultureInfo.CurrentCulture,
            _strings[StudioStringKeys.RunProgressTaskSince],
            started.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        return $"{tool.ToolName}{StatusBarText.Separator}{since}";
    }

    private void OnLauncherChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_launcher is null || e.PropertyName != nameof(LaunchTabViewModel.IsRunning))
            return;

        if (_launcher.IsRunning)
            _team = _launcher.TeamHeadline;

        IsActive = _launcher.IsRunning;
        RefreshAll();
    }

    /// <summary>Every launch resets the panel onto a fresh model: the group follows it there.</summary>
    private void OnProgressChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_launcher is null || e.PropertyName != nameof(RunProgressViewModel.Model))
            return;

        Watch(_launcher.Progress.Model);
        RefreshAll();
    }

    private void Watch(RunProgressModel model)
    {
        _model.Changed -= OnModelChanged;
        _model = model;
        _model.Changed += OnModelChanged;
    }

    private void OnModelChanged(object? sender, RunProgressChangedEventArgs e)
    {
        // A delta arrives per token, and moves nothing this group shows.
        if (string.Equals(e.Kind, RunEventKinds.LlmDelta, StringComparison.Ordinal))
            return;

        RefreshAll();
    }
}

/// <summary>
/// The assistant's group on the status bar, while it composes or tries a team (STUDIO-34).
/// <para>
/// It reads the wizard's progress card, which already reads the engine's stream: the stage in
/// the card's own words, the two meters, and what the session's token allowance has left. It
/// is on the bar exactly while the engine process lives — working, or waiting on the user.
/// </para>
/// </summary>
public sealed class StatusBarAtelierGroupViewModel : ObservableObject
{
    private readonly ComposeProgressViewModel? _progress;
    private readonly IStudioStrings _strings;
    private readonly Func<bool> _isExpert;
    private bool _isActive;

    internal StatusBarAtelierGroupViewModel(
        ComposeProgressViewModel? progress,
        IStudioStrings strings,
        Func<bool> isExpert,
        Action<StatusBarActivity> open)
    {
        _progress = progress;
        _strings = strings;
        _isExpert = isExpert;
        OpenCommand = new RelayCommand(() => open(StatusBarActivity.Atelier));

        if (progress is null)
            return;

        _isActive = progress.IsEngineRunning;
        progress.PropertyChanged += OnProgressChanged;
    }

    /// <summary>Whether the engine process lives: the group is on the bar exactly that long.</summary>
    public bool IsActive
    {
        get => _isActive;
        private set => SetProperty(ref _isActive, value);
    }

    /// <summary>The stage in the card's own words — or «I am waiting for your answer», which outranks it.</summary>
    public string Stage => _progress?.Title ?? string.Empty;

    /// <summary>Whether the engine waits on the user rather than on itself.</summary>
    public bool IsWaiting => _progress?.IsWaiting == true;

    /// <summary>running | waiting — the tone the view maps to a colour.</summary>
    public string Tone => IsWaiting ? "waiting" : "running";

    /// <summary>What went up to the models (↑); null while nothing was spent.</summary>
    public string? TokensUp => _progress?.TokensUp is { Length: > 0 } up ? up : null;

    /// <summary>What came back (↓); null while nothing was spent.</summary>
    public string? TokensDown => _progress?.TokensDown is { Length: > 0 } down ? down : null;

    /// <summary>Whether ↑ is on the bar — in both modes, once measured.</summary>
    public bool ShowsTokensUp => TokensUp is not null;

    /// <summary>Whether ↓ is on the bar — in both modes, once measured.</summary>
    public bool ShowsTokensDown => TokensDown is not null;

    /// <summary>Whether the figures are the runtime's approximation — the bar marks them «≈», like the card.</summary>
    public bool TokensEstimated => _progress?.TokensEstimated == true;

    /// <summary>What the session's allowance has left; null for a session with no cap.</summary>
    public string? BudgetLeft => _progress?.TokensRemaining is { } left
        ? string.Format(
            CultureInfo.CurrentCulture,
            _strings[StudioStringKeys.StatusBarBudgetLeft],
            left.ToString("N0", CultureInfo.CurrentCulture))
        : null;

    /// <summary>The remaining budget is the expert's (D-03), when the session has one.</summary>
    public bool ShowsBudgetLeft => _isExpert() && BudgetLeft is not null;

    /// <summary>Asks the shell for the assistant's screen (D-04).</summary>
    public RelayCommand OpenCommand { get; }

    /// <summary>The window switched between novice and expert.</summary>
    internal void RefreshMode() => OnPropertyChanged(nameof(ShowsBudgetLeft));

    /// <summary>Everything the group formats itself, said again in a new language.</summary>
    internal void RefreshAll() => OnPropertiesChanged(
        nameof(Stage), nameof(TokensUp), nameof(TokensDown), nameof(BudgetLeft), nameof(ShowsBudgetLeft));

    private void OnProgressChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ComposeProgressViewModel.IsEngineRunning):
                IsActive = _progress?.IsEngineRunning == true;
                break;
            case nameof(ComposeProgressViewModel.Title):
                OnPropertyChanged(nameof(Stage));
                break;
            case nameof(ComposeProgressViewModel.IsWaiting):
                OnPropertiesChanged(nameof(IsWaiting), nameof(Tone));
                break;
            case nameof(ComposeProgressViewModel.TokensUp):
            case nameof(ComposeProgressViewModel.TokensDown):
            case nameof(ComposeProgressViewModel.TokensEstimated):
                OnPropertiesChanged(
                    nameof(TokensUp), nameof(TokensDown), nameof(ShowsTokensUp), nameof(ShowsTokensDown),
                    nameof(TokensEstimated));
                break;
            case nameof(ComposeProgressViewModel.TokensRemaining):
                OnPropertiesChanged(nameof(BudgetLeft), nameof(ShowsBudgetLeft));
                break;
        }
    }
}

/// <summary>The few text rules the bar's segments share.</summary>
internal static class StatusBarText
{
    /// <summary>The joiner Studio puts between the parts of one line.</summary>
    public const string Separator = " · ";

    /// <summary>The parts that say something, joined; null when none does — an absent segment, not an empty one.</summary>
    public static string? Join(IEnumerable<string?> parts, string separator)
    {
        var present = parts.Where(part => !string.IsNullOrWhiteSpace(part)).ToList();
        return present.Count > 0 ? string.Join(separator, present) : null;
    }

    /// <summary>A token count grouped for reading; null when unmeasured.</summary>
    public static string? Count(long? tokens) =>
        tokens is { } count ? count.ToString("N0", CultureInfo.CurrentCulture) : null;
}
