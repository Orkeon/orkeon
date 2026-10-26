using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.ViewModels.Launch;

/// <summary>One folder of the « Allowed folders » section of the options tab: the team's, as this launch mounts it.</summary>
/// <param name="VirtualPath">The path the agents address.</param>
/// <param name="RightsLabel">« Read » or « Read and write ».</param>
/// <param name="IsWritable">Whether the agents may write there.</param>
/// <param name="PhysicalPath">The folder on this machine; empty when the entry names none.</param>
public sealed record RunFolderRow(string VirtualPath, string RightsLabel, bool IsWritable, string PhysicalPath);

/// <summary>
/// One agent's column of the run tab (refonte lot 12): there before the launch, from the crew's
/// files, and filled by the run's own events — the task it is on, then how it ended.
/// </summary>
/// <param name="Name">The agent's role.</param>
/// <param name="State">waiting | running | done | failed — the tone the view maps to its bar and its mark.</param>
/// <param name="StatusText">The state in words.</param>
/// <param name="Detail">Tool calls · tokens · duration of its last task; empty before it ends one.</param>
public sealed record RunAgentLane(string Name, string State, string StatusText, string Detail);

/// <summary>
/// The Launch screen in four tabs (refonte lot 12, R14): « Team », « Options », « Command »
/// (Expert) and « Run », with an action bar under them. Each tab has its state mark and its
/// state subtitle, named after the view's <c>StepTabState</c> (no WPF type here); the launch
/// opens the run tab, and while a run lives the other tabs stay open to reading but not to
/// change. Nothing the engine receives changes: these are readings of the launcher's state.
/// </summary>
public sealed partial class LaunchTabViewModel
{
    /// <summary>The « Team » tab.</summary>
    public const string TeamTab = "team";

    /// <summary>The « Options » tab.</summary>
    public const string OptionsTab = "options";

    /// <summary>The « Command » tab — Expert only.</summary>
    public const string CommandTab = "command";

    /// <summary>The « Run » tab.</summary>
    public const string RunTab = "run";

    private const int CommandSubtitleLength = 44;

    private string _activeTab = TeamTab;
    private bool _isExpertMode;
    private bool _isJournalEnlarged;
    private IReadOnlyList<string> _crewRoles = [];

    /// <summary>The tab on screen; the command tab is the expert's, a novice lands on the options.</summary>
    public string ActiveTab
    {
        get => _activeTab;
        set
        {
            var tab = value switch
            {
                TeamTab or OptionsTab or RunTab => value,
                CommandTab => _isExpertMode ? CommandTab : OptionsTab,
                _ => TeamTab,
            };
            if (SetProperty(ref _activeTab, tab))
                OnPropertiesChanged(nameof(IsTeamTab), nameof(IsOptionsTab), nameof(IsCommandTab), nameof(IsRunTab));
        }
    }

    /// <summary>Whether the team tab is on screen.</summary>
    public bool IsTeamTab => _activeTab == TeamTab;

    /// <summary>Whether the options tab is on screen.</summary>
    public bool IsOptionsTab => _activeTab == OptionsTab;

    /// <summary>Whether the command tab is on screen.</summary>
    public bool IsCommandTab => _activeTab == CommandTab;

    /// <summary>Whether the run tab is on screen.</summary>
    public bool IsRunTab => _activeTab == RunTab;

    /// <summary>The window's mode, set by the shell: the command tab and its footer step are the expert's.</summary>
    public bool IsExpertMode
    {
        get => _isExpertMode;
        set
        {
            if (!SetProperty(ref _isExpertMode, value))
                return;

            if (!value && _activeTab == CommandTab)
                ActiveTab = OptionsTab;
            OnPropertyChanged(nameof(OptionsNextLabel));
        }
    }

    /// <summary>« See the options » (the arrow is the button's icon, C05), the team pane's footer.</summary>
    public RelayCommand ShowOptionsTabCommand { get; private set; } = null!;

    /// <summary>« See the command » (Expert) or « See the run » (Novice), the options pane's footer.</summary>
    public RelayCommand OptionsNextCommand { get; private set; } = null!;

    /// <summary>« See the run », the command pane's footer.</summary>
    public RelayCommand ShowRunTabCommand { get; private set; } = null!;

