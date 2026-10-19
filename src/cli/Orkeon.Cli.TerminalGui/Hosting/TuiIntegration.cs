namespace Orkeon.Cli.TerminalGui.Hosting;

/// <summary>
/// A host-resolved progress readout for the status line: what long operation runs and
/// how far along it is. <see cref="Ratio"/> null ⇒ indeterminate (spinner + message,
/// no bar).
/// </summary>
public sealed record ProgressInfo
{
    /// <summary>Headline, e.g. <c>Compacting conversation</c>.</summary>
    public required string Label { get; init; }

    /// <summary>Completion in [0, 1], or null when the operation cannot measure one.</summary>
    public double? Ratio { get; init; }

    /// <summary>Current-phase line, e.g. <c>parsing sources</c>.</summary>
    public string? Message { get; init; }

    /// <summary>When the operation started — drives the elapsed readout.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// True when the host verified the snapshot belongs to a LIVE background instance.
    /// A foreground (unticketed) snapshot renders only while the runner executes a
    /// command — the staleness guard for a reporter that died between report and done.
    /// </summary>
    public bool FromLiveInstance { get; init; }
}

/// <summary>The state of a task row, which colours its bullet in the tasks list.</summary>
public enum TaskRowState
{
    /// <summary>Waiting or queued: dim.</summary>
    Idle,

    /// <summary>Working: green.</summary>
    Running,

    /// <summary>Finished: dim.</summary>
    Done,

    /// <summary>Failed: red.</summary>
    Failed,

    /// <summary>Cancelled: red.</summary>
    Cancelled,
}

/// <summary>One row of the agents pane, already host-resolved.</summary>
public sealed record AgentRowInfo
{
    /// <summary>Row label, e.g. <c>main</c> or <c>assistant@main-loop</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Free-text description (the command's intent); truncated at render time.</summary>
    public string Description { get; init; } = "";

    /// <summary>Wall-clock elapsed; null when not started or not meaningful.</summary>
    public TimeSpan? Elapsed { get; init; }

    /// <summary>Cumulated tokens attributed to this row; null ⇒ not attributable (render <c>—</c>).</summary>
    public long? Tokens { get; init; }

    /// <summary>
    /// True for the primary loop's <c>main</c> row. The bullet no longer follows it: the
    /// filled <c>●</c> marks the prompt's current recipient, every other row is hollow (<c>◯</c>).
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>The task's state: the bullet's colour (green running, dim idle or done, red failed or cancelled).</summary>
    public TaskRowState State { get; init; }

    /// <summary>Instance ticket, for selection → detail lookups. Null for <c>main</c>.</summary>
    public string? Ticket { get; init; }
}

/// <summary>
/// Host-supplied delegates the fidelity views pull from. This layer references only
/// <c>Orkeon.Cli.Abstractions</c>; the session state bag, the cost tracker and the
/// command-instance registry all live above it — so the HOST (ConsoleApp) resolves
/// them and plugs closures in here after building its service provider.
/// </summary>
/// <remarks>
/// Every delegate is optional and every reader treats a null (or a throw) as "not
/// available": the views degrade to omission, never to a crash — a status line that
/// cannot know the token count simply drops the token segment. Registered as a DI
/// singleton by <c>AddOrkeonCliTerminalGui</c>; populated once at startup.
/// </remarks>
public sealed class TuiIntegration
{
    /// <summary>The session's effective permission mode (hint-bar posture).</summary>
    public Func<string?>? PermissionMode { get; set; }

    /// <summary>Cycle to the next permission mode (hint bar's Shift+Tab).</summary>
    public Action? CyclePermissionMode { get; set; }

    /// <summary>Cumulated session tokens (real usage, cost tracker). Null ⇒ segment omitted.</summary>
    public Func<long?>? SessionTokens { get; set; }

    /// <summary>Rows for the agents pane. Null/empty ⇒ pane collapses to zero rows.</summary>
    public Func<IReadOnlyList<AgentRowInfo>>? AgentRows { get; set; }

    /// <summary>Cancel the in-flight async work (hint bar's Esc during a turn).</summary>
    public Action? InterruptCurrent { get; set; }

    /// <summary>True while a dispatched prompt turn runs.</summary>
    public Func<bool>? PromptTurnRunning { get; set; }

    /// <summary>The selected model and its availability. Null result shows "no model".</summary>
    public Func<Orkeon.Cli.TerminalGui.Layout.ModelWitnessInfo?>? ModelWitness { get; set; }

    /// <summary>The long operation currently reporting progress. Null ⇒ no bar.</summary>
    public Func<ProgressInfo?>? Progress { get; set; }

    /// <summary>
    /// Spinner-verb rotation for the status line (the <c>spinnerVerbs</c> setting).
    /// Null/empty ⇒ the built-in gerunds. Read live so a <c>/config set</c> applies
    /// without a restart.
    /// </summary>
    public Func<IReadOnlyList<string>?>? SpinnerVerbs { get; set; }

    /// <summary>
    /// Renders the detail of one agents-pane row (by ticket) for the transcript —
    /// the pane's Enter/double-click action. Null ⇒ selection shows no detail.
    /// </summary>
    public Func<string, string?>? DescribeAgent { get; set; }

    /// <summary>
    /// Delivers a prompt line to a task of the agents pane: <c>(ticket, text)</c>, true when the
    /// task took it. Selecting a task in the list makes it the prompt's recipient; null (the
    /// case while no task is addressable) ⇒ Enter prints that sending to a task is not
    /// supported yet and keeps the text in the field.
    /// </summary>
    public Func<string, string, bool>? SendToTask { get; set; }

    /// <summary>
    /// The background tasks that reached an end (done, failed, cancelled), with their final
    /// <see cref="AgentRowInfo.State"/> and frozen <see cref="AgentRowInfo.Elapsed"/>, oldest
    /// first. Read after <see cref="AgentRows"/> at each poll: the transcript says how each task
    /// ended, and a task that started and ended between two polls still gets its launch and end
    /// lines. Null ⇒ a task that leaves the list reads as finished, and one too short for a poll
    /// leaves no line.
    /// </summary>
    public Func<IReadOnlyList<AgentRowInfo>>? FinishedAgentRows { get; set; }
}
