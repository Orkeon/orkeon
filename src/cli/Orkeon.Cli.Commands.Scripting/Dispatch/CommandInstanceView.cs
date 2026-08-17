namespace Orkeon.Cli.Commands.Scripting.Dispatch;

/// <summary>
/// Stable identity of a command instance: the addressing/dispatch coordinates that never
/// change once the instance is registered. Groups the descriptive fields of
/// <see cref="CommandInstanceView"/> to keep its constructor narrow.
/// </summary>
internal readonly record struct CommandInstanceIdentity(
    string Ticket,
    string Name,
    string Kind,
    string TargetAgent,
    string Intent,
    string CorrelationId);

/// <summary>
/// Lifecycle/outcome of a command instance at snapshot time: state token, timing, and the
/// terminal result/error/progress. Groups the volatile fields of
/// <see cref="CommandInstanceView"/> to keep its constructor narrow.
/// </summary>
internal readonly record struct CommandInstanceLifecycle(
    string State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long ElapsedMs,
    CommandResponse? Result,
    string? Error,
    CommandProgress? Progress,
    long Tokens);

#pragma warning disable IDE1006 // camelCase: reflected to JS as the entries returned by commands.list()/get()
/// <summary>
/// Immutable snapshot of a <see cref="CommandInstance"/> exposed to scripts via
/// <c>commands.list()</c> / <c>commands.get()</c> and rendered by the built-in
/// <c>ps</c>/<c>inspect</c> commands (design §6).
/// </summary>
public sealed class CommandInstanceView
{
    internal CommandInstanceView(CommandInstanceIdentity identity, CommandInstanceLifecycle lifecycle)
    {
        this.ticket = identity.Ticket;
        this.name = identity.Name;
        this.kind = identity.Kind;
        this.targetAgent = identity.TargetAgent;
        this.intent = identity.Intent;
        this.correlationId = identity.CorrelationId;
        this.state = lifecycle.State;
        this.startedAt = lifecycle.StartedAt.ToString("O");
        this.completedAt = lifecycle.CompletedAt?.ToString("O");
        this.elapsedMs = lifecycle.ElapsedMs;
        this.result = lifecycle.Result;
        this.error = lifecycle.Error;
        this.progress = lifecycle.Progress;
        this.tokens = lifecycle.Tokens;
    }

    /// <summary>Opaque handle identifying this in-flight instance.</summary>
    public string ticket { get; }

    /// <summary>Command name that produced this instance.</summary>
    public string name { get; }

    /// <summary><c>"sync"</c> or <c>"async"</c>.</summary>
    public string kind { get; }

    /// <summary>Logical name of the addressed agent.</summary>
    public string targetAgent { get; }

    /// <summary>Intent the command was dispatched with.</summary>
    public string intent { get; }

    /// <summary>Channel correlation id (host-side completion key).</summary>
    public string correlationId { get; }

    /// <summary>Lowercase lifecycle token: running/done/failed/cancelled/rejected.</summary>
    public string state { get; }

    /// <summary>ISO-8601 start timestamp.</summary>
    public string startedAt { get; }

    /// <summary>ISO-8601 completion timestamp, or <see langword="null"/> while running.</summary>
    public string? completedAt { get; }

    /// <summary>Elapsed wall time in milliseconds (live while running, frozen once terminal).</summary>
    public long elapsedMs { get; }

    /// <summary>The agent response when the instance reached <c>done</c>; otherwise <see langword="null"/>.</summary>
    public CommandResponse? result { get; }

    /// <summary>Failure / rejection reason; otherwise <see langword="null"/>.</summary>
    public string? error { get; }

    /// <summary>Latest progress snapshot published by the agent, if any.</summary>
    public CommandProgress? progress { get; }

    /// <summary>LLM tokens attributed to this instance so far; 0 when none were observed.</summary>
    public long tokens { get; }
}
#pragma warning restore IDE1006
