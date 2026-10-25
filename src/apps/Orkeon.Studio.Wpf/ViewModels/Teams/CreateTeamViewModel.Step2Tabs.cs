using System.Globalization;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// Step 2 in four tabs (refonte lot 10, R14): « Team », « The team's folders », « Composition
/// instruction », « Composition ». The team tab opens the step and the tab on screen survives
/// a recomposition; only a new creation brings it back to the team. Each tab has a state mark
/// and a state subtitle; the « Composition » tab is read only — what the engine did, the
/// technical journal behind a button. The states are the names of the view's
/// <c>StepTabState</c> (<c>Working</c> turns while the engine composes): no WPF type here.
/// </summary>
public sealed partial class CreateTeamViewModel
{
    /// <summary>The « Team » tab.</summary>
    public const int Step2TeamTab = 0;

    /// <summary>The « The team's folders » tab.</summary>
    public const int Step2FoldersTab = 1;

    /// <summary>The « Composition instruction » tab.</summary>
    public const int Step2NotesTab = 2;

    /// <summary>The « Composition » tab.</summary>
    public const int Step2CompositionTab = 3;

    /// <summary>The team's « Can: » pills shown before « + N » folds the rest.</summary>
    public const int FoldedTeamToolCount = 7;

    private const string TabWorking = "Working";
    private const string TabWarning = "Warning";

    private int _step2TabIndex;
    private bool _showAllTeamTools;
    private bool _isYamlView;

    /// <summary>The tab on screen in step 2.</summary>
    public int Step2TabIndex
    {
        get => _step2TabIndex;
        set => SetProperty(ref _step2TabIndex, Math.Clamp(value, Step2TeamTab, Step2CompositionTab));
    }

    /// <summary>« See the folders → », the team pane's footer.</summary>
    public RelayCommand SeeFoldersCommand { get; private set; } = null!;

    /// <summary>« See the instruction → », the folders pane's footer.</summary>
    public RelayCommand SeeNotesCommand { get; private set; } = null!;

    /// <summary>« See the composition → », the instruction pane's footer.</summary>
    public RelayCommand SeeCompositionCommand { get; private set; } = null!;

    /// <summary>« ← Back to the team », the composition pane's footer.</summary>
    public RelayCommand BackToTeamCommand { get; private set; } = null!;

    /// <summary>The Proposal | YAML switch (Expert): the proposal side.</summary>
    public RelayCommand ShowProposalViewCommand { get; private set; } = null!;

    /// <summary>The Proposal | YAML switch (Expert): the generated definition side.</summary>
    public RelayCommand ShowYamlViewCommand { get; private set; } = null!;

    /// <summary>The team's « + N » pill: unfolds the rest.</summary>
    public RelayCommand ShowAllTeamToolsCommand { get; private set; } = null!;

    /// <summary>Whether the team pane shows the generated definition instead of the proposal.</summary>
    public bool IsYamlView
    {
        get => _isYamlView;
        private set
        {
            if (SetProperty(ref _isYamlView, value))
                OnPropertyChanged(nameof(IsProposalView));
        }
    }

    /// <summary>Whether the team pane shows the proposal — the default.</summary>
    public bool IsProposalView => !_isYamlView;

    /// <summary>Whether the engine composes — not a trial, no proposal yet to show.</summary>
    public bool IsComposing => IsEngineWorking && !RunInProgress;

    /// <summary>The team's pills on screen: the first seven until « + N » is clicked.</summary>
    public IReadOnlyList<WizardToolChip> VisibleTeamTools
    {
        get
        {
            var tools = Tools;
            return _showAllTeamTools || tools.Count <= FoldedTeamToolCount ? tools : [.. tools.Take(FoldedTeamToolCount)];
        }
    }

    /// <summary>Whether team pills are folded behind « + N ».</summary>
    public bool HasHiddenTeamTools => Tools.Count > VisibleTeamTools.Count;

    /// <summary>« + 4 »: the folded team pills' count.</summary>
    public string MoreTeamToolsLabel => HasHiddenTeamTools
        ? _strings.Format(StudioStringKeys.WizardMoreTools, Tools.Count - VisibleTeamTools.Count)
        : string.Empty;