    /// <summary>« ← Back to the team », the run pane's footer, and « Change team » of the fiche.</summary>
    public RelayCommand ShowTeamTabCommand { get; private set; } = null!;

    /// <summary>The label of <see cref="OptionsNextCommand"/>.</summary>
    public string OptionsNextLabel => _strings[_isExpertMode ? StudioStringKeys.RunSeeCommand : StudioStringKeys.RunSeeRun];

    // ── The team tab ──

    /// <summary>A check once a team is chosen and can run, the amber dot while one is to choose or a refusal holds.</summary>
    public string TeamTabState => !HasTeamCard ? "Expected" : IsTeamRefused ? "Warning" : "Done";

    /// <summary>The team's name, « To choose », or that it cannot be launched.</summary>
    public string TeamTabSubtitle =>
        !HasTeamCard ? _strings[StudioStringKeys.RunTabTeamToChoose]
        : IsTeamRefused ? _strings.Format(StudioStringKeys.RunTabTeamBlocked, TeamHeadline ?? string.Empty)
        : TeamHeadline ?? string.Empty;

    /// <summary>
    /// The team pane's footer: « Team ready: 3 agents, 2 declared folders. » when the crew's files
    /// name its agents, « Team ready: X » when they do not (a scripted team), or that none is chosen.
    /// </summary>
    public string TeamFooter => !HasTeamCard
        ? _strings[StudioStringKeys.RunTeamNotChosen]
        : _crewRoles.Count > 0
            ? _strings.Format(StudioStringKeys.RunTeamReadyCounts, _crewRoles.Count, FolderRows.Count)
            : _strings.Format(StudioStringKeys.RunTeamReady, TeamHeadline ?? string.Empty);

    /// <summary>
    /// The fiche's mono line: the catalog's « 3 agents · reads … · writes to … », then the model
    /// this run takes and the team's type — the parts that can be asserted, nothing else.
    /// </summary>
    public string? TeamFicheLine
    {
        get
        {
            var parts = new List<string>(3);
            if (TeamMetaLine is { Length: > 0 } meta)
                parts.Add(meta);
            parts.Add(RunLlmLabelOf(RunLlmChoice));
            if (Target.KindDisplay is { Length: > 0 } kind)
                parts.Add(kind);
            return parts.Count > 0 ? string.Join(" · ", parts) : null;
        }
    }

    /// <summary>
    /// The team-type note, once: what « orkeon run » asks of a multi-file team folder when that
    /// applies, else the detected shape.
    /// </summary>
    public string TargetTypeNote => Target.DirectoryRunNotice ?? Target.StatusDisplay;

    // ── The options tab ──

    /// <summary>The options keep their marks to themselves: an empty circle.</summary>
    public static string OptionsTabState => "Empty";

    /// <summary>« Normal · progress »: the verbosity and the display.</summary>
    public string OptionsTabSubtitle => string.Join(" · ", VerbosityLabel(Options.Verbosity), DisplayLabel.ToLower(CultureInfo.CurrentCulture));

    /// <summary>The options pane's footer: the verbosity, the display and the folders the launch mounts.</summary>
    public string OptionsFooter => _strings.Format(
        StudioStringKeys.RunOptionsFooter,
        VerbosityLabel(Options.Verbosity),
        DisplayLabel.ToLower(CultureInfo.CurrentCulture),
        FolderRows.Count + Mounts.LaunchMounts.Mounts.Count);

    /// <summary>The display of the run: its progress in words, or the raw output of the engine.</summary>
    public string DisplayLabel => _strings[Options.WatchProgress ? StudioStringKeys.RunDisplayProgress : StudioStringKeys.RunDisplayRaw];

    /// <summary>« Progress » of the display switch.</summary>
    public RelayCommand ShowProgressDisplayCommand { get; private set; } = null!;

    /// <summary>« Raw output » of the display switch.</summary>
    public RelayCommand ShowRawDisplayCommand { get; private set; } = null!;

