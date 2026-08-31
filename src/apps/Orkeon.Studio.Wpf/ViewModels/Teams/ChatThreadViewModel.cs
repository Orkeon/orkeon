using System.Collections.ObjectModel;
using System.Globalization;
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
/// It owns the composition interview: « Composer l'équipe » no longer starts the engine, it
/// opens this thread, asks three questions, and only then hands the enriched brief over.
/// The engine call itself stays where it was — the thread wraps around it, never replaces it.
/// </para>
/// </summary>
public sealed class ChatThreadViewModel : ObservableObject
{
    private static readonly TimeSpan FirstThink = TimeSpan.FromMilliseconds(2300);
    private static readonly TimeSpan NextThink = TimeSpan.FromMilliseconds(1900);
    private static readonly TimeSpan LocalAnswer = TimeSpan.FromMilliseconds(1600);
    private static readonly TimeSpan ClosingPause = TimeSpan.FromMilliseconds(1500);

    private readonly IStudioStrings _strings;
    private readonly IUiDelay _delay;
    private readonly AssistantAnswers _answers;

    private IReadOnlyList<AssistantQuestion> _questions;
    private Action<IReadOnlyList<string>>? _onInterviewComplete;
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
    private int _pendingQuestionIndex;
    private int _tick;
    private bool _questionOnTheTable;
    private bool _handingOver;
    private string _draft = "";
    private AssistantContext _context = AssistantContext.WizardStep1;

    /// <summary>Builds the thread over its seams; both default to the inline implementations.</summary>
    public ChatThreadViewModel(IStudioStrings? strings = null, IUiDelay? delay = null)
    {
        _strings = strings ?? EnglishStudioStrings.Instance;
        _delay = delay ?? ImmediateUiDelay.Instance;
        _answers = new AssistantAnswers(_strings);
        _questions = AssistantInterview.Build(_strings);
        _strings.CultureChanged += (_, _) => ReloadCatalogue();

        OpenCommand = new RelayCommand(Open);
        CloseCommand = new RelayCommand(Close);
        EditBriefCommand = new RelayCommand(EditBrief);
        ToggleCommand = new RelayCommand(() => { if (_isOpen) Close(); else Open(); });
        ToggleRecapCommand = new RelayCommand(() => IsRecapExpanded = !_isRecapExpanded);
        StopCommand = new RelayCommand(Stop, () => _isBusy);
        SendCommand = new RelayCommand(() => Send(_draft), () => _draft.Trim().Length > 0 && !_isBusy);
        SkipCommand = new RelayCommand(
            () => Send(_strings[StudioStringKeys.ChatSkipAnswer]), () => IsAsking);
        PickChipCommand = new RelayCommand(value => Send(value as string ?? ""), _ => IsAsking);
    }

    /// <summary>Raised when the user asks to leave the thread and go back to the form.</summary>
    public event EventHandler? EditBriefRequested;

    /// <summary>Raised when Stop is pressed — the owner cancels whatever it started.</summary>
    public event EventHandler? StopRequested;

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

    /// <summary>Whether a question is on the table right now.</summary>
    public bool IsAsking =>
        _questionOnTheTable && !_isBusy && !_isDone && _pendingQuestionIndex < _questions.Count;

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

    /// <summary>Which question is owed an answer.</summary>
    public int PendingQuestionIndex => _pendingQuestionIndex;

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

    /// <summary>Whether « Ce que j'ai retenu » is unfolded.</summary>
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

    /// <summary>The quick replies of the question on the table; empty when none is.</summary>
    public IReadOnlyList<AssistantChip> Chips =>
        IsAsking ? _questions[_pendingQuestionIndex].Chips : [];

    /// <summary>What the input suggests — the question's own example, else the neutral invitation.</summary>
    public string Placeholder =>
        IsAsking ? _questions[_pendingQuestionIndex].Placeholder : _strings[StudioStringKeys.ChatPlaceholder];

    /// <summary>« Répondre » while a question stands, « Envoyer » otherwise.</summary>
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

            if (IsAsking)
                return Format(StudioStringKeys.ChatStatusQuestionPattern, _pendingQuestionIndex + 1, _questions.Count);

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

    /// <summary>« Répondre » when a question waits, « Voir la discussion » otherwise.</summary>
    public string StripAction =>
        _strings[IsAsking ? StudioStringKeys.ChatReply : StudioStringKeys.ChatSeeConversation];

    /// <summary>Whether the strip belongs on screen: work in flight, or an unanswered question.</summary>
    public bool IsLive => _isStarted && !_isDone && !_isOpen;

