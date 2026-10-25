using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>What the creation waits on the user for (refonte R13): one point at a time, named.</summary>
public enum WizardWaitPoint
{
    /// <summary>Nothing: the engine works, or nothing is under way.</summary>
    None,

    /// <summary>Step 1: the folders the engine proposed wait for « Confirm the folders ».</summary>
    ConfirmFolders,

    /// <summary>Step 1: the assistant asked a question and the engine reads the answer.</summary>
    AnswerAssistant,

    /// <summary>Step 1: the assistant has no model yet — the gate's list.</summary>
    ChooseModel,

    /// <summary>The engine waits on an arbitration of the user's.</summary>
    Decide,

    /// <summary>Step 2: a folder of the team is still to choose before the trial.</summary>
    ChooseFolder,

    /// <summary>Step 4: the team is ready and waits to be adopted.</summary>
    Adopt,
}

/// <summary>
/// The single wait point (refonte R13). When the creation waits on the user, one point is
/// named: the stepper and the status bar say « Waiting for you: confirm the folders », the
/// assistant stops its activity indicator and posts a bubble that runs the same command as
/// the card, the composer's placeholder says what is expected and Enter no longer sends, and
/// the view moves the keyboard focus to the card's main button — or into « Your question »
/// when the answer goes to the assistant. Everything here is read from the state the screen
/// already holds; nothing new is asked of the engine.
/// </summary>
public sealed partial class CreateTeamViewModel
{
    private WizardWaitPoint _waitPoint;

    /// <summary>
    /// Raised when a wait point is reached, or asked for again (« Waiting for you: … » clicked):
    /// the view reads <see cref="WaitPoint"/>, brings the step, the tab and the card on screen
    /// and moves the keyboard focus.
    /// </summary>
    public event EventHandler? WaitPointReached;

    /// <summary>The click on « Waiting for you: … »: the wait point on screen, focused.</summary>
    public RelayCommand GoToWaitPointCommand { get; private set; } = null!;

    /// <summary>What the creation waits on the user for, right now.</summary>
    public WizardWaitPoint WaitPoint => _waitPoint;

    /// <summary>Whether something waits on the user.</summary>
    public bool HasWaitPoint => _waitPoint != WizardWaitPoint.None;

    /// <summary>« Waiting for you: confirm the folders »; empty when nothing waits.</summary>
    public string WaitPointLabel =>
        _waitPoint == WizardWaitPoint.None
            ? string.Empty
            : _strings.Format(StudioStringKeys.WizardWaitingFor, _strings[WaitActionKey(_waitPoint)]);

    /// <summary>
    /// Whether the step's other cards step back behind the expected one (R13): a wait on a card
    /// of step 2 or 4. A tabbed step marks its wait with the tab's amber dot instead.
    /// </summary>
    public bool IsWaitOnChooseFolder => _waitPoint == WizardWaitPoint.ChooseFolder && _step == 2;

    /// <summary>Whether step 4's adoption card is the one expected (R13).</summary>
    public bool IsWaitOnAdopt => _waitPoint == WizardWaitPoint.Adopt && _step == 4;

    /// <summary>Whether the session reached its end and the team waits to be adopted — the name typed or not.</summary>
    private bool IsReadyToAdopt =>
        !IsEngineRunning
        && _model.Slug is not null
        && _model.Promotion is null
        && (_model.Stage is "ready" || string.Equals(_model.FinishedStatus, "ready", StringComparison.Ordinal));

    private void InitializeWaitPoint() =>
        GoToWaitPointCommand = new RelayCommand(GoToWaitPoint, () => HasWaitPoint);

    /// <summary>What the state says the creation waits for, in priority order.</summary>
    private WizardWaitPoint ReadWaitPoint()
    {
        if (IsEngineRunning)
        {
            if (_model.FoldersPending)
                return WizardWaitPoint.ConfirmFolders;
            if (_model.DecisionPending)
                return WizardWaitPoint.Decide;
            if (Chat.IsAsking)
                return WizardWaitPoint.AnswerAssistant;
        }

        // Read from what the engine and the session reached, never from a field being typed:
        // the wait point moves the keyboard focus, and it must not take it from under the
        // user's keys. A point a step reached stays named from the other steps, so that
        // « Waiting for you: … » brings it back.
        if (_step == 1 && NeedsAssistant)
            return WizardWaitPoint.ChooseModel;
        if (_maxStep >= 2 && CanTryTeam && FoldersToChooseCount > 0)
            return WizardWaitPoint.ChooseFolder;
        if (_maxStep >= 4 && IsReadyToAdopt)
            return WizardWaitPoint.Adopt;

        return WizardWaitPoint.None;
    }