    /// <summary>The team's folders, as this launch mounts them.</summary>
    public IReadOnlyList<RunFolderRow> FolderRows
    {
        get
        {
            var rows = new List<RunFolderRow>();
            var definitions = Mounts.ResolvedTeamMounts
                .Select(mount => MountDefinition.TryParse(mount.Effective, out var parsed, out _) ? parsed : null)
                .OfType<MountDefinition>();
            foreach (var definition in definitions)
            {
                var writable = definition.Rights != MountRights.ReadOnly;
                rows.Add(new(
                    definition.VirtualPath,
                    _strings[writable ? StudioStringKeys.RunRightsReadWrite : StudioStringKeys.RunRightsRead],
                    writable,
                    definition.PhysicalPath ?? string.Empty));
            }

            return rows;
        }
    }

    /// <summary>« Allowed folders — 3 folders of the team, nothing added ».</summary>
    public string FoldersSectionTitle
    {
        get
        {
            var added = Mounts.LaunchMounts.Mounts.Count;
            return _strings.Format(
                StudioStringKeys.RunFoldersSection,
                FolderRows.Count,
                added == 0 ? _strings[StudioStringKeys.RunFoldersAddedNone] : _strings.Format(StudioStringKeys.RunFoldersAdded, added));
        }
    }

    // ── The command tab ──

    /// <summary>The command keeps its marks to itself: an empty circle.</summary>
    public static string CommandTabState => "Empty";

    /// <summary>The start of the command, the rest folded behind an ellipsis.</summary>
    public string CommandTabSubtitle => CommandLinePreview is { Length: > 0 } command
        ? command.Length <= CommandSubtitleLength ? command : command[..CommandSubtitleLength].TrimEnd() + "…"
        : string.Empty;

    /// <summary>The R6 row « Engine »: where the CLI was found, or that it was not.</summary>
    public string EngineRow => BinaryLocation is { Found: true, Path: { Length: > 0 } path } ? path : _strings[StudioStringKeys.RunEngineMissing];

    /// <summary>The R6 row « Mounts »: how many folders the launch lays.</summary>
    public string MountsRow => _strings.Format(StudioStringKeys.RunMountsCount, Mounts.ToMountArguments().Count + Mounts.ToMountIdArguments().Count);

    /// <summary>The R6 row « Mode »: « Trial » or « Real ».</summary>
    public string ModeRow => _strings[IsTrial ? StudioStringKeys.RunModeTrial : StudioStringKeys.RunModeReal];

    // ── The run tab ──

    /// <summary>Empty at rest, turning while the run lives, a check on success, the amber dot on a failure or a stop.</summary>
    public string RunTabState => CardState switch
    {
        RunCardState.Running => "Working",
        RunCardState.Succeeded => "Done",
        RunCardState.Failed or RunCardState.Cancelled => "Warning",
        _ => "Empty",
    };

    /// <summary>« Waiting · nothing to report », « Running · 2 min 05 s », « Succeeded · 4 min 12 s », « Failed », « Cancelled ».</summary>
    public string RunTabSubtitle => CardState switch
    {
        RunCardState.Running => _strings.Format(StudioStringKeys.RunTabRunRunning, RunDuration ?? "…"),
        RunCardState.Succeeded => RunDuration is { } took
            ? _strings.Format(StudioStringKeys.RunTabRunSucceeded, took)
            : _strings[StudioStringKeys.RunPaneSucceeded],
        RunCardState.Failed => _strings[StudioStringKeys.RunTabRunFailed],
        RunCardState.Cancelled => _strings[StudioStringKeys.HistStatusCancelled],
        _ => _strings[StudioStringKeys.RunTabRunIdle],
    };

    /// <summary>The run pane's title: « Ready to work », running, « Finished », failed, cancelled — the badge says the outcome.</summary>
    public string RunPaneTitle => _strings[CardState switch
    {
        RunCardState.Running => StudioStringKeys.RunPaneRunning,
        RunCardState.Succeeded => StudioStringKeys.RunPaneDone,
        RunCardState.Failed => StudioStringKeys.RunTabRunFailed,
        RunCardState.Cancelled => StudioStringKeys.HistStatusCancelled,
        _ => StudioStringKeys.RunStateIdle,
    }];

    /// <summary>The sentence under the title: what is going on, how it ended, or what the pane will show.</summary>
    public string RunPaneContext => CardState switch
    {
        RunCardState.Running => Progress.Summary,
        RunCardState.Idle => _strings[StudioStringKeys.RunPaneIdleContext],
        _ => ExitDescription ?? Progress.Summary,
    };