    /// <summary>The team tab: turning while the engine composes, a check once a team is proposed.</summary>
    public string TeamTabState => IsComposing ? TabWorking : HasProposal ? TabDone : TabEmpty;

    /// <summary>« 3 agents · proposed », « Being composed », « Not proposed yet ».</summary>
    public string TeamTabSubtitle =>
        IsComposing ? _strings[StudioStringKeys.WizardTabTeamComposing]
        : HasProposal ? _strings.Format(StudioStringKeys.WizardTabTeamProposed, Agents.Count)
        : _strings[StudioStringKeys.WizardTabTeamEmpty];

    /// <summary>The team pane's footer: « 3 agents, composition stopped. », or that it goes on.</summary>
    public string TeamFooter => IsComposing
        ? _strings[StudioStringKeys.WizardTeamFooterComposing]
        : _strings.Format(StudioStringKeys.WizardTeamFooterStopped, Agents.Count);

    /// <summary>The folders tab of step 2: the amber dot while a folder is to choose (R13).</summary>
    public string Step2FoldersTabState =>
        HasFoldersToChoose ? TabExpected : HasMountRows ? TabDone : TabEmpty;

    /// <summary>« 2 folders · 1 to choose », « 2 folders · inside the team », « 2 folders set », « No folder ».</summary>
    public string Step2FoldersTabSubtitle
    {
        get
        {
            var rows = MountRows;
            if (rows.Count == 0)
                return _strings[StudioStringKeys.WizardTabTeamFoldersNone];
            if (FoldersToChooseCount is var toChoose and > 0)
                return _strings.Format(StudioStringKeys.WizardTabTeamFoldersToChoose, rows.Count, toChoose);

            return _strings.Format(
                rows.All(row => row.IsInsideTeam)
                    ? StudioStringKeys.WizardTabTeamFoldersInside
                    : StudioStringKeys.WizardTabTeamFoldersSet,
                rows.Count);
        }
    }

    /// <summary>The folders pane's footer: what is left to choose, or that every folder has its place.</summary>
    public string Step2FoldersFooter
    {
        get
        {
            var rows = MountRows;
            if (rows.Count == 0)
                return _strings[StudioStringKeys.WizardStep2FoldersFooterNone];
            if (FoldersToChooseCount is var toChoose and > 0)
                return _strings.Format(StudioStringKeys.WizardFoldersToChoose, toChoose);

            return _strings.Format(
                rows.All(row => row.IsInsideTeam)
                    ? StudioStringKeys.WizardStep2FoldersFooterInside
                    : StudioStringKeys.WizardStep2FoldersFooterSet,
                rows.Count);
        }
    }

    /// <summary>Whether a composition has run in this session (stopped or under way).</summary>
    private bool HasComposition => IsComposing || _model.FinishedStatus is not null;

    /// <summary>The composition tab: turning while it runs, a check once stopped, the warning on a definition to fix.</summary>
    public string CompositionTabState =>
        IsComposing ? TabWorking
        : _model.FinishedStatus is null ? TabEmpty
        : ValidationOk == false ? TabWarning
        : TabDone;

    /// <summary>« Stopped · 4 files written », « Under way », « Not started ».</summary>
    public string CompositionTabSubtitle =>
        IsComposing ? _strings[StudioStringKeys.WizardTabCompositionRunning]
        : _model.FinishedStatus is null ? _strings[StudioStringKeys.WizardTabCompositionNone]
        : _strings.Format(StudioStringKeys.WizardTabCompositionStopped, Files.Count);

    /// <summary>The « State » row: under way, or stopped with what the validation said.</summary>
    public string CompositionStateText =>
        IsComposing ? _strings[StudioStringKeys.WizardCompositionStateRunning]
        : ValidationOk switch
        {
            true => _strings[StudioStringKeys.WizardCompositionStateValid],
            false => _strings[StudioStringKeys.WizardCompositionStateInvalid],
            null => _strings[StudioStringKeys.WizardCompositionStateStopped],
        };

    /// <summary>The « Files written » row.</summary>
    public string CompositionFilesText => _strings.Format(StudioStringKeys.WizardCompositionFiles, Files.Count);

    /// <summary>The « Last exchange » row: the model's last words, whole (the row clips them, the tooltip does not).</summary>
    public string LastExchange =>
        _model.Messages.LastOrDefault(m => m.Role == Orkeon.Studio.Core.Forge.ForgeChatMessage.Assistant)?.Text ?? string.Empty;

