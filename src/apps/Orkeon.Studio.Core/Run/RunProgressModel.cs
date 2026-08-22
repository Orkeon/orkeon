using Orkeon.Studio.Core.Events;

namespace Orkeon.Studio.Core.Run;

/// <summary>One task the run has finished.</summary>
public sealed record RunTaskProgress(
    string? TaskId,
    string? AgentRole,
    bool Success,
    long DurationMs,
    long? Tokens,
    int? ToolCalls);

/// <summary>What the run has spent so far.</summary>
public sealed record RunCost(long Tokens, string? Model, string? Provider);

/// <summary>A question a task is asking, waiting for this process to answer.</summary>
public sealed record RunQuestion(
    string CorrelationId,
    string InputKind,
    string Prompt,
    IReadOnlyList<string> Choices)
{
    /// <summary>Free text.</summary>
    public const string Text = "text";

    /// <summary>Yes or no.</summary>
    public const string Confirm = "confirm";

    /// <summary>One of <see cref="Choices"/>.</summary>
    public const string Choice = "choice";
}

/// <summary>An <c>error</c> event, as the screen will show it.</summary>
public sealed record RunErrorInfo(string Code, string Message, bool Recoverable);

/// <summary>Something the run's hub relayed to this process.</summary>
public sealed record RunHubMessage(string? From, string? Topic, string? CorrelationId, string? Payload);

/// <summary>
/// Folds a watched run's event stream into the state a screen shows (BUS-06).
/// <para>
/// It is deliberately a plain model rather than a view-model: the same folding serves the WPF
/// screen, a terminal, and the tests. Everything it exposes comes from an event — nothing is
/// inferred, so a screen never shows progress the run did not report.
/// </para>
/// <para>
/// A run that says nothing leaves this empty, which is the honest state: "no news" is not
/// "going well".
/// </para>
/// </summary>
public sealed class RunProgressModel
{
    private readonly List<RunTaskProgress> _tasks = [];
    private readonly List<RunHubMessage> _hubMessages = [];
    private readonly System.Text.StringBuilder _generated = new();

    /// <summary>Tasks finished so far, in the order the run reported them.</summary>
    public IReadOnlyList<RunTaskProgress> Tasks => _tasks;

    /// <summary>Messages the run's hub relayed to this process, oldest first.</summary>
    public IReadOnlyList<RunHubMessage> HubMessages => _hubMessages;

    /// <summary>The target the run opened with, when it said so.</summary>
    public string? Target { get; private set; }

    /// <summary>Whether the run was asked for token-by-token deltas.</summary>
    public bool Streaming { get; private set; }

    /// <summary>What has been spent, or null while the meter has not moved.</summary>
    public RunCost? Cost { get; private set; }

    /// <summary>The question waiting for an answer, or null when nothing is being asked.</summary>
    public RunQuestion? PendingQuestion => _questions.Count > 0 ? _questions[0] : null;

    private readonly List<RunQuestion> _questions = [];

    /// <summary>The last error reported, or null.</summary>
    public RunErrorInfo? LastError { get; private set; }

    /// <summary>Whether the run reported its own end.</summary>
    public bool Finished { get; private set; }

    /// <summary>The outcome, once <see cref="Finished"/>.</summary>
    public bool? Success { get; private set; }

    /// <summary>The exit code, once the run reported it.</summary>
    public int? ExitCode { get; private set; }

    /// <summary>Generated text accumulated from <c>llm.delta</c>, empty without <c>--stream</c>.</summary>
    public string GeneratedText => _generated.ToString();

