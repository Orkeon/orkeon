namespace Orkeon.Cli.TerminalGui.Hosting;

/// <summary>The context chip's content: what the prompt is currently "at".</summary>
/// <param name="Text">Chip label, e.g. <c>@verif-proxy</c> or the session title.</param>
/// <param name="IsAgentTarget">True ⇒ agent-target styling (yellow); false ⇒ session (blue).</param>
public sealed record ContextChipInfo(string Text, bool IsAgentTarget);

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

    /// <summary>True ⇒ filled bullet + emphasised row (the reference's selected row).</summary>
    public bool IsActive { get; init; }

    /// <summary>True ⇒ the row shows <c>idle</c> instead of metrics.</summary>
    public bool IsIdle { get; init; }

    /// <summary>
    /// Lifecycle token of the underlying instance — <c>running</c>/<c>done</c>/<c>failed</c>/
    /// <c>cancelled</c>/<c>rejected</c>, or null for rows with no instance (<c>main</c>).
    /// Terminal tokens change the metric block: a finished agent must read as finished,
    /// never as <c>idle</c>.
    /// </summary>
    public string? Status { get; init; }

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

    /// <summary>What the context chip shows. Null ⇒ chip omitted (bare rule).</summary>
    public Func<ContextChipInfo?>? ContextChip { get; set; }

    /// <summary>Rows for the agents pane. Null/empty ⇒ pane collapses to zero rows.</summary>
    public Func<IReadOnlyList<AgentRowInfo>>? AgentRows { get; set; }

    /// <summary>Cancel the in-flight async work (hint bar's Esc during a turn).</summary>
    public Action? InterruptCurrent { get; set; }

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
}
