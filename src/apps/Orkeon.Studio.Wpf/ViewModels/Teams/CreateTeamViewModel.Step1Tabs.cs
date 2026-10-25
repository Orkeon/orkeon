using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// Step 1 in three tabs (refonte R14): « Need », « The team's folders », « Composition
/// instruction ». The flow is the one it was: the need opens the step, the folders tab exists
/// from the start (empty until the composition proposes them), and when the engine proposes
/// the folders that tab opens and carries the step's wait point (R13). Each tab has a state —
/// a check, an amber dot, an empty circle — a state subtitle, and its pane's own footer.
/// The states are strings (<c>Empty</c>, <c>Done</c>, <c>Expected</c>), the names of the
/// view's <c>StepTabState</c>: no WPF type here.
/// </summary>
public sealed partial class CreateTeamViewModel
{
    /// <summary>The « Need » tab.</summary>
    public const int Step1NeedTab = 0;

    /// <summary>The « The team's folders » tab.</summary>
    public const int Step1FoldersTab = 1;

    /// <summary>The « Composition instruction » tab.</summary>
    public const int Step1NotesTab = 2;

    private const string TabEmpty = "Empty";
    private const string TabDone = "Done";
    private const string TabExpected = "Expected";

    private int _step1TabIndex;

    /// <summary>The tab on screen in step 1.</summary>
    public int Step1TabIndex
    {
        get => _step1TabIndex;
        set
        {
            if (SetProperty(ref _step1TabIndex, Math.Clamp(value, Step1NeedTab, Step1NotesTab)))
                Step1BackCommand?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>« See the folders → » and « ← Back to the folders ».</summary>
    public RelayCommand ShowFoldersTabCommand { get; private set; } = null!;

    /// <summary>The step's « Back »: the tab before the one on screen.</summary>
    public RelayCommand Step1BackCommand { get; private set; } = null!;

    /// <summary>The « Need » tab's state: done once the need is typed.</summary>
    public string NeedTabState => _need.Trim().Length > 0 ? TabDone : TabEmpty;

    /// <summary>« To describe » or « Described ».</summary>
    public string NeedTabSubtitle => _strings[_need.Trim().Length > 0
        ? StudioStringKeys.WizardTabNeedDone
        : StudioStringKeys.WizardTabNeedEmpty];

    /// <summary>
    /// The « Need » pane's footer: « Need described, 2 questions settled with the assistant »,
    /// counted on the answers given in the conversation.
    /// </summary>
    public string NeedFooter
    {
        get
        {
            if (_need.Trim().Length == 0)
                return _strings[StudioStringKeys.WizardTabNeedEmptyFooter];

            var answers = Chat.Turns.Count(turn => turn.IsUser);
            return answers == 0
                ? _strings[StudioStringKeys.WizardTabNeedFooter]
                : _strings.Format(StudioStringKeys.WizardTabNeedFooterAnswers, answers);
        }
    }

    /// <summary>Whether the folders were confirmed in this session.</summary>
    private bool FoldersConfirmed => !_model.FoldersPending && _model.Folders.Count > 0;

    /// <summary>The folders tab's state: the amber dot while they wait for the confirm, a check once confirmed.</summary>
    public string FoldersTabState =>
        _model.FoldersPending ? TabExpected : FoldersConfirmed ? TabDone : TabEmpty;

    /// <summary>« Proposed after the composition », « 3 folders to confirm », « 3 folders confirmed ».</summary>
    public string FoldersTabSubtitle
    {
        get
        {
            if (_model.FoldersPending)
                return _strings.Format(StudioStringKeys.WizardTabFoldersPending, FolderRows.Count);
            if (FoldersConfirmed)
                return _strings.Format(StudioStringKeys.WizardTabFoldersConfirmed, _model.Folders.Count);

            return _strings[StudioStringKeys.WizardTabFoldersEmpty];
        }
    }

    /// <summary>Whether the folders tab has nothing to show yet — its pane says when they come.</summary>
    public bool IsFoldersTabEmpty => !_model.FoldersPending && !FoldersConfirmed;

    /// <summary>
    /// The folders pane's footer: how many folders are still to choose and what confirming now
    /// gives them — « 1 folder to choose. You can confirm now: the team will get an empty
    /// « documents » folder at adoption. » — or that each folder has its place.
    /// </summary>
    public string FoldersFooter
    {
        get
        {
            if (!_model.FoldersPending)
            {
                return _strings[FoldersConfirmed
                    ? StudioStringKeys.WizardTabFoldersConfirmedFooter
                    : StudioStringKeys.WizardTabFoldersEmpty];
            }

            var toChoose = FolderRows.Where(row => row.NeedsChoice).ToList();
            return toChoose.Count switch
            {
                0 => _strings[StudioStringKeys.WizardTabFoldersReadyFooter],
                1 => _strings.Format(StudioStringKeys.WizardTabFoldersOneToChoose, toChoose[0].FolderName),
                _ => _strings.Format(StudioStringKeys.WizardTabFoldersManyToChoose, toChoose.Count),
            };
        }
    }

    /// <summary>The instruction tab's state: optional, so an empty circle until written.</summary>
    public string NotesTabState => ComposeNotes.HasConsigne ? TabDone : TabEmpty;

    /// <summary>« Optional » or « Written ».</summary>
    public string NotesTabSubtitle => _strings[ComposeNotes.HasConsigne
        ? StudioStringKeys.WizardTabNotesDone
        : StudioStringKeys.WizardTabNotesEmpty];

    /// <summary>The instruction pane's footer: « No instruction yet. », or that it is taken as written.</summary>
    public string NotesFooter => _strings[ComposeNotes.HasConsigne
        ? StudioStringKeys.WizardTabNotesFooterTaken
        : StudioStringKeys.WizardTabNotesFooterEmpty];

    /// <summary>« Composition under way » on the disabled « Compose the team »; empty otherwise.</summary>
    public string ComposeTip => IsEngineRunning ? _strings[StudioStringKeys.WizardComposeBusyTip] : string.Empty;

    private void InitializeStep1Tabs()
    {
        ShowFoldersTabCommand = new RelayCommand(() => Step1TabIndex = Step1FoldersTab);
        Step1BackCommand = new RelayCommand(() => Step1TabIndex--, () => _step1TabIndex > Step1NeedTab);
        ComposeNotes.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StepNotesViewModel.HasConsigne))
                OnPropertiesChanged(nameof(NotesTabState), nameof(NotesTabSubtitle), nameof(NotesFooter));
        };
        FolderRows.CollectionChanged += (_, _) => RefreshStep1Tabs();
    }

    /// <summary>Republishes every tab's state, subtitle and footer.</summary>
    private void RefreshStep1Tabs() => OnPropertiesChanged(
        nameof(NeedTabState), nameof(NeedTabSubtitle), nameof(NeedFooter),
        nameof(FoldersTabState), nameof(FoldersTabSubtitle), nameof(IsFoldersTabEmpty), nameof(FoldersFooter),
        nameof(NotesTabState), nameof(NotesTabSubtitle), nameof(NotesFooter), nameof(ComposeTip));
}