    /// <summary>Raised after each applied event, so a screen can refresh once per change.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Folds one event in. Unknown kinds are ignored on purpose: a newer CLI may emit more
    /// than this build understands, and a screen that crashes on an unread event is worse than
    /// one that shows a little less.
    /// </summary>
    public void Apply(OrkeonEvent orkeonEvent)
    {
        ArgumentNullException.ThrowIfNull(orkeonEvent);

        switch (orkeonEvent.Kind)
        {
            case RunEventKinds.RunStarted:
                Target = orkeonEvent.GetString("target");
                Streaming = orkeonEvent.GetBool("stream") ?? false;
                break;

            case RunEventKinds.TaskCompleted:
                _tasks.Add(new RunTaskProgress(
                    orkeonEvent.GetString("taskId"),
                    orkeonEvent.GetString("agentRole"),
                    orkeonEvent.GetBool("success") ?? false,
                    orkeonEvent.GetInt64("durationMs") ?? 0,
                    orkeonEvent.GetInt64("tokens"),
                    (int?)orkeonEvent.GetInt64("toolCalls")));
                break;

            case RunEventKinds.CostUpdated:
                // tokens/model/provider are what the CLI actually emits. The first version
                // read `usd` and `budgetRemaining` — fields the writer never produced (its own
                // test pins their absence: the framework has no price table), so the screen
                // was built against a fiction.
                Cost = new RunCost(
                    orkeonEvent.GetInt64("tokens") ?? 0,
                    orkeonEvent.GetString("model"),
                    orkeonEvent.GetString("provider"));
                break;

            case RunEventKinds.LlmDelta:
                _generated.Append(orkeonEvent.GetString("text"));
                break;

            case RunEventKinds.InputNeeded:
                // Questions queue rather than overwrite: parallel tasks can ask concurrently,
                // and the first version kept a single slot — the second question clobbered
                // the first, which stayed unanswerable forever. A malformed question (no
                // correlation id — no address to answer to) changes nothing at all: it used
                // to CLEAR a legitimate question already on screen.
                if (ReadQuestion(orkeonEvent) is { } question)
                {
                    _questions.RemoveAll(q => string.Equals(q.CorrelationId, question.CorrelationId, StringComparison.Ordinal));
                    _questions.Add(question);
                }
                else
                {
                    return;
                }

                break;

            case RunEventKinds.InputGiven:
                // The answer is echoed by whoever sent it; that question is no longer pending.
                // An echo naming a question removes it alone; one naming nobody removes the
                // oldest — mirroring how the CLI pump spends an unnamed answer.
                if (orkeonEvent.CorrelationId is { } answered)
                    _questions.RemoveAll(q => string.Equals(q.CorrelationId, answered, StringComparison.Ordinal));
                else if (_questions.Count > 0)
                    _questions.RemoveAt(0);
                break;

            case RunEventKinds.HubMessage:
                _hubMessages.Add(new RunHubMessage(
                    orkeonEvent.GetString("from"),
                    orkeonEvent.GetString("topic"),
                    orkeonEvent.CorrelationId,
                    orkeonEvent.GetRawJson("payload")));
                break;

            case RunEventKinds.Error:
                LastError = new RunErrorInfo(
                    orkeonEvent.GetString("code") ?? "unknown",
                    orkeonEvent.GetString("message") ?? string.Empty,
                    orkeonEvent.GetBool("recoverable") ?? false);
                break;

            case RunEventKinds.RunFinished:
                Finished = true;
                Success = orkeonEvent.GetBool("success");
                ExitCode = (int?)orkeonEvent.GetInt64("exitCode");
                _questions.Clear();   // nobody is left to answer them
                break;

            default:
                return;   // nothing changed, so nothing to announce
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Clears the pending question because the answer was accepted by the run's stdin. The
    /// caller knows the write succeeded; the run itself does not echo the answer back, so
    /// waiting for an event that never comes would leave the question on screen forever.
    /// </summary>
    public void AnswerAccepted()
    {
        if (_questions.Count == 0)
            return;

        _questions.RemoveAt(0);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static RunQuestion? ReadQuestion(OrkeonEvent orkeonEvent)
    {
        var correlationId = orkeonEvent.CorrelationId;
        if (string.IsNullOrWhiteSpace(correlationId))
            return null;   // an answer needs an address; without one the screen cannot reply

        return new RunQuestion(
            correlationId,
            orkeonEvent.GetString("inputKind") ?? RunQuestion.Text,
            orkeonEvent.GetString("prompt") ?? string.Empty,
            orkeonEvent.GetStrings("choices"));
    }
}
