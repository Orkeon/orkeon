using Orkeon.Cli.Commands.Scripting.Dispatch;

namespace Orkeon.Cli.Commands.Scripting.Progress;

/// <summary>
/// Ambient (async-flow-local) command instance for progress attribution. Set by
/// <see cref="CommandDispatchService.postWork"/>/<c>post</c> around the detached work so
/// anything that reports progress inside that flow — a crew body calling the
/// <c>progress_report</c> tool, a <c>ctx.progress</c> handle — can also stamp the
/// instance's <see cref="CommandInstance.ReportProgress"/> without threading it through
/// every layer.
/// </summary>
/// <remarks>
/// AsyncLocal, not ThreadLocal: the detached work hops threads at each await and the
/// value must follow the flow, not the thread. JS is never called back through this —
/// it only ever mutates host-side state, preserving the dispatch design's threading rule.
/// </remarks>
public static class ProgressAmbient
{
    private static readonly AsyncLocal<CommandInstance?> _current = new();

    /// <summary>The instance owning the current async flow, or null outside detached work.</summary>
    public static CommandInstance? CurrentInstance
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