    /// <summary>The pinned brief card — what the user described, verbatim.</summary>
    public string Brief =>
        _brief?.Invoke() is { Length: > 0 } brief ? brief : _strings[StudioStringKeys.ChatBriefEmpty];

    /// <summary>The chips under the brief — the step-1 precisions, as picked.</summary>
    public IReadOnlyList<string> BriefChips => _briefChips?.Invoke() ?? [];

    /// <summary>Whether the brief card belongs on this screen (it is the wizard's).</summary>
    public bool ShowsBrief => _context
        is AssistantContext.WizardStep1 or AssistantContext.WizardStep2
        or AssistantContext.WizardStep3 or AssistantContext.WizardStep4;

    /// <summary>« Ce que j'ai retenu », rebuilt from the owner on every change.</summary>
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

    /// <summary>
    /// «Edit» on the pinned brief card: the same retreat as Close, plus the intent that
    /// distinguishes the two. Sharing CloseCommand made the pencil a second cross — it hid
    /// the thread and left the brief exactly as unreachable as before.
    /// </summary>
    public RelayCommand EditBriefCommand { get; }

    /// <summary>One button for both.</summary>
    public RelayCommand ToggleCommand { get; }

    /// <summary>Folds and unfolds the recap.</summary>
    public RelayCommand ToggleRecapCommand { get; }

    /// <summary>Stops whatever is in flight, keeping every turn.</summary>
    public RelayCommand StopCommand { get; }

    /// <summary>Sends what is in the input.</summary>
    public RelayCommand SendCommand { get; }

    /// <summary>Answers the question with «do your best» and moves on.</summary>
    public RelayCommand SkipCommand { get; }

    /// <summary>Answers with a quick reply's recorded value.</summary>
    public RelayCommand PickChipCommand { get; }

    // ── wiring, from the owner ─────────────────────────────────────────────

