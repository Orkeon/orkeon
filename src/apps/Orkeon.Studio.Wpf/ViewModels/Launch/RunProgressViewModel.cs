using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Run;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Launch;

/// <summary>One finished task, as the progress panel shows it.</summary>
public sealed class RunTaskViewModel
{
    private readonly IStudioStrings _strings;

    /// <summary>Wraps what the run reported about one task.</summary>
    public RunTaskViewModel(RunTaskProgress task, IStudioStrings? strings = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        Task = task;
        _strings = strings ?? EnglishStudioStrings.Instance;
    }

    /// <summary>The underlying Core record.</summary>
    public RunTaskProgress Task { get; }

    /// <summary>The agent that ran it, or its identifier when the run named no agent.</summary>
    public string Title => Task.AgentRole ?? Task.TaskId ?? "—";

    /// <summary>Whether it succeeded.</summary>
    public bool Success => Task.Success;

    /// <summary>How long it took, in seconds, for a compact label.</summary>
    public string Duration =>
        string.Format(
            CultureInfo.CurrentCulture,
            _strings[StudioStringKeys.UsageSeconds],
            (Task.DurationMs / 1000.0).ToString("0.0", CultureInfo.CurrentCulture));

    /// <summary>Tokens spent, or an empty label when the run did not say.</summary>
    public string Tokens => Task.Tokens is { } tokens
        ? tokens.ToString("N0", CultureInfo.CurrentCulture)
        : string.Empty;
}

/// <summary>
/// The progress panel of a watched run (BUS-06): what finished, what it cost, and what the run
/// is asking.
/// <para>
/// The screen stops being a terminal here. The raw log does not disappear — what the stream
/// said stays available — but it drops behind the two things a user actually watches for:
/// whether the work is advancing, and whether it is waiting on them.
/// </para>
/// <para>
/// The folding itself lives in <see cref="RunProgressModel"/>, in Core, so the same reading
/// serves this screen, a terminal, and the tests. This type only projects it into bindable
/// collections.
/// </para>
/// </summary>
public sealed class RunProgressViewModel : ObservableObject
{
    private readonly IStudioStrings _strings;
    private RunProgressModel _model = new();
    private Func<string, string, bool>? _answer;
    private Func<string, string, bool>? _reply;
    private string _answerText = string.Empty;
    private string _replyText = string.Empty;

    /// <summary>Builds the panel over the localization port.</summary>
    public RunProgressViewModel(IStudioStrings? strings = null)
    {
        _strings = strings ?? EnglishStudioStrings.Instance;
        _strings.CultureChanged += (_, _) => RaiseLabels();

        AnswerCommand = new RelayCommand(Answer, () => PendingQuestion is not null);
        ChooseCommand = new RelayCommand(parameter => Choose(parameter as string), parameter => parameter is string);
        ReplyCommand = new RelayCommand(Reply, () => PendingAgentRequest is not null && _reply is not null);
    }

    /// <summary>Tasks the run has finished, in the order it reported them.</summary>
    public ObservableCollection<RunTaskViewModel> Tasks { get; } = [];

    /// <summary>Sends the typed answer to the waiting question.</summary>
    public RelayCommand AnswerCommand { get; }

    /// <summary>Sends one of the offered choices.</summary>
    public RelayCommand ChooseCommand { get; }

    /// <summary>Sends the typed reply to the agent waiting on this process.</summary>
    public RelayCommand ReplyCommand { get; }

    /// <summary>The question the run is waiting on, or null.</summary>
    public RunQuestion? PendingQuestion => _model.PendingQuestion;

    /// <summary>Whether a question is waiting — the panel's only modal-ish state.</summary>
    public bool IsAsking => PendingQuestion is not null;

    /// <summary>The question's wording.</summary>
    public string QuestionPrompt => PendingQuestion?.Prompt ?? string.Empty;

    /// <summary>The offered choices, empty for a free-text or yes/no question.</summary>
    public IReadOnlyList<string> QuestionChoices => PendingQuestion?.Choices ?? [];

