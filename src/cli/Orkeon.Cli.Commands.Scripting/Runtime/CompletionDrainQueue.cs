using Jint.Native;
using Orkeon.Cli.Commands.Scripting.Dispatch;

namespace Orkeon.Cli.Commands.Scripting.Runtime;

/// <summary>
/// Per-engine queue of async <c>completed</c> callbacks waiting to be replayed on the engine
/// thread (design §4.3). The agent's response (a pool thread) enqueues; the next command
/// invocation on the owning engine drains under the engine lock — JS is never called from the
/// background thread.
/// </summary>
/// <remarks>
/// One instance per <c>*.cmd.ts</c> file (shared by all that file's commands, which share one
/// Jint engine). A command pumps only the completions of its own instances (<see cref="DrainFor"/>),
/// so a sibling of the same file never runs another command's <c>completed</c> under its own name.
/// Thread-safe: every access holds one lock.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The type is a genuine FIFO queue abstraction (enqueue/drain) and the 'Queue' suffix accurately describes its semantics; it is not intended to derive from System.Collections.Queue.")]
public sealed class CompletionDrainQueue
{
    private readonly List<PendingCompletion> _pending = new();
    private readonly Lock _lock = new();

    /// <summary>Enqueue a terminal instance + its (optional) <c>completed</c> callback.</summary>
    public void Enqueue(JsValue? completed, CommandInstance instance)
    {
        lock (_lock) _pending.Add(new PendingCompletion(completed, instance));
    }

    /// <summary>
    /// Drain the pending completions of the instances keyed to <paramref name="commandName"/> (the
    /// <see cref="CommandInstance.Name"/> a launch records), oldest first; the others stay queued for their own
    /// command. Each is removed exactly once.
    /// </summary>
    internal IReadOnlyList<PendingCompletion> DrainFor(string commandName)
    {
        lock (_lock)
        {
            if (_pending.Count == 0) return Array.Empty<PendingCompletion>();
            var drained = _pending.FindAll(p => string.Equals(p.Instance.Name, commandName, StringComparison.Ordinal));
            if (drained.Count > 0)
                _pending.RemoveAll(p => string.Equals(p.Instance.Name, commandName, StringComparison.Ordinal));
            return drained;
        }
    }
}

/// <summary>A terminal async instance and the <c>completed</c> closure to replay for it (if any).</summary>
/// <param name="Completed">The descriptor's <c>completed(result, ctx)</c> closure, or <see langword="null"/>.</param>
/// <param name="Instance">The terminal command instance.</param>
public readonly record struct PendingCompletion(JsValue? Completed, CommandInstance Instance);
