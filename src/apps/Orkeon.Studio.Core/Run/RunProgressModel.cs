using System.Globalization;
using Orkeon.Studio.Core.Events;

namespace Orkeon.Studio.Core.Run;

/// <summary>
/// One task the run has started and not yet reported finished (STUDIO-17). Deliberately thin:
/// the start says which task, whose turn, and when — nothing has been measured yet.
/// </summary>
public sealed record RunTaskInFlight(string? TaskId, string? AgentRole, DateTimeOffset? StartedAt);

/// <summary>One task the run has finished.</summary>
public sealed record RunTaskProgress(
    string? TaskId,
    string? AgentRole,
    bool Success,
    long DurationMs,
    long? Tokens,
    int? ToolCalls,
    bool Skipped = false);

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
/// A <c>send</c> an agent addressed to this process, waiting for a <c>reply</c>. The agent
/// is blocked on it until its own timeout; silence past that is a refusal.
/// </summary>
public sealed record RunAgentRequest(string CorrelationId, string? From, string? Payload);

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
    private readonly List<RunTaskInFlight> _running = [];
    private readonly List<(string Key, string Tool)> _activeTools = [];
    private readonly List<RunHubMessage> _hubMessages = [];
    private readonly System.Text.StringBuilder _generated = new();

    /// <summary>Tasks finished so far, in the order the run reported them.</summary>
    public IReadOnlyList<RunTaskProgress> Tasks => _tasks;

    /// <summary>
    /// Tasks started and not yet finished, oldest first — what a screen shows as "in progress"
    /// (STUDIO-17). Empty on an older CLI that announces no starts: the screen then shows
    /// finished tasks only, as it always did, rather than a guess.
    /// </summary>
    public IReadOnlyList<RunTaskInFlight> RunningTasks => _running;

    /// <summary>
    /// The tool most recently called and not yet returned, or null when no tool is at work.
    /// Straight from <c>tool.called</c> / <c>tool.returned</c>, paired by correlation id.
    /// </summary>
    public string? ActiveToolName => _activeTools.Count > 0 ? _activeTools[^1].Tool : null;

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

    /// <summary>The agent request waiting for a reply, or null when no agent is asking.</summary>
    public RunAgentRequest? PendingAgentRequest => _agentRequests.Count > 0 ? _agentRequests[0] : null;

    private readonly List<RunAgentRequest> _agentRequests = [];

    /// <summary>The last error reported, or null.</summary>
    public RunErrorInfo? LastError { get; private set; }

    /// <summary>Whether the run reported its own end.</summary>
    public bool Finished { get; private set; }

    /// <summary>The outcome, once <see cref="Finished"/>.</summary>
    public bool? Success { get; private set; }

    /// <summary>The exit code, once the run reported it.</summary>
    public int? ExitCode { get; private set; }

    /// <summary>Total tokens the run reported at its close; null before, or on an older CLI (W-08).</summary>
    public long? FinalTokens { get; private set; }

    /// <summary>Wall time the run reported at its close, milliseconds; null when unsaid.</summary>
    public long? FinalDurationMs { get; private set; }

    /// <summary>Cache-served prompt tokens at the close — a partition of the prompt side; null when unmeasured.</summary>
    public long? FinalCacheHitTokens { get; private set; }

    /// <summary>Cache-missed prompt tokens at the close; null when unmeasured.</summary>
    public long? FinalCacheMissTokens { get; private set; }

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

            case RunEventKinds.TaskStarted:
                // The run's own clock, when it parses; null otherwise — a screen shows "since
                // HH:mm:ss" or nothing, never a time it made up.
                _running.Add(new RunTaskInFlight(
                    orkeonEvent.GetString("taskId"),
                    orkeonEvent.GetString("agentRole"),
                    ReadTimestamp(orkeonEvent)));
                break;

            case RunEventKinds.TaskCompleted:
                var finished = new RunTaskProgress(
                    orkeonEvent.GetString("taskId"),
                    orkeonEvent.GetString("agentRole"),
                    orkeonEvent.GetBool("success") ?? false,
                    orkeonEvent.GetInt64("durationMs") ?? 0,
                    orkeonEvent.GetInt64("tokens"),
                    (int?)orkeonEvent.GetInt64("toolCalls"),
                    // A task that never ran because a dependency failed (LLM-11): not started,
                    // completed with success=false, and told apart from a failure by this flag.
                    orkeonEvent.GetBool("skipped") ?? false);
                _tasks.Add(finished);
                Settle(finished.TaskId, finished.AgentRole);
                break;

            case RunEventKinds.ToolCalled:
                if (orkeonEvent.GetString("toolName") is not { Length: > 0 } calledTool)
                    return;   // a call that names no tool is nothing a screen can show

                _activeTools.Add((ToolKey(orkeonEvent), calledTool));
                break;

            case RunEventKinds.ToolReturned:
                if (!ToolReturned(orkeonEvent))
                    return;   // a return nothing was waiting for changes nothing

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

                // A send says so explicitly (`expectsReply`): the peer must not guess which
                // correlated lines are questions — a topic relay can carry a correlationId
                // too. Requests queue like human questions do, and a line with no
                // correlation id offers no address to reply to, so it stays journal-only.
                if (orkeonEvent.GetBool("expectsReply") == true
                    && orkeonEvent.CorrelationId is { } requestId
                    && !string.IsNullOrWhiteSpace(requestId))
                {
                    _agentRequests.RemoveAll(r => string.Equals(r.CorrelationId, requestId, StringComparison.Ordinal));
                    _agentRequests.Add(new RunAgentRequest(
                        requestId,
                        orkeonEvent.GetString("from"),
                        orkeonEvent.GetRawJson("payload")));
                }

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
                FinalTokens = orkeonEvent.GetInt64("tokens");
                FinalDurationMs = orkeonEvent.GetInt64("durationMs");
                FinalCacheHitTokens = orkeonEvent.GetInt64("cacheHitTokens");
                FinalCacheMissTokens = orkeonEvent.GetInt64("cacheMissTokens");
                _questions.Clear();       // nobody is left to answer them
                _agentRequests.Clear();   // the asking agents are gone with the run
                _running.Clear();         // nothing is in progress once the run has ended
                _activeTools.Clear();
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

    /// <summary>
    /// Clears the pending agent request because the reply was accepted by the run's stdin.
    /// The bridge does not echo replies back (it settles the agent's await directly), so —
    /// like <see cref="AnswerAccepted"/> — waiting for an event would leave the request on
    /// screen forever.
    /// </summary>
    public void ReplyAccepted()
    {
        if (_agentRequests.Count == 0)
            return;

        _agentRequests.RemoveAt(0);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Ends the in-flight entry a close refers to. By task and agent first; by task alone next
    /// — the graph and autonomous modes announce the start under the agent's role and report
    /// the close under the mode's name; the oldest start last, for a close naming nothing known.
    /// A close on an older CLI that announced no start finds the list empty and changes it not.
    /// </summary>
    private void Settle(string? taskId, string? agentRole)
    {
        if (_running.Count == 0)
            return;

        var index = _running.FindIndex(t => Same(t.TaskId, taskId) && Same(t.AgentRole, agentRole));
        if (index < 0)
            index = _running.FindIndex(t => Same(t.TaskId, taskId));
        if (index < 0)
            index = 0;

        _running.RemoveAt(index);
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    /// <summary>A call's identity on the wire: its correlation id, or its sequence number when it carries none.</summary>
    private static string ToolKey(OrkeonEvent orkeonEvent) =>
        orkeonEvent.CorrelationId is { Length: > 0 } id
            ? id
            : orkeonEvent.Seq.ToString(CultureInfo.InvariantCulture);

    /// <summary>Pairs a return with its call — by correlation id, else the latest call of that tool. False when nothing matched.</summary>
    private bool ToolReturned(OrkeonEvent orkeonEvent)
    {
        if (orkeonEvent.CorrelationId is { Length: > 0 } id)
        {
            var byId = _activeTools.FindLastIndex(t => string.Equals(t.Key, id, StringComparison.Ordinal));
            if (byId >= 0)
            {
                _activeTools.RemoveAt(byId);
                return true;
            }
        }

        var name = orkeonEvent.GetString("toolName");
        var byName = _activeTools.FindLastIndex(t => string.Equals(t.Tool, name, StringComparison.Ordinal));
        if (byName < 0)
            return false;

        _activeTools.RemoveAt(byName);
        return true;
    }

    private static DateTimeOffset? ReadTimestamp(OrkeonEvent orkeonEvent) =>
        DateTimeOffset.TryParse(
            orkeonEvent.GetString("ts"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var when)
            ? when
            : null;

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