    /// <summary>Whether the question offers a fixed set of answers.</summary>
    public bool IsChoice => PendingQuestion?.InputKind == RunQuestion.Choice;

    /// <summary>Whether the question is a yes/no.</summary>
    public bool IsConfirm => PendingQuestion?.InputKind == RunQuestion.Confirm;

    /// <summary>What the user typed, for a free-text question.</summary>
    public string AnswerText
    {
        get => _answerText;
        set => SetProperty(ref _answerText, value);
    }

    /// <summary>The agent request waiting for a reply, or null.</summary>
    public RunAgentRequest? PendingAgentRequest => _model.PendingAgentRequest;

    /// <summary>Whether an agent is waiting on this process — gates the request panel.</summary>
    public bool HasAgentRequest => PendingAgentRequest is not null;

    /// <summary>The asking agent's hub address, empty when the run did not say.</summary>
    public string AgentRequestFrom => PendingAgentRequest?.From ?? string.Empty;

    /// <summary>The request's payload, as the raw JSON the agent sent.</summary>
    public string AgentRequestPayload => PendingAgentRequest?.Payload ?? string.Empty;

    /// <summary>What the user typed as the reply.</summary>
    public string ReplyText
    {
        get => _replyText;
        set => SetProperty(ref _replyText, value);
    }

    /// <summary>Messages the run's hub relayed here, as one line each, oldest first.</summary>
    public ObservableCollection<string> HubMessages { get; } = [];

    /// <summary>Whether any hub message arrived — gates the hub panel's visibility.</summary>
    public bool HasHubMessages => HubMessages.Count > 0;

    /// <summary>The cost line, empty until the meter moves.</summary>
    public string CostSummary => _model.Cost is { } cost
        ? string.Format(
            CultureInfo.CurrentCulture,
            _strings[StudioStringKeys.RunProgressCost],
            cost.Tokens.ToString("N0", CultureInfo.CurrentCulture),
            cost.Model ?? "—")
        : string.Empty;

    /// <summary>
    /// The one-line state of the run. Deliberately says "nothing reported yet" rather than
    /// implying progress: no news is not the same as going well. A finished run appends
    /// what it cost — tokens, cache hit, duration — when the stream measured it (W-08).
    /// </summary>
    public string Summary
    {
        get
        {
            if (_model.Finished)
            {
                var verdict = _strings[_model.Success == true
                    ? StudioStringKeys.RunProgressSucceeded
                    : StudioStringKeys.RunProgressFailed];
                var chips = UsageMetricsFormatter.Chips(
                    _model.FinalTokens is > 0 ? _model.FinalTokens : null,
                    _model.FinalCacheHitTokens,
                    _model.FinalCacheMissTokens,
                    _model.FinalDurationMs,
                    _strings,
                    CultureInfo.CurrentCulture);
                return chips.Count > 0 ? $"{verdict} {string.Join(" · ", chips)}" : verdict;
            }

            if (Tasks.Count == 0)
                return _strings[StudioStringKeys.RunProgressNothingYet];

            return string.Format(
                CultureInfo.CurrentCulture,
                _strings[StudioStringKeys.RunProgressTasksDone],
                Tasks.Count);
        }
    }

    /// <summary>The last error the run reported, or an empty string.</summary>
    public string LastError => _model.LastError is { } error
        ? $"{error.Code}: {error.Message}"
        : string.Empty;

    /// <summary>Whether the run reported an error.</summary>
    public bool HasError => _model.LastError is not null;

    /// <summary>Text generated so far, when the run was asked to stream it.</summary>
    public string GeneratedText => _model.GeneratedText;

    /// <summary>Whether any generated text arrived — gates the stream panel's visibility.</summary>
    public bool HasGeneratedText => _model.GeneratedText.Length > 0;