    /// <summary>Whether the model said anything yet.</summary>
    public bool HasLastExchange => LastExchange.Length > 0;

    /// <summary>The « Draft » row: when the session was saved on disk, or that it is not yet.</summary>
    public string CompositionDraftText
    {
        get
        {
            if (_draftSavedAt is not { } savedAt || _model.Slug is null)
                return _strings[StudioStringKeys.WizardCompositionDraftNotSaved];

            var local = TimeZoneInfo.ConvertTime(savedAt, _clock.LocalTimeZone);
            return _strings.Format(
                StudioStringKeys.WizardCompositionDraftSaved,
                local.ToString("HH:mm", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>The composition pane's footer.</summary>
    public string CompositionFooter => _strings[IsComposing
        ? StudioStringKeys.WizardCompositionFooterRunning
        : StudioStringKeys.WizardCompositionFooterDone];

    /// <summary>Whether the composition pane has anything to report.</summary>
    public bool IsCompositionTabEmpty => !HasComposition;

    /// <summary>
    /// « Try the team → » runs only once no folder is left to choose: a trial over a folder
    /// nobody chose would read nothing. « Adopt without trying » keeps the pause's rule.
    /// </summary>
    public bool CanTryTeamNow => CanTryTeam && !HasFoldersToChoose;

    /// <summary>The disabled « Try the team → »'s reason; empty when it runs.</summary>
    public string TryTeamTip => CanTryTeam && HasFoldersToChoose
        ? _strings.Format(StudioStringKeys.WizardFoldersToChoose, FoldersToChooseCount)
        : string.Empty;

    /// <summary>Whether the action bar has a sentence — its dot shows with it.</summary>
    public bool HasStep2Hint => Step2Hint.Length > 0;

    /// <summary>Whether the action bar's sentence speaks of a folder to choose — its dot in the warning tone.</summary>
    public bool IsStep2HintWarning => HasFoldersToChoose;

    private void InitializeStep2Tabs()
    {
        SeeFoldersCommand = new RelayCommand(() => Step2TabIndex = Step2FoldersTab);
        SeeNotesCommand = new RelayCommand(() => Step2TabIndex = Step2NotesTab);
        SeeCompositionCommand = new RelayCommand(() => Step2TabIndex = Step2CompositionTab);
        BackToTeamCommand = new RelayCommand(() => Step2TabIndex = Step2TeamTab);
        ShowProposalViewCommand = new RelayCommand(() => IsYamlView = false);
        ShowYamlViewCommand = new RelayCommand(() => IsYamlView = true);
        ShowAllTeamToolsCommand = new RelayCommand(() =>
        {
            _showAllTeamTools = true;
            RefreshStep2Tabs();
        });
        Agents.CollectionChanged += (_, _) => RefreshStep2Tabs();
    }

    /// <summary>A new creation: the team tab, the proposal side, the pills folded.</summary>
    private void ResetStep2Tabs()
    {
        Step2TabIndex = Step2TeamTab;
        IsYamlView = false;
        _showAllTeamTools = false;
        RefreshStep2Tabs();
    }

    /// <summary>Republishes every step-2 tab's state, subtitle and footer, and the trial's gate.</summary>
    private void RefreshStep2Tabs()
    {
        OnPropertiesChanged(
            nameof(IsComposing), nameof(VisibleTeamTools), nameof(HasHiddenTeamTools), nameof(MoreTeamToolsLabel),
            nameof(TeamTabState), nameof(TeamTabSubtitle), nameof(TeamFooter),
            nameof(Step2FoldersTabState), nameof(Step2FoldersTabSubtitle), nameof(Step2FoldersFooter),
            nameof(CompositionTabState), nameof(CompositionTabSubtitle), nameof(CompositionStateText),
            nameof(CompositionFilesText), nameof(LastExchange), nameof(HasLastExchange),
            nameof(CompositionDraftText), nameof(CompositionFooter), nameof(IsCompositionTabEmpty),
            nameof(CanTryTeamNow), nameof(TryTeamTip), nameof(HasStep2Hint), nameof(IsStep2HintWarning));
        TryTeamCommand?.RaiseCanExecuteChanged();
    }
}