    /// <summary>
    /// Hands the thread everything it must ask the owner rather than know: the recap rows,
    /// the brief and its chips, the assistant's profile name, the engine channel, and what
    /// to do once the three answers are in.
    /// </summary>
    public void Bind(
        Func<IReadOnlyList<ChatRecapFact>> facts,
        Func<string> brief,
        Func<IReadOnlyList<string>> briefChips,
        Func<string?> profileName,
        Func<string, bool> askEngine,
        Action<IReadOnlyList<string>> onInterviewComplete)
    {
        _facts = facts;
        _brief = brief;
        _briefChips = briefChips;
        _profileName = profileName;
        _askEngine = askEngine;
        _onInterviewComplete = onInterviewComplete;
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
    /// Re-reads every catalogued word. The questions still to ask, the status beats, the
    /// primer — and every bubble already on screen that was said from the catalogue. What
    /// the user typed is left exactly as they typed it.
    /// </summary>
    public void ReloadCatalogue()
    {
        _questions = AssistantInterview.Build(_strings);

        foreach (var turn in Turns)
            turn.Retranslate();

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

    /// <summary>Tells the thread the engine finished, so the header stops claiming it is working.</summary>
    public void EngineFinished()
    {
        _delay.CancelPending();
        IsBusy = false;
    }

    /// <summary>Notifies the recap and the brief card that their source moved.</summary>
    public void OwnerChanged() =>
        OnPropertiesChanged(nameof(Facts), nameof(RecapProgress), nameof(Brief), nameof(BriefChips));

    // ── the interview ──────────────────────────────────────────────────────

    /// <summary>
    /// « Composer l'équipe ». Opens the thread and asks; the engine is not started here —
    /// it is started by the callback, after the third answer, with the enriched brief.
    /// </summary>
    public void StartInterview()
    {
        // Pressed again after the brief is complete — «recompose with what I already told
        // you». The conversation is not replayed: the answers it collected are handed
        // straight back to the engine, and the thread stays where the user left it.
        if (_isDone)
        {
            _onInterviewComplete?.Invoke(CollectedAnswers());
            return;
        }

        // Pressed again mid-interview: bring the question back, do not ask it twice.
        if (_isStarted)
        {
            Open();
            return;
        }

        _delay.CancelPending();
        Turns.Clear();
        _pendingQuestionIndex = 0;
        _questionOnTheTable = false;
        _handingOver = false;
        _tick = 0;
        Draft = "";
        IsDone = false;
        IsStarted = true;
        IsRecapExpanded = false;
        UnreadCount = 0;
        IsOpen = true;
        IsBusy = true;
        OnPropertyChanged(nameof(IsEmpty));
        Think(0, FirstThink);
    }

    /// <summary>Clears the thread whole — the wizard restarting takes its conversation with it.</summary>
    public void Reset()
    {
        _delay.CancelPending();
        Turns.Clear();
        _pendingQuestionIndex = 0;
        _questionOnTheTable = false;
        _handingOver = false;
        _tick = 0;
        Draft = "";
        IsStarted = false;
        IsDone = false;
        IsBusy = false;
        IsOpen = false;
        UnreadCount = 0;
        OnPropertyChanged(nameof(IsEmpty));
        RaiseDerived();
    }

    private void Think(int questionIndex, TimeSpan duration)
    {
        _delay.After(duration * 0.32, () => { _tick = 1; RaiseDerived(); });
        _delay.After(duration * 0.66, () => { _tick = 2; RaiseDerived(); });
        _delay.After(duration, () =>
        {
            _tick = 0;

            if (questionIndex < _questions.Count)
            {
                _pendingQuestionIndex = questionIndex;
                _questionOnTheTable = true;
                IsBusy = false;
                // The turn keeps the question's INDEX, not its sentence: a language switch
                // has to rewrite what is already on screen.
                Push(new ChatTurnViewModel(_strings, isBot: true, "", questionIndex: questionIndex));
                Draft = "";
                return;
            }

            // The brief is complete. The closing bubble is said, then the column goes back
            // to the wizard — and only now does the engine hear about any of this.
            IsBusy = false;
            IsDone = true;
            Push(new ChatTurnViewModel(_strings, isBot: true, "", isClosing: true));

            var answers = CollectedAnswers();
            _handingOver = true;
            RaiseDerived();
            _delay.After(ClosingPause, () =>
            {
                _handingOver = false;
                Close();
                _onInterviewComplete?.Invoke(answers);
            });
        });
    }

    private string[] CollectedAnswers()
    {
        var answers = new string[_questions.Count];
        foreach (var turn in Turns)
        {
            if (turn is { IsBot: false, AnswerIndex: { } index } && index < answers.Length)
                answers[index] = turn.Body;
        }

        return answers;
    }

    private void Send(string text)
    {
        var said = (text ?? "").Trim();

        // While the hand-off to the engine is pending, a Send would call CancelPending below
        // and take the composition with it — the thread would sit «done» and the engine would
        // never hear a word. The draft is kept, not swallowed: it goes out a second later.
        if (said.Length == 0 || _isBusy || _handingOver)
            return;

        _delay.CancelPending();

        // Mid-interview: the turn answers the question on the table, and the next one follows.
        if (_isStarted && !_isDone)
        {
            var index = _pendingQuestionIndex;
            Push(new ChatTurnViewModel(_strings, isBot: false, said, answerIndex: index));
            Draft = "";
            _questionOnTheTable = false;
            _pendingQuestionIndex = index + 1;
            _tick = 0;
            IsBusy = true;
            Think(_pendingQuestionIndex, NextThink);
            return;
        }

        // A free question. The engine wins whenever one is listening — a canned line would
        // be a downgrade of an answer the app already knows how to get. The bank speaks
        // only where there is no session at all.
        Push(new ChatTurnViewModel(_strings, isBot: false, said));
        Draft = "";

        if (_askEngine?.Invoke(said) == true)
        {
            IsBusy = true;
            return;
        }

        var answerKey = _answers.AnswerKey(said, _context);
        IsBusy = true;
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
        // The question being prepared goes with it — offering quick replies to a question
        // whose bubble was never pushed is an answer box for a question nobody asked.
        _delay.CancelPending();
        _handingOver = false;
        _questionOnTheTable = false;
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

    /// <summary>
    /// Closes the thread AND says why: the caller is asking to go back and change the brief,
    /// not merely to put the conversation away. Raising this from Close() instead made every
    /// dismissal of the panel — the header's cross included — claim the user wanted to edit.
    /// </summary>
    private void EditBrief()
    {
        Close();
        EditBriefRequested?.Invoke(this, EventArgs.Empty);
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
        string.Format(CultureInfo.CurrentCulture, _strings[key], arguments);

    private void RaiseDerived() => OnPropertiesChanged(
        nameof(IsAsking), nameof(Chips), nameof(Placeholder), nameof(SendLabel),
        nameof(Status), nameof(Thinking), nameof(StripTitle), nameof(StripAction),
        nameof(IsLive), nameof(Facts), nameof(RecapProgress), nameof(Brief),
        nameof(BriefChips), nameof(Primer));
}