    /// <summary>How long the run has gone, or went; null without a start to count from.</summary>
    public string? RunDuration => Progress.Model.Elapsed is { } elapsed
        ? UsageMetricsFormatter.Duration((long)elapsed.TotalMilliseconds, _strings, CultureInfo.CurrentCulture)
        : null;

    /// <summary>One column per agent: the crew's, then any the run announced that the files did not.</summary>
    public ObservableCollection<RunAgentLane> AgentLanes { get; } = [];

    /// <summary>Whether the figures row shows: once a run started.</summary>
    public bool HasRunFigures => IsRunning || HasResult;

    /// <summary>What went up to the models; « — » until measured.</summary>
    public string SentFigure => StatusBarText.Count(Progress.Model.Cost?.PromptTokens) ?? "—";

    /// <summary>What came back; « — » until measured.</summary>
    public string ReceivedFigure => StatusBarText.Count(Progress.Model.Cost?.CompletionTokens) ?? "—";

    /// <summary>The model the meter named; « — » until it named one.</summary>
    public string ModelFigure => Progress.CostModel ?? "—";

    /// <summary>Where the team's deliverables land, as the agents address it: the first writable folder.</summary>
    public string? ResultVirtualPath
    {
        get
        {
            foreach (var mountString in Mounts.TeamMounts.Concat(Mounts.LaunchMounts.Mounts.Select(m => m.MountString)))
            {
                if (MountDefinition.TryParse(mountString, out var mount, out _) && mount is not null && mount.Rights != MountRights.ReadOnly)
                    return mount.VirtualPath;
            }

            return null;
        }
    }

    /// <summary>The figure « Result in »: <see cref="ResultVirtualPath"/>, or « — ».</summary>
    public string ResultFigure => ResultVirtualPath ?? "—";

    /// <summary>« Technical journal · 214 lines ».</summary>
    public string JournalTitle => _strings.Format(StudioStringKeys.RunJournalCount, Log.Lines.Count);

    /// <summary>« Enlarge »: the journal takes the height of a long reading.</summary>
    public bool IsJournalEnlarged
    {
        get => _isJournalEnlarged;
        set
        {
            if (SetProperty(ref _isJournalEnlarged, value))
                OnPropertiesChanged(nameof(JournalEnlargeLabel), nameof(IsJournalTall));
        }
    }

    /// <summary>« Enlarge » / « Reduce » of the journal.</summary>
    public RelayCommand ToggleJournalSizeCommand { get; private set; } = null!;

    /// <summary>Whether the journal stretches: enlarged, or while the run writes to it.</summary>
    public bool IsJournalTall => _isJournalEnlarged || IsRunning;

    /// <summary>« Enlarge » or « Reduce ».</summary>
    public string JournalEnlargeLabel => _strings[_isJournalEnlarged ? StudioStringKeys.RunJournalReduce : StudioStringKeys.RunJournalEnlarge];

    /// <summary>The run pane's footer: nothing running, « Running for 2 min 05 s. », or finished.</summary>
    public string RunFooter => CardState switch
    {
        RunCardState.Running => RunDuration is { } since
            ? _strings.Format(StudioStringKeys.RunRunningSince, since)
            : _strings[StudioStringKeys.RunRunningFooter],
        RunCardState.Idle => _strings[StudioStringKeys.RunNoRunFooter],
        _ => _strings[StudioStringKeys.RunDoneFooter],
    };

    // ── The action bar ──

    /// <summary>Whether the panes take changes: not while a run lives (« Can be changed after the run »).</summary>
    public bool IsEditable => !IsRunning;

    /// <summary>ok | danger | running — the dot of the action bar.</summary>
    public string LaunchReadinessTone => IsRunning ? "running" : LaunchBlockedReason is null ? "ok" : "danger";

    /// <summary>
    /// The action bar's sentence: « Ready to launch in Real mode. The result will go to /output. »,
    /// why the launch is refused, or « team · running · 2 min 05 s ».
    /// </summary>
    public string LaunchReadinessText
    {
        get
        {
            if (IsRunning)
                return _strings.Format(StudioStringKeys.RunRunningLine, TeamHeadline ?? Target.SelectedPath, RunDuration ?? "…");
            if (LaunchBlockedReason is { } reason)
                return reason;

            var ready = _strings[IsTrial ? StudioStringKeys.RunReadyTrial : StudioStringKeys.RunReadyReal];
            return ResultVirtualPath is { } result ? ready + " " + _strings.Format(StudioStringKeys.RunReadyResult, result) : ready;
        }
    }

