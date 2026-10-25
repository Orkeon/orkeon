using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// The conversation with the assistant (30/08 mock, T-01 → T-08).
/// <para>
/// One instance for the whole window, not one per screen: the thread has to survive a tab
/// change, and that is precisely the defect it replaces — until now a question surfaced in
/// a bar above the form, alone, with no history behind it and nothing to say what the
/// assistant had already understood.
/// </para>
/// <para>
/// It asks nothing of its own. The compose-the-team button starts the engine, and the engine's
/// brief stage is the interview: ForgeStages emits an assistant.message, blocks on stdin,
/// and loops until the model submits a brief. Every question here is the model's, however
/// many it wants; every reply goes down that pipe. The thread once played three scripted
/// questions BEFORE the engine started, so the user was interviewed twice, by two
/// mechanisms, on two surfaces.
/// </para>
/// </summary>
public sealed class ChatThreadViewModel : ObservableObject
{
    private static readonly TimeSpan BeatOne = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan BeatTwo = TimeSpan.FromMilliseconds(1800);
    private static readonly TimeSpan LocalAnswer = TimeSpan.FromMilliseconds(1600);
    private static readonly TimeSpan ClosingPause = TimeSpan.FromMilliseconds(1500);

    private readonly IStudioStrings _strings;
    private readonly IUiDelay _delay;
    private readonly AssistantAnswers _answers;

    private Func<string, bool>? _askEngine;
    private Func<IReadOnlyList<ChatRecapFact>>? _facts;
    private Func<string>? _brief;
    private Func<IReadOnlyList<string>>? _briefChips;
    private Func<string?>? _profileName;

    private bool _isOpen;
    private bool _isBusy;
    private bool _isDone;
    private bool _isStarted;
    private bool _isRecapExpanded;
    private int _unreadCount;
    private int _tick;
    private string _draft = "";
    private ChatPendingAction? _pendingAction;
    private ChatQuestion? _ownerQuestion;
    private ChatQuestion? _askedQuestion;
    private ChatTurnViewModel? _askedTurn;
    private ChatQuestion? _shownQuestion;
    private bool _busyBeforeAction;
    private AssistantContext _context = AssistantContext.WizardStep1;

    /// <summary>Builds the thread over its seams; both default to the inline implementations.</summary>
    public ChatThreadViewModel(IStudioStrings? strings = null, IUiDelay? delay = null)
    {
        _strings = strings ?? EnglishStudioStrings.Instance;
        _delay = delay ?? ImmediateUiDelay.Instance;
        _answers = new AssistantAnswers(_strings);
        _strings.CultureChanged += (_, _) => ReloadCatalogue();

        OpenCommand = new RelayCommand(Open);
        CloseCommand = new RelayCommand(Close);
        ToggleCommand = new RelayCommand(() => { if (_isOpen) Close(); else Open(); });
        ToggleRecapCommand = new RelayCommand(() => IsRecapExpanded = !_isRecapExpanded);
        StopCommand = new RelayCommand(Stop, () => _isBusy);
        SendCommand = new RelayCommand(() => Send(_draft), () => _draft.Trim().Length > 0 && !_isBusy);
        // R13: while the screen waits on a card, Enter in the composer no longer sends — a
        // question then goes with Ctrl+Enter or the button, never by reflex.
        PendingActionCommand = new RelayCommand(
            () => _pendingAction?.Action.Execute(null), () => _pendingAction?.Action.CanExecute(null) == true);
        PendingRevealCommand = new RelayCommand(() => _pendingAction?.Reveal.Execute(null), () => _pendingAction is not null);
        QuestionRevealCommand = new RelayCommand(() => Question?.Reveal?.Execute(null), () => Question?.Reveal is not null);
        EnterCommand = new RelayCommand(() =>
        {
            if (EnterSends && SendCommand.CanExecute(null))
                Send(_draft);
        });
        // Not a predetermined answer to a predetermined question: « I don't know — do your
        // best » answers ANY question the model can ask, and it is the only way out for
        // someone stuck on one.
        SkipCommand = new RelayCommand(
            () => Send(_strings[StudioStringKeys.ChatSkipAnswer]), () => IsAsking);
    }

    /// <summary>Raised when Stop is pressed — the owner cancels whatever it started.</summary>
    public event EventHandler? StopRequested;

    /// <summary>Raised when the keyboard focus must go into « Your question » (R13: the answer goes to the assistant).</summary>
    public event EventHandler? FocusInputRequested;