    /// <summary>
    /// Prepares the panel for a new run and takes the channels it will answer on. A null
    /// channel means "watch only" — the panel then shows questions (or agent requests)
    /// without offering to answer, which is honest, rather than accepting an answer that
    /// goes nowhere.
    /// </summary>
    public void Reset(Func<string, string, bool>? answer, Func<string, string, bool>? reply = null)
    {
        _answer = answer;
        _reply = reply;
        Tasks.Clear();
        HubMessages.Clear();
        AnswerText = string.Empty;
        ReplyText = string.Empty;
        _model = new RunProgressModel();
        RaiseLabels();
    }

    /// <summary>
    /// Reads one line of the run's output. Returns true when it was a protocol event, so the
    /// caller knows whether to also show it raw — a line this panel could not read must never
    /// be lost.
    /// </summary>
    public bool TryApply(string line)
    {
        if (!OrkeonEventParser.TryParse(line, out var orkeonEvent))
            return false;

        var before = _model.Tasks.Count;
        var hubBefore = _model.HubMessages.Count;
        _model.Apply(orkeonEvent!);

        for (var index = before; index < _model.Tasks.Count; index++)
            Tasks.Add(new RunTaskViewModel(_model.Tasks[index], _strings));

        for (var index = hubBefore; index < _model.HubMessages.Count; index++)
            HubMessages.Add(Describe(_model.HubMessages[index]));

        // A delta arrives per token, on the UI thread. Re-raising the eleven labels plus the
        // command state for each one is thousands of change notifications per response;
        // generated text is the only thing a delta can move.
        if (string.Equals(orkeonEvent!.Kind, RunEventKinds.LlmDelta, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(GeneratedText));
            OnPropertyChanged(nameof(HasGeneratedText));
            return true;
        }

        RaiseLabels();
        return true;
    }

    private void Answer()
    {
        if (PendingQuestion is not { } question)
            return;

        var value = question.InputKind == RunQuestion.Confirm && string.IsNullOrWhiteSpace(AnswerText)
            ? "yes"
            : AnswerText;

        Send(question, value);
    }

    private void Choose(string? choice)
    {
        if (choice is null || PendingQuestion is not { } question)
            return;

        Send(question, choice);
    }

    private void Send(RunQuestion question, string value)
    {
        // A refused write means no child is listening. Clearing the question anyway would
        // pretend the answer landed, so the panel keeps showing it.
        if (_answer?.Invoke(question.CorrelationId, value) != true)
            return;

        AnswerText = string.Empty;
        _model.AnswerAccepted();
        RaiseLabels();
    }

    private void Reply()
    {
        if (PendingAgentRequest is not { } request)
            return;

        // Same refusal rule as Send: a write nobody read must keep the request on screen.
        if (_reply?.Invoke(request.CorrelationId, ReplyText) != true)
            return;

        ReplyText = string.Empty;
        _model.ReplyAccepted();
        RaiseLabels();
    }

    /// <summary>One hub message as the panel lists it: `[topic] from · payload`.</summary>
    internal static string Describe(RunHubMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var topic = message.Topic is { Length: > 0 } t ? $"[{t}] " : string.Empty;
        var from = message.From is { Length: > 0 } f ? $"{f} · " : string.Empty;
        return $"{topic}{from}{message.Payload ?? string.Empty}";
    }

    private void RaiseLabels()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CostSummary));
        OnPropertyChanged(nameof(PendingQuestion));
        OnPropertyChanged(nameof(IsAsking));
        OnPropertyChanged(nameof(QuestionPrompt));
        OnPropertyChanged(nameof(QuestionChoices));
        OnPropertyChanged(nameof(IsChoice));
        OnPropertyChanged(nameof(IsConfirm));
        OnPropertyChanged(nameof(LastError));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(GeneratedText));
        OnPropertyChanged(nameof(HasGeneratedText));
        OnPropertyChanged(nameof(PendingAgentRequest));
        OnPropertyChanged(nameof(HasAgentRequest));
        OnPropertyChanged(nameof(AgentRequestFrom));
        OnPropertyChanged(nameof(AgentRequestPayload));
        OnPropertyChanged(nameof(HasHubMessages));
        AnswerCommand.RaiseCanExecuteChanged();
        ReplyCommand.RaiseCanExecuteChanged();
    }
}