    /// <summary>
    /// Re-reads the wait point and, when it moved, tells every surface: the assistant's bubble
    /// and composer, the stepper, the status bar's stage, and the view's focus.
    /// </summary>
    private void SyncWaitPoint()
    {
        var next = ReadWaitPoint();
        if (next == _waitPoint)
            return;

        _waitPoint = next;
        Chat.SetPendingAction(PendingActionFor(next));
        OnPropertiesChanged(
            nameof(WaitPoint), nameof(HasWaitPoint), nameof(WaitPointLabel),
            nameof(IsWaitOnChooseFolder), nameof(IsWaitOnAdopt),
            nameof(SessionStateLabel), nameof(HasSessionState), nameof(Step1Hint));
        GoToWaitPointCommand?.RaiseCanExecuteChanged();
        if (next == WizardWaitPoint.ConfirmFolders)
            Step1TabIndex = Step1FoldersTab;
        SyncProgress();
        if (next != WizardWaitPoint.None)
            WaitPointReached?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>« Waiting for you: … » clicked: the step that holds the wait, then the focus.</summary>
    private void GoToWaitPoint()
    {
        switch (_waitPoint)
        {
            case WizardWaitPoint.None:
                return;
            case WizardWaitPoint.ConfirmFolders:
                GoStep(1);
                Step1TabIndex = Step1FoldersTab;
                break;
            case WizardWaitPoint.ChooseModel:
                GoStep(1);
                break;
            case WizardWaitPoint.AnswerAssistant:
                Chat.OpenCommand.Execute(null);
                break;
            case WizardWaitPoint.ChooseFolder:
                GoStep(2);
                break;
            case WizardWaitPoint.Adopt:
                GoStep(4);
                break;
        }

        WaitPointReached?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The assistant's side of a wait on a card (R13): its bubble, the button that runs the
    /// card's command and the link to the card. A wait on the assistant itself, or on nothing,
    /// has none — the composer is the answer.
    /// </summary>
    private ChatPendingAction? PendingActionFor(WizardWaitPoint point) => point switch
    {
        WizardWaitPoint.ConfirmFolders => new ChatPendingAction(
            _strings[StudioStringKeys.WizardWaitBubbleFolders],
            _strings[StudioStringKeys.WizardFoldersConfirm],
            ConfirmFoldersCommand,
            GoToWaitPointCommand,
            _strings[StudioStringKeys.WizardWaitPlaceholderFolders]),
        WizardWaitPoint.ChooseFolder => new ChatPendingAction(
            _strings[StudioStringKeys.WizardWaitBubbleChooseFolder],
            _strings[StudioStringKeys.WizardWaitShowCard],
            GoToWaitPointCommand,
            GoToWaitPointCommand,
            _strings[StudioStringKeys.WizardWaitPlaceholderChooseFolder]),
        WizardWaitPoint.Adopt => new ChatPendingAction(
            _strings[StudioStringKeys.WizardWaitBubbleAdopt],
            _strings[StudioStringKeys.WizardSave],
            SaveTeamCommand,
            GoToWaitPointCommand,
            _strings[StudioStringKeys.WizardWaitPlaceholderAdopt]),
        _ => null,
    };

    /// <summary>The action each wait point names, in the infinitive: « confirm the folders ».</summary>
    private static string WaitActionKey(WizardWaitPoint point) => point switch
    {
        WizardWaitPoint.ConfirmFolders => StudioStringKeys.WizardWaitActionFolders,
        WizardWaitPoint.AnswerAssistant => StudioStringKeys.WizardWaitActionAnswer,
        WizardWaitPoint.ChooseModel => StudioStringKeys.WizardWaitActionModel,
        WizardWaitPoint.Decide => StudioStringKeys.WizardWaitActionDecide,
        WizardWaitPoint.ChooseFolder => StudioStringKeys.WizardWaitActionChooseFolder,
        _ => StudioStringKeys.WizardWaitActionAdopt,
    };
}