    /// <summary>Raised when the keyboard focus must go to the recommended answer of the question on screen (R15).</summary>
    public event EventHandler? FocusRepliesRequested;

    // ── state ──────────────────────────────────────────────────────────────

    /// <summary>The bubbles, oldest first.</summary>
    public ObservableCollection<ChatTurnViewModel> Turns { get; } = [];

    /// <summary>Whether the thread has the content column.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (SetProperty(ref _isOpen, value))
                OnPropertyChanged(nameof(IsClosed));
        }
    }

    /// <summary>The inverse, for the content the thread replaces.</summary>
    public bool IsClosed => !_isOpen;

    /// <summary>Whether something is being worked on — the hairline, the halo, the dots.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;

            StopCommand.RaiseCanExecuteChanged();
            SendCommand.RaiseCanExecuteChanged();
            RaiseDerived();
        }
    }

    /// <summary>Whether the interview reached its end.</summary>
    public bool IsDone
    {
        get => _isDone;
        private set
        {
            if (SetProperty(ref _isDone, value))
                RaiseDerived();
        }
    }

    /// <summary>Whether the interview was ever started.</summary>
    public bool IsStarted
    {
        get => _isStarted;
        private set => SetProperty(ref _isStarted, value);
    }

    /// <summary>
    /// Whether the assistant is waiting on the user right now.
    /// <para>
    /// There is no event for this, and none is invented: the brief stage emits then BLOCKS
    /// on stdin, so the local mirror of «blocked» is «the last bubble is the assistant's and
    /// nothing is in flight». <c>_isStarted</c> keeps the local answer bank on the Run and
    /// History screens from lighting it; <c>!_isDone</c> matters because after brief.ready
    /// the engine has moved on to the blueprint and is no longer reading stdin — a message
    /// arriving there is narration, not a question.
    /// </para>
    /// </summary>
    public bool IsAsking =>
        _isStarted && !_isBusy && !_isDone && _pendingAction is null && Turns.Count > 0 && Turns[^1].IsBot;

    /// <summary>
    /// What the screen waits on, when it is a card of the main column (R13): the bubble that
    /// names the action, its button and its link. Null while nothing waits on a card.
    /// </summary>
    public ChatPendingAction? PendingAction => _pendingAction;

    /// <summary>Whether the bubble of a pending action shows.</summary>
    public bool HasPendingAction => _pendingAction is not null;

    /// <summary>
    /// The question on screen (R15), with its answers as buttons: the owner's own — an
    /// arbitration, the fill step — else the engine's standing question, its listed options
    /// read from the bubble, then « Skip this question ». Null when nothing is asked.
    /// </summary>
    public ChatQuestion? Question => _ownerQuestion ?? AskedQuestion();

    /// <summary>Whether a question's answers show under the bubbles.</summary>
    public bool HasQuestion => Question is not null;

    /// <summary>The question's link: the work its answer leads to, on screen (R13, R15).</summary>
    public RelayCommand QuestionRevealCommand { get; }

    /// <summary>Whether Enter in the composer sends: not while a card waits on the user (R13).</summary>
    public bool EnterSends => _pendingAction is null;

    /// <summary>How many assistant turns arrived while the thread was closed.</summary>
    public int UnreadCount
    {
        get => _unreadCount;
        private set
        {
            if (SetProperty(ref _unreadCount, value))
                OnPropertyChanged(nameof(HasUnread));
        }
    }

    /// <summary>Whether the access button carries its accent dot.</summary>
    public bool HasUnread => _unreadCount > 0;

    /// <summary>What is being typed.</summary>
    public string Draft
    {
        get => _draft;
        set
        {
            if (SetProperty(ref _draft, value))
                SendCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Whether "What I've noted" is unfolded.</summary>
    public bool IsRecapExpanded
    {
        get => _isRecapExpanded;
        set
        {
            if (SetProperty(ref _isRecapExpanded, value))
                OnPropertyChanged(nameof(RecapIcon));
        }
    }

    /// <summary>Plus or minus, on the recap header.</summary>
    public string RecapIcon => _isRecapExpanded ? "minus" : "plus";

    /// <summary>Whether the thread has nothing in it yet — the primer takes the first bubble.</summary>
    public bool IsEmpty => Turns.Count == 0;

    /// <summary>The primer, anchored to the screen the thread is mounted on.</summary>
    public string Primer => _answers.Primer(_context);

    /// <summary>What the input suggests — answering a standing question, else the invitation.</summary>
    public string Placeholder => _pendingAction?.Placeholder ?? _strings[
        IsAsking ? StudioStringKeys.ChatPlaceholderAnswer : StudioStringKeys.ChatPlaceholder];

    /// <summary>The reply label while a question stands, the send label otherwise.</summary>
    public string SendLabel =>
        _strings[IsAsking ? StudioStringKeys.ChatReply : StudioStringKeys.ChatSend];

    /// <summary>The mono line in the header — what the assistant is doing, in one register.</summary>
    public string Status
    {
        get
        {
            if (_isBusy)
            {
                var beat = _strings[_tick switch
                {
                    0 => StudioStringKeys.ChatStatus1,
                    1 => StudioStringKeys.ChatStatus2,
                    _ => StudioStringKeys.ChatStatus3,
                }];

                return _profileName?.Invoke() is { Length: > 0 } profile
                    ? Format(StudioStringKeys.ChatProfileSuffixPattern, beat, profile)
                    : beat;
            }

            // R13: a card waits on the user — the line says it, and nothing claims to work.
            if (_pendingAction is not null)
                return _strings[StudioStringKeys.ChatStatusWaitingCard];

            // No «n of 3» any more: only the model knows how many questions it will ask.
            if (IsAsking)
                return _strings[StudioStringKeys.ChatStatusWaiting];

            if (_isDone)
                return Format(StudioStringKeys.ChatStatusDonePattern, Turns.Count);

            return Turns.Count > 0
                ? Format(StudioStringKeys.ChatStatusMessagesPattern, Turns.Count)
                : _strings[StudioStringKeys.ChatStatusNoQuestion];
        }
    }

    /// <summary>The sentence in the thinking bubble — the same beats, the other register.</summary>
    public string Thinking => _strings[_tick switch
    {
        0 => StudioStringKeys.ChatThinking1,
        1 => StudioStringKeys.ChatThinking2,
        _ => StudioStringKeys.ChatThinking3,
    }];

    /// <summary>The title of the in-flow strip when the thread is closed.</summary>
    public string StripTitle =>
        _strings[_isBusy ? StudioStringKeys.ChatStripBusy : StudioStringKeys.ChatStripAsking];

    /// <summary>The reply label when a question waits, the see-the-conversation label otherwise.</summary>
    public string StripAction =>
        _strings[IsAsking ? StudioStringKeys.ChatReply : StudioStringKeys.ChatSeeConversation];

    /// <summary>Whether the strip belongs on screen: work in flight, or an unanswered question.</summary>
    public bool IsLive => _isStarted && !_isDone && !_isOpen && !IsAsking;

    /// <summary>The pinned brief card — what the user described, verbatim.</summary>
    public string Brief =>
        _brief?.Invoke() is { Length: > 0 } brief ? brief : _strings[StudioStringKeys.ChatBriefEmpty];

    /// <summary>The chips under the brief — the step-1 precisions, as picked.</summary>
    public IReadOnlyList<string> BriefChips => _briefChips?.Invoke() ?? [];

    /// <summary>Whether the brief card belongs on this screen (it is the wizard's).</summary>
    public bool ShowsBrief => _context
        is AssistantContext.WizardStep1 or AssistantContext.WizardStep2
        or AssistantContext.WizardStep3 or AssistantContext.WizardStep4;

    /// <summary>"What I've noted", rebuilt from the owner on every change.</summary>
    public IReadOnlyList<ChatRecapFact> Facts => _facts?.Invoke() ?? [];

    /// <summary>«4 / 8» — how much of the recap is filled in.</summary>
    public string RecapProgress
    {
        get
        {
            var facts = Facts;
            return Format(StudioStringKeys.ChatRecapProgressPattern, facts.Count(f => f.IsKnown), facts.Count);
        }
    }

    // ── commands ───────────────────────────────────────────────────────────

    /// <summary>Gives the thread the content column.</summary>
    public RelayCommand OpenCommand { get; }

    /// <summary>Gives the column back, history intact.</summary>
    public RelayCommand CloseCommand { get; }

    /// <summary>One button for both.</summary>
    public RelayCommand ToggleCommand { get; }

    /// <summary>Folds and unfolds the recap.</summary>
    public RelayCommand ToggleRecapCommand { get; }

    /// <summary>Stops whatever is in flight, keeping every turn.</summary>
    public RelayCommand StopCommand { get; }

    /// <summary>Sends what is in the input.</summary>
    public RelayCommand SendCommand { get; }

    /// <summary>The pending bubble's button: the card's own command, run as is (R13).</summary>
    public RelayCommand PendingActionCommand { get; }

    /// <summary>The pending bubble's link: the card on screen, focused (R13).</summary>
    public RelayCommand PendingRevealCommand { get; }

    /// <summary>Enter in the composer: sends, unless a card waits on the user (R13).</summary>
    public RelayCommand EnterCommand { get; }

    /// <summary>Answers the question with «do your best» and moves on.</summary>
    public RelayCommand SkipCommand { get; }

    // ── wiring, from the owner ─────────────────────────────────────────────

    /// <summary>
    /// Hands the thread everything it must ask the owner rather than know: the recap rows,
    /// the brief and its chips, the assistant's profile name, and the engine channel.
    /// </summary>
    public void Bind(
        Func<IReadOnlyList<ChatRecapFact>> facts,
        Func<string> brief,
        Func<IReadOnlyList<string>> briefChips,
        Func<string?> profileName,
        Func<string, bool> askEngine)
    {
        _facts = facts;
        _brief = brief;
        _briefChips = briefChips;
        _profileName = profileName;
        _askEngine = askEngine;
    }

    /// <summary>Where the thread now sits — it decides the primer and the free-question fallback.</summary>
    public void SetContext(AssistantContext context)
    {
        if (_context == context)
            return;

        _context = context;
        OnPropertiesChanged(nameof(Primer), nameof(ShowsBrief));
    }

    /// <summary>
    /// Re-reads every catalogued word: the status beats, the primer, and every bubble on
    /// screen that was said from the catalogue. What the user typed is left exactly as they
    /// typed it — and so is what the model said, which is not ours to re-key.
    /// </summary>
    public void ReloadCatalogue()
    {
        foreach (var turn in Turns)
            turn.Retranslate();

        // The standing question is read again in the new language, as the same question: the
        // panel does not open, nor the focus move, for a language switch.
        _askedQuestion = null;
        _askedTurn = null;
        _shownQuestion = Question;
        RaiseDerived();
    }

    /// <summary>Pushes the assistant's own turn, straight from the engine, into the thread.</summary>
    public void AddAssistantTurn(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        Push(new ChatTurnViewModel(_strings, isBot: true, text));
        IsBusy = false;
    }

    /// <summary>
    /// The engine is gone. <paramref name="interrupted"/> means it left before the brief was
    /// accepted — a crash, a non-zero exit, a missing binary, a Stop. Clearing the spinner
    /// and saying nothing left the thread showing a question with a composer that wrote into
    /// a closed pipe, which is worse now that the interview IS the thread.
    /// <para>
    /// Note there is no CancelPending here: the only pending callbacks are the beats, which
    /// their own <c>_isBusy</c> guard neutralises, and a closing pause that must be allowed
    /// to finish.
    /// </para>
    /// </summary>
    public void EngineFinished(bool interrupted = false)
    {
        IsBusy = false;
        if (!interrupted)
            return;

        // From the catalogue, not resolved into the turn: a language switch rewrites what
        // the assistant said, here as everywhere.
        Push(new ChatTurnViewModel(_strings, isBot: true, "", bodyKey: StudioStringKeys.ChatSessionEnded));
        IsStarted = false;
        RaiseDerived();
    }

    /// <summary>
    /// The engine accepted the brief (<c>brief.ready</c>): the interview is over, said out
    /// loud, and the column goes back to the wizard. Idempotent — the event may be replayed
    /// by a session rehydration.
    /// </summary>
    public void BriefAccepted()
    {
        if (_isDone)
            return;

        IsBusy = false;
        IsDone = true;
        Push(new ChatTurnViewModel(_strings, isBot: true, "", isClosing: true));
        RaiseDerived();
        _delay.After(ClosingPause, Close);
    }

    /// <summary>
    /// The screen waits on a card of the main column (R13), or no longer does. While it waits,
    /// the activity indicator stops — « I am sketching… » never shows while the engine is
    /// blocked on the user — and the thread holds a bubble naming the action. When the wait
    /// ends, a thread that was working before goes back to its beats.
    /// </summary>
    public void SetPendingAction(ChatPendingAction? action)
    {
        if (ReferenceEquals(action, _pendingAction) || (action is not null && action.Equals(_pendingAction)))
            return;

        var wasPending = _pendingAction is not null;
        if (!wasPending && action is not null)
            _busyBeforeAction = _isBusy;

        if (_pendingAction is not null)
            _pendingAction.Action.CanExecuteChanged -= OnPendingActionCanExecuteChanged;
        _pendingAction = action;
        if (action is not null)
            action.Action.CanExecuteChanged += OnPendingActionCanExecuteChanged;
        PendingActionCommand.RaiseCanExecuteChanged();
        PendingRevealCommand.RaiseCanExecuteChanged();
        if (action is not null)
        {
            IsBusy = false;
        }
        else if (wasPending && _busyBeforeAction && _isStarted && !_isDone)
        {
            _busyBeforeAction = false;
            IsBusy = true;
            StartBeats();
        }

        OnPropertiesChanged(nameof(PendingAction), nameof(HasPendingAction), nameof(EnterSends));
        RaiseDerived();
    }

    private void OnPendingActionCanExecuteChanged(object? sender, EventArgs e) =>
        PendingActionCommand.RaiseCanExecuteChanged();

    /// <summary>Asks the view for the keyboard focus in « Your question », the panel open (R13).</summary>
    public void RequestInputFocus()
    {
        Open();
        FocusInputRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The answer goes to the assistant (R13, R15): the panel opens, and the focus goes to the
    /// recommended answer when the question offers choices, into « Your question » otherwise.
    /// </summary>
    public void RequestAnswerFocus()
    {
        if (Question is { OffersChoices: true })
        {
            Open();
            FocusRepliesRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        RequestInputFocus();
    }

    /// <summary>
    /// The owner asks a question of its own (R15) — an arbitration, the fill step — or no
    /// longer does. It wins over the engine's standing question. <paramref name="announce"/>
    /// false re-words the same question (a language switch): the panel and the focus stay.
    /// </summary>
    public void SetQuestion(ChatQuestion? question, bool announce = true)
    {
        if (ReferenceEquals(question, _ownerQuestion))
            return;

        _ownerQuestion = question;
        if (!announce)
            _shownQuestion = Question;
        RaiseDerived();
    }

    /// <summary>
    /// The engine's standing question, read once per bubble so that its buttons keep their
    /// identity — and the focus — while the screen re-reads it.
    /// </summary>
    private ChatQuestion? AskedQuestion()
    {
        if (!IsAsking)
            return null;

        var turn = Turns[^1];
        if (ReferenceEquals(turn, _askedTurn) && _askedQuestion is not null)
            return _askedQuestion;

        var replies = ChatReplyReader.Read(turn.Body)
            .Select(option => new ChatReply(
                option.Label, new RelayCommand(() => Send(option.Label)), option.IsRecommended))
            .ToList();
        var offersChoices = replies.Count > 0;
        replies.Add(new ChatReply(_strings[StudioStringKeys.ChatSkip], SkipCommand));
        _askedTurn = turn;
        _askedQuestion = new ChatQuestion(null, "", replies) { OffersChoices = offersChoices };
        return _askedQuestion;
    }

    /// <summary>
    /// A new question came on screen (R15): the panel opens — the main column no longer shows
    /// it — and, when it offers choices, the focus goes to the recommended answer.
    /// </summary>
    private void FollowQuestion()
    {
        var question = Question;
        if (ReferenceEquals(question, _shownQuestion))
            return;

        _shownQuestion = question;
        if (question is null)
            return;

        if (!_isOpen)
            Open();
        if (question.OffersChoices)
            FocusRepliesRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Notifies the recap and the brief card that their source moved.</summary>
    public void OwnerChanged() =>
        OnPropertiesChanged(nameof(Facts), nameof(RecapProgress), nameof(Brief), nameof(BriefChips));

    // ── the session ────────────────────────────────────────────────────────

    /// <summary>
    /// The compose-the-team gesture — the engine is starting. The thread takes the column and
    /// shows the assistant thinking; every question after this comes off the wire.
    /// </summary>
    public void StartSession()
    {
        _delay.CancelPending();
        Turns.Clear();
        _tick = 0;
        Draft = "";
        IsDone = false;
        IsStarted = true;
        IsRecapExpanded = false;
        UnreadCount = 0;
        IsOpen = true;
        IsBusy = true;
        StartBeats();
        OnPropertyChanged(nameof(IsEmpty));
        RaiseDerived();
    }

    /// <summary>Clears the thread whole — the wizard restarting takes its conversation with it.</summary>
    public void Reset()
    {
        _delay.CancelPending();
        Turns.Clear();
        _tick = 0;
        Draft = "";
        IsStarted = false;
        IsDone = false;
        IsBusy = false;
        IsOpen = false;
        UnreadCount = 0;
        _pendingAction = null;
        _busyBeforeAction = false;
        _ownerQuestion = null;
        _askedQuestion = null;
        _askedTurn = null;
        _shownQuestion = null;
        OnPropertiesChanged(nameof(PendingAction), nameof(HasPendingAction), nameof(EnterSends));
        OnPropertyChanged(nameof(IsEmpty));
        RaiseDerived();
    }

    /// <summary>
    /// Walks the «thinking» line through its three beats while the assistant works. Each
    /// callback checks <c>_isBusy</c>, so a reply that lands first freezes the line where it
    /// is rather than moving it after the fact.
    /// </summary>
    private void StartBeats()
    {
        _tick = 0;
        _delay.After(BeatOne, () => { if (_isBusy) { _tick = 1; RaiseDerived(); } });
        _delay.After(BeatTwo, () => { if (_isBusy) { _tick = 2; RaiseDerived(); } });
    }

    private void Send(string text)
    {
        var said = (text ?? "").Trim();
        if (said.Length == 0 || _isBusy)
            return;

        // No CancelPending: its only job was killing the scripted beats, and it would now
        // eat the closing pause — the thread would sit «done» with the column never handed
        // back. The beats defend themselves through their own _isBusy guard.
        Push(new ChatTurnViewModel(_strings, isBot: false, said));
        Draft = "";

        // The engine wins whenever one is listening — a canned line would be a downgrade of
        // an answer the app already knows how to get. The bank speaks only where there is no
        // session at all: before Compose, and on the Run and History screens.
        if (_askEngine is { } ask)
        {
            // Claim the wait BEFORE handing the message over. The reply can come back from
            // inside that call, and setting the flag afterwards would leave the thread
            // saying it is thinking about an answer it is already showing.
            //
            // Only the brief stage reads stdin: past brief.ready nobody is listening, so
            // claiming «thinking» there would lock the composer for the rest of the run.
            if (!_isDone)
                IsBusy = true;

            if (ask(said))
            {
                if (_isBusy)
                    StartBeats();

                return;
            }

            IsBusy = false;
        }

        var answerKey = _answers.AnswerKey(said, _context);
        IsBusy = true;
        StartBeats();
        _delay.After(LocalAnswer, () =>
        {
            IsBusy = false;
            Push(new ChatTurnViewModel(_strings, isBot: true, "", bodyKey: answerKey));
        });
    }

    private void Stop()
    {
        // Everything said stays said, the thread stays open, and nothing restarts on its
        // own: a Stop that quietly resumed a second later would be worse than no Stop.
        _delay.CancelPending();
        IsBusy = false;
        StopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Open()
    {
        IsOpen = true;
        UnreadCount = 0;
        RaiseDerived();
    }

    private void Close()
    {
        IsOpen = false;
        RaiseDerived();
    }

    private void Push(ChatTurnViewModel turn)
    {
        Turns.Add(turn);

        if (turn.IsBot && !_isOpen)
            UnreadCount++;

        OnPropertyChanged(nameof(IsEmpty));
        RaiseDerived();
    }

    private string Format(string key, params object[] arguments) =>
        _strings.Format(key, arguments);

    private void RaiseDerived()
    {
        OnPropertiesChanged(
            nameof(IsAsking), nameof(Placeholder), nameof(SendLabel),
            nameof(Status), nameof(Thinking), nameof(StripTitle), nameof(StripAction),
            nameof(IsLive), nameof(Facts), nameof(RecapProgress), nameof(Brief),
            nameof(BriefChips), nameof(Primer), nameof(Question), nameof(HasQuestion));
        QuestionRevealCommand.RaiseCanExecuteChanged();
        FollowQuestion();
    }
}

/// <summary>
/// The assistant's bubble while the screen waits on a card of the main column (refonte R13):
/// what it says, the button that runs the card's own command, the link that brings the card on
/// screen, and the composer's placeholder meanwhile.
/// </summary>
/// <param name="Text">The bubble's sentence, naming the action.</param>
/// <param name="ActionLabel">The button's label — the card's own (« Confirm the folders »).</param>
/// <param name="Action">The card's command, run as is.</param>
/// <param name="Reveal">The link: the card on screen, focused.</param>
/// <param name="Placeholder">« Confirm the folders to continue, or ask a question ».</param>
public sealed record ChatPendingAction(string Text, string ActionLabel, ICommand Action, ICommand Reveal, string Placeholder);
