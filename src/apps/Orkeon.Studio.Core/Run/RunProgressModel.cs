using System.Globalization;
using Orkeon.Studio.Core.Events;

namespace Orkeon.Studio.Core.Run;

/// <summary>
/// One task the run has started and not yet reported finished (STUDIO-17). Deliberately thin:
/// the start says which task, whose turn, and when — nothing has been measured yet.
/// </summary>
public sealed record RunTaskInFlight(string? TaskId, string? AgentRole, DateTimeOffset? StartedAt);

/// <summary>
/// One tool call the run announced (<c>tool.called</c>) and has not seen return (STUDIO-30).
/// </summary>
/// <param name="ToolName">The tool at work.</param>
/// <param name="StartedAt">The run's own clock at the call; null when it did not parse.</param>
public sealed record RunToolInFlight(string ToolName, DateTimeOffset? StartedAt);

/// <summary>
/// One delegation under way (STUDIO-30): an agent handed work over and the delegate has not
/// come back. The CLI reports it with <c>delegation.started</c> instead of a <c>tool.called</c>
/// and closes it with the <c>tool.returned</c> every call gets — there is no
/// <c>delegation.finished</c>.
/// </summary>
/// <param name="ToRole">The coworker the work went to, when the call named one.</param>
/// <param name="StartedAt">The run's own clock at the handover; null when it did not parse.</param>
public sealed record RunDelegationInFlight(string? ToRole, DateTimeOffset? StartedAt);

/// <summary>One agent the team grew by at runtime (<c>agent.spawned</c>, STUDIO-30).</summary>
/// <param name="Role">The new agent's role, when the call named one.</param>
/// <param name="Reason">What it was spawned for — the spawn call's goal — when stated.</param>
/// <param name="SpawnedAt">The run's own clock at the spawn; null when it did not parse.</param>
public sealed record RunSpawnedAgent(string? Role, string? Reason, DateTimeOffset? SpawnedAt);

/// <summary>One task the run has finished.</summary>
public sealed record RunTaskProgress(
    string? TaskId,
    string? AgentRole,
    bool Success,
    long DurationMs,
    long? Tokens,
    int? ToolCalls,
    bool Skipped = false);

/// <summary>
/// What the run has spent so far, as its latest <c>cost.updated</c> said it — every figure
/// cumulative. A field the line did not carry stays null: an older CLI sends no split, a
/// provider that measures no cache sends no cache pair, and a vendor that bills nothing in
/// its answers sends no amount. None of them is a zero.
/// </summary>
/// <param name="Tokens">Both directions together.</param>
/// <param name="Model">The model that answered last, when reported.</param>
/// <param name="Provider">The provider that answered last, when reported.</param>
/// <param name="PromptTokens">What went up (↑).</param>
/// <param name="CompletionTokens">What came back (↓).</param>
/// <param name="CacheHitTokens">Prompt tokens served from the provider's cache — a partition of <paramref name="PromptTokens"/>.</param>
/// <param name="CacheMissTokens">Prompt tokens the provider had to compute.</param>
/// <param name="Amount">What the vendor billed, as billed (0 for a free call) — never an estimate.</param>
/// <param name="Currency">The ISO 4217 code of <paramref name="Amount"/>, when stated.</param>
/// <param name="Source">Who stated <paramref name="Amount"/>: <c>vendor</c>, the one source the CLI emits.</param>
public sealed record RunCost(
    long Tokens,
    string? Model,
    string? Provider,
    long? PromptTokens,
    long? CompletionTokens,
    long? CacheHitTokens,
    long? CacheMissTokens,
    decimal? Amount,
    string? Currency,
    string? Source);

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
/// What <see cref="RunProgressModel.Changed"/> says about a change (STUDIO-30): which kind of
/// event moved the state.
/// </summary>
public sealed class RunProgressChangedEventArgs : EventArgs
{
    /// <summary>Names the kind that moved the state; null for a change made on this side.</summary>
    public RunProgressChangedEventArgs(string? kind) => Kind = kind;

    /// <summary>
    /// The kind of the event that moved the state (one of <see cref="RunEventKinds"/>), or null
    /// when the change was made on this side — an answer or a reply the run's stdin accepted.
    /// </summary>
    public string? Kind { get; }
}

