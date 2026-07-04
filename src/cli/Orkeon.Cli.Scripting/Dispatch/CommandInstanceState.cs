namespace Orkeon.Cli.Scripting.Dispatch;

/// <summary>
/// Lifecycle state of a dispatched command instance (design §6). Terminal states
/// (<see cref="Done"/>, <see cref="Failed"/>, <see cref="Cancelled"/>, <see cref="Rejected"/>)
/// are retained for a while so <c>result</c>/<c>inspect</c> can read them back.
/// </summary>
public enum CommandInstanceState
{
    /// <summary>The request is in flight — the agent has not yet responded.</summary>
    Running,

    /// <summary>The agent responded successfully (its handler returned a success response).</summary>
    Done,

    /// <summary>The agent responded with a failure, threw, or the request timed out.</summary>
    Failed,

    /// <summary>The instance was cancelled (Ctrl-C or explicit <c>cancel --ticket</c>).</summary>
    Cancelled,

    /// <summary>Admission was refused by the per-command quota — the handler never ran.</summary>
    Rejected,
}

/// <summary>Helpers over <see cref="CommandInstanceState"/>.</summary>
public static class CommandInstanceStateExtensions
{
    /// <summary>True for any state that will never change again.</summary>
    public static bool IsTerminal(this CommandInstanceState state)
        => state is not CommandInstanceState.Running;

    /// <summary>The lowercase token used in filters and JS-facing views (e.g. <c>"running"</c>).</summary>
    public static string ToToken(this CommandInstanceState state) => state switch
    {
        CommandInstanceState.Running => "running",
        CommandInstanceState.Done => "done",
        CommandInstanceState.Failed => "failed",
        CommandInstanceState.Cancelled => "cancelled",
        CommandInstanceState.Rejected => "rejected",
#pragma warning disable CA1308 // produces the lowercase token used in filters/JS views, not a comparison normalization
        _ => state.ToString().ToLowerInvariant(),
#pragma warning restore CA1308
    };
}