    /// <summary>Why « Launch now » is greyed — the tooltip of the button —; null when it is not.</summary>
    public string? LaunchBlockedReason =>
        !IsBinaryAvailable ? _strings[StudioStringKeys.RunBlockedNoCli]
        : !Target.IsResolved ? _strings[StudioStringKeys.RunBlockedNoTeam]
        : IsTargetArchived ? _strings[StudioStringKeys.RunBlockedArchived]
        : IsBlockedByUndeclaredFolders || IsBlockedByUnknownMountIds ? _strings[StudioStringKeys.RunBlockedFolders]
        : null;

    /// <summary>Whether a refusal of the team itself holds: an undeclared folder, an unknown id, an archive.</summary>
    private bool IsTeamRefused => IsBlockedByUndeclaredFolders || IsBlockedByUnknownMountIds || IsTargetArchived;

    /// <summary>The beat of the status bar moves the durations between the run's events.</summary>
    internal void RefreshClock() =>
        OnPropertiesChanged(nameof(RunDuration), nameof(RunTabSubtitle), nameof(LaunchReadinessText), nameof(RunFooter));

    private void InitializeTabs()
    {
        ShowTeamTabCommand = new RelayCommand(() => ActiveTab = TeamTab);
        ShowOptionsTabCommand = new RelayCommand(() => ActiveTab = OptionsTab);
        OptionsNextCommand = new RelayCommand(() => ActiveTab = _isExpertMode ? CommandTab : RunTab);
        ShowRunTabCommand = new RelayCommand(() => ActiveTab = RunTab);
        ToggleJournalSizeCommand = new RelayCommand(() => IsJournalEnlarged = !IsJournalEnlarged);
        ShowProgressDisplayCommand = new RelayCommand(() => Options.WatchProgress = true, () => IsEditable);
        ShowRawDisplayCommand = new RelayCommand(() => Options.WatchProgress = false, () => IsEditable);

        Progress.Tasks.CollectionChanged += OnProgressTasksChanged;
        Progress.RunningTasks.CollectionChanged += OnProgressTasksChanged;
        Progress.PropertyChanged += (_, _) => OnPropertiesChanged(
            nameof(RunPaneContext), nameof(SentFigure), nameof(ReceivedFigure), nameof(ModelFigure), nameof(RunDuration));
        Log.Lines.CollectionChanged += (_, _) => OnPropertyChanged(nameof(JournalTitle));
        Options.PropertyChanged += OnOptionsChangedForTabs;
        Mounts.LaunchMounts.Mounts.CollectionChanged += (_, _) => RaiseFoldersChanged();
        Target.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TargetSelectionViewModel.StatusDisplay) or nameof(TargetSelectionViewModel.DirectoryRunNotice))
                OnPropertiesChanged(nameof(TargetTypeNote), nameof(TeamFicheLine));
        };
        PropertyChanged += OnOwnPropertyChangedForTabs;
        _strings.CultureChanged += (_, _) => OnTabsLanguageChanged();
    }

    // Every reading of the tabs is worded by the strings: a language switch re-reads them all.
    private void OnTabsLanguageChanged()
    {
        RebuildAgentLanes();
        RaiseTeamTabChanged();
        RaiseFoldersChanged();
        OnPropertiesChanged(
            nameof(OptionsNextLabel), nameof(OptionsTabSubtitle), nameof(DisplayLabel), nameof(CommandTabSubtitle),
            nameof(EngineRow), nameof(ModeRow), nameof(RunTabSubtitle), nameof(RunPaneTitle), nameof(RunPaneContext),
            nameof(JournalTitle), nameof(JournalEnlargeLabel), nameof(RunFooter));
    }

    private void OnOptionsChangedForTabs(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LaunchOptionsViewModel.Verbosity) or nameof(LaunchOptionsViewModel.WatchProgress))
            OnPropertiesChanged(nameof(OptionsTabSubtitle), nameof(OptionsFooter), nameof(DisplayLabel));
    }

    // The tabs read the launcher's own state: each change of it is a change of their readings.
    private void OnOwnPropertyChangedForTabs(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HasTeamCard):
                _crewRoles = CrewAgentRoles.In(Target.SelectedPath);
                RebuildAgentLanes();
                RaiseTeamTabChanged();
                RaiseFoldersChanged();
                break;
            case nameof(TeamHeadline) or nameof(TeamMetaLine) or nameof(IsTargetArchived)
                or nameof(IsBlockedByUndeclaredFolders) or nameof(IsBlockedByUnknownMountIds):
                RaiseTeamTabChanged();
                break;
            case nameof(RunLlmChoice):
                OnPropertyChanged(nameof(TeamFicheLine));
                break;
            case nameof(RunBadgeTone):
                OnPropertiesChanged(nameof(RunTabState), nameof(RunTabSubtitle), nameof(RunPaneTitle), nameof(RunPaneContext),
                    nameof(RunFooter), nameof(HasRunFigures), nameof(RunDuration));
                break;
            case nameof(IsRunning):
                OnPropertiesChanged(nameof(IsEditable), nameof(IsJournalTall), nameof(LaunchReadinessTone), nameof(LaunchReadinessText),
                    nameof(HasRunFigures));
                ShowProgressDisplayCommand.RaiseCanExecuteChanged();
                ShowRawDisplayCommand.RaiseCanExecuteChanged();
                RebuildAgentLanes();
                break;
            case nameof(BinaryLocation):
                OnPropertiesChanged(nameof(EngineRow), nameof(LaunchReadinessTone), nameof(LaunchReadinessText), nameof(LaunchBlockedReason));
                break;
            case nameof(CommandLinePreview):
                OnPropertiesChanged(nameof(CommandTabSubtitle), nameof(MountsRow));
                break;
        }
    }

    private void RaiseTeamTabChanged()
    {
        OnPropertiesChanged(
            nameof(TeamTabState), nameof(TeamTabSubtitle), nameof(TeamFooter), nameof(TeamFicheLine), nameof(TargetTypeNote),
            nameof(LaunchReadinessTone), nameof(LaunchReadinessText), nameof(LaunchBlockedReason));
    }

    private void RaiseFoldersChanged() => OnPropertiesChanged(
        nameof(FolderRows), nameof(FoldersSectionTitle), nameof(OptionsFooter), nameof(ResultVirtualPath), nameof(ResultFigure),
        nameof(LaunchReadinessText), nameof(MountsRow), nameof(TeamFooter));

    private void OnProgressTasksChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildAgentLanes();

    /// <summary>The columns: the crew's agents in their order, each read off the run's tasks by role.</summary>
    private void RebuildAgentLanes()
    {
        var names = new List<string>(_crewRoles);
        var titles = Progress.Tasks.Select(t => t.Title).Concat(Progress.RunningTasks.Select(t => t.Title));
        foreach (var title in titles.Where(title => !names.Contains(title, StringComparer.OrdinalIgnoreCase)))
            names.Add(title);

        AgentLanes.Clear();
        foreach (var name in names)
            AgentLanes.Add(LaneOf(name));
    }

    private RunAgentLane LaneOf(string name)
    {
        if (Progress.RunningTasks.Any(t => string.Equals(t.Title, name, StringComparison.OrdinalIgnoreCase)))
            return new(name, "running", _strings[StudioStringKeys.RunLaneRunning], string.Empty);

        if (Progress.Tasks.LastOrDefault(t => string.Equals(t.Title, name, StringComparison.OrdinalIgnoreCase)) is { } last)
        {
            var detail = string.Join(" · ", new[] { last.ToolCalls, last.Tokens, last.Duration }.Where(part => part.Length > 0));
            return last.Success
                ? new(name, "done", _strings[StudioStringKeys.RunLaneDone], detail)
                : new(name, "failed", _strings[StudioStringKeys.RunLaneFailed], detail);
        }

        return new(name, "waiting", _strings[StudioStringKeys.RunLaneWaiting], string.Empty);
    }

    private string VerbosityLabel(int verbosity) => _strings[verbosity switch
    {
        0 => StudioStringKeys.RunVerbNormal,
        1 => StudioStringKeys.RunVerbDetailed,
        _ => StudioStringKeys.RunVerbDiagnostic,
    }];
}