/// <summary>
/// Folds a watched run's event stream into the state a screen shows (BUS-06).
/// <para>
/// It is deliberately a plain model rather than a view-model: the same folding serves the WPF
/// screen, a terminal, and the tests. Everything it exposes comes from an event — nothing is
/// inferred, so a screen never shows progress the run did not report. Its one reading of this
/// machine's clock, <see cref="Elapsed"/>, counts from the start the run itself stamped.
/// </para>
/// <para>
/// It holds the whole live state of one run (STUDIO-30) — tasks and tools at work with their
/// start, delegations under way, agents spawned, whether the run waits on an answer, what it
/// spent, how long it has gone — so a status bar reads it rather than the raw stream.
/// </para>
/// <para>
/// A run that says nothing leaves this empty, which is the honest state: "no news" is not
/// "going well".
/// </para>
/// </summary>
public sealed class RunProgressModel
{
    private readonly TimeProvider _time;
    private readonly List<RunTaskProgress> _tasks = [];
    private readonly List<RunTaskInFlight> _running = [];
    private readonly InFlight<RunToolInFlight> _activeTools = new();
    private readonly List<RunToolInFlight> _unfinishedTools = [];
    private readonly InFlight<RunDelegationInFlight> _activeDelegations = new();
    private readonly List<RunDelegationInFlight> _unfinishedDelegations = [];
    private readonly List<RunSpawnedAgent> _spawnedAgents = [];
    private readonly List<RunHubMessage> _hubMessages = [];
    private readonly System.Text.StringBuilder _generated = new();
    private DateTimeOffset? _finishedAt;

    /// <summary>
    /// Builds an empty model. <paramref name="timeProvider"/> is the clock <see cref="Elapsed"/>
    /// reads while the run goes: the system's by default, a fixed one in tests.
    /// </summary>
    public RunProgressModel(TimeProvider? timeProvider = null) =>
        _time = timeProvider ?? TimeProvider.System;

    /// <summary>Tasks finished so far, in the order the run reported them.</summary>
    public IReadOnlyList<RunTaskProgress> Tasks => _tasks;

    /// <summary>
    /// Tasks started and not yet finished, oldest first — what a screen shows as "in progress"
    /// (STUDIO-17). Empty on an older CLI that announces no starts: the screen then shows
    /// finished tasks only, as it always did, rather than a guess.
    /// </summary>
    public IReadOnlyList<RunTaskInFlight> RunningTasks => _running;

    /// <summary>
    /// Every tool at work — called and not yet returned — oldest first, each with the run's own
    /// clock at the call (STUDIO-30). Parallel calls are all here, not only the latest: a return
    /// closes the call its correlation id names, in whatever order the returns come back.
    /// </summary>
    public IReadOnlyList<RunToolInFlight> ActiveTools => _activeTools.Calls;

    /// <summary>
    /// The tool most recently called and not yet returned — the last of
    /// <see cref="ActiveTools"/> — or null when no tool is at work.
    /// </summary>
    public string? ActiveToolName => _activeTools.Calls.Count > 0 ? _activeTools.Calls[^1].ToolName : null;

    /// <summary>
    /// Tool calls the run announced so far. Each one is in exactly one place:
    /// <see cref="ActiveTools"/>, <see cref="SucceededToolCalls"/>, <see cref="FailedToolCalls"/>
    /// or <see cref="UnfinishedTools"/>. A delegation or a spawn is a tool call underneath, but
    /// the run reports it under its own kind, and it is not counted here.
    /// </summary>
    public int ToolCallCount { get; private set; }

    /// <summary>Tool calls whose return said <c>success: true</c>.</summary>
    public int SucceededToolCalls { get; private set; }

    /// <summary>Tool calls whose return said anything else — a tool that threw included.</summary>
    public int FailedToolCalls { get; private set; }

    /// <summary>
    /// Tools still at work when the run reported its end: they never returned, so none of them
    /// is a success. Kept aside rather than dropped — dropped, a call that never came back would
    /// read as one that went well. Empty until the run ends.
    /// </summary>
    public IReadOnlyList<RunToolInFlight> UnfinishedTools => _unfinishedTools;

    /// <summary>
    /// Delegations under way — handed over and not yet come back — oldest first (STUDIO-30).
    /// </summary>
    public IReadOnlyList<RunDelegationInFlight> ActiveDelegations => _activeDelegations.Calls;

    /// <summary>Delegations still under way when the run reported its end — not finished, like <see cref="UnfinishedTools"/>.</summary>
    public IReadOnlyList<RunDelegationInFlight> UnfinishedDelegations => _unfinishedDelegations;

    /// <summary>
    /// Agents the team grew by at runtime, in the order the run announced them (STUDIO-30). The
    /// announcement goes out with the spawn call, and whatever that call returns leaves the
    /// agent counted: a spawn that waits for its agent reports that agent's own failure.
    /// </summary>
    public IReadOnlyList<RunSpawnedAgent> SpawnedAgents => _spawnedAgents;

    /// <summary>Messages the run's hub relayed to this process, oldest first.</summary>
    public IReadOnlyList<RunHubMessage> HubMessages => _hubMessages;

    /// <summary>The target the run opened with, when it said so.</summary>
    public string? Target { get; private set; }

    /// <summary>Whether the run was asked for token-by-token deltas.</summary>
    public bool Streaming { get; private set; }

    /// <summary>
    /// When the run said it started — the envelope <c>ts</c> of its <c>run.started</c>; null
    /// before that line, or when its clock did not parse.
    /// </summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>
    /// How long the run has been going (STUDIO-30). Once it reported its end, its own wall
    /// time — <see cref="FinalDurationMs"/>, else the span between its two stamps — frozen there.
    /// Before, the time since <see cref="StartedAt"/> on this machine's clock: a reading rather
    /// than an event, so it moves without <see cref="Changed"/>, and a screen refreshes it on
    /// its own timer. Null when nothing gives a start to count from.
    /// </summary>
    public TimeSpan? Elapsed
    {
        get
        {
            if (Finished)
                return FinalDurationMs is { } durationMs ? TimeSpan.FromMilliseconds(durationMs) : _finishedAt - StartedAt;

            if (StartedAt is not { } started)
                return null;

            // A clock set back since the start must not show a negative time.
            var elapsed = _time.GetUtcNow() - started;
            return elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero;
        }
    }

    /// <summary>What has been spent, or null while the meter has not moved.</summary>
    public RunCost? Cost { get; private set; }

    /// <summary>The question waiting for an answer, or null when nothing is being asked.</summary>
    public RunQuestion? PendingQuestion => _questions.Count > 0 ? _questions[0] : null;

    private readonly List<RunQuestion> _questions = [];

    /// <summary>The agent request waiting for a reply, or null when no agent is asking.</summary>
    public RunAgentRequest? PendingAgentRequest => _agentRequests.Count > 0 ? _agentRequests[0] : null;

    private readonly List<RunAgentRequest> _agentRequests = [];

    /// <summary>
    /// Whether the run is waiting on this process — a question for a human, or an agent's
    /// request for a reply (STUDIO-30).
    /// </summary>
    public bool IsWaitingForAnswer => _questions.Count > 0 || _agentRequests.Count > 0;

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

    /// <summary>
    /// Raised after each applied event, so a screen can refresh once per change. It names the
    /// kind that moved the state (STUDIO-30): a token delta arrives per token, and a screen that
    /// shows no generated text can skip it rather than repaint for each one.
    /// </summary>
    public event EventHandler<RunProgressChangedEventArgs>? Changed;

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
                StartedAt = ReadTimestamp(orkeonEvent);
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

                _activeTools.Open(ToolKey(orkeonEvent), new RunToolInFlight(calledTool, ReadTimestamp(orkeonEvent)));
                ToolCallCount++;
                break;

            case RunEventKinds.ToolReturned:
                if (!Returned(orkeonEvent))
                    return;   // a return nothing was waiting for changes nothing

                break;

            case RunEventKinds.DelegationStarted:
                // Reported instead of the delegation's tool.called, and closed by that call's
                // tool.returned — there is no delegation.finished. A handover naming no
                // coworker is still one under way.
                _activeDelegations.Open(ToolKey(orkeonEvent), new RunDelegationInFlight(
                    orkeonEvent.GetString("toRole"),
                    ReadTimestamp(orkeonEvent)));
                break;

            case RunEventKinds.AgentSpawned:
                _spawnedAgents.Add(new RunSpawnedAgent(
                    orkeonEvent.GetString("role"),
                    orkeonEvent.GetString("reason"),
                    ReadTimestamp(orkeonEvent)));
                break;

            case RunEventKinds.CostUpdated:
                // Only what the CLI actually emits. The first version read `usd` and
                // `budgetRemaining` — fields the writer never produced — so the screen was
                // built against a fiction. The split and the vendor's charge arrive with every
                // reading since STUDIO-29; a field a line leaves out stays null here.
                Cost = new RunCost(
                    orkeonEvent.GetInt64("tokens") ?? 0,
                    orkeonEvent.GetString("model"),
                    orkeonEvent.GetString("provider"),
                    orkeonEvent.GetInt64("promptTokens"),
                    orkeonEvent.GetInt64("completionTokens"),
                    orkeonEvent.GetInt64("cacheHitTokens"),
                    orkeonEvent.GetInt64("cacheMissTokens"),
                    orkeonEvent.GetDecimal("cost"),
                    orkeonEvent.GetString("currency"),
                    orkeonEvent.GetString("costSource"));
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
                _finishedAt = ReadTimestamp(orkeonEvent);
                _questions.Clear();       // nobody is left to answer them
                _agentRequests.Clear();   // the asking agents are gone with the run
                _running.Clear();         // nothing is in progress once the run has ended

                // A call still open now never came back. It leaves what is at work, but is set
                // aside as not finished rather than dropped, where it would read as a success.
                _activeTools.AbandonInto(_unfinishedTools);
                _activeDelegations.AbandonInto(_unfinishedDelegations);
                break;

            default:
                return;   // nothing changed, so nothing to announce
        }

        Changed?.Invoke(this, new RunProgressChangedEventArgs(orkeonEvent.Kind));
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
        Changed?.Invoke(this, new RunProgressChangedEventArgs(kind: null));
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
        Changed?.Invoke(this, new RunProgressChangedEventArgs(kind: null));
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

    /// <summary>
    /// Pairs a return with its call — by correlation id, a delegation or a tool; else the latest
    /// call of that tool — and counts a tool call's outcome. False when nothing matched.
    /// </summary>
    private bool Returned(OrkeonEvent orkeonEvent)
    {
        var id = orkeonEvent.CorrelationId is { Length: > 0 } correlationId ? correlationId : null;

        // The delegate came back. Its return closes the delegation, and counts as no tool call.
        if (id is not null && _activeDelegations.Close(id))
            return true;

        var name = orkeonEvent.GetString("toolName");
        if (!(id is not null && _activeTools.Close(id))
            && !_activeTools.CloseLatest(t => string.Equals(t.ToolName, name, StringComparison.Ordinal)))
        {
            return false;
        }

        // A success only when the return says so: a return that says nothing is not one.
        if (orkeonEvent.GetBool("success") == true)
            SucceededToolCalls++;
        else
            FailedToolCalls++;

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

    /// <summary>
    /// Calls announced and not yet returned, oldest first, each filed under its identity on the
    /// wire — so a return closes the very call it answers, whatever order the returns come in.
    /// </summary>
    private sealed class InFlight<T>
    {
        private readonly List<string> _keys = [];

        /// <summary>The open calls, oldest first.</summary>
        public List<T> Calls { get; } = [];

        /// <summary>Files <paramref name="call"/> under <paramref name="key"/>, as the latest open call.</summary>
        public void Open(string key, T call)
        {
            _keys.Add(key);
            Calls.Add(call);
        }

        /// <summary>Closes the latest call filed under <paramref name="key"/>; false when none is open.</summary>
        public bool Close(string key) => CloseAt(_keys.LastIndexOf(key));

        /// <summary>Closes the latest call <paramref name="match"/> accepts; false when none does.</summary>
        public bool CloseLatest(Predicate<T> match) => CloseAt(Calls.FindLastIndex(match));

        /// <summary>Hands every open call to <paramref name="abandoned"/> and closes them all.</summary>
        public void AbandonInto(List<T> abandoned)
        {
            abandoned.AddRange(Calls);
            Calls.Clear();
            _keys.Clear();
        }

        private bool CloseAt(int index)
        {
            if (index < 0)
                return false;

            _keys.RemoveAt(index);
            Calls.RemoveAt(index);
            return true;
        }
    }
}
