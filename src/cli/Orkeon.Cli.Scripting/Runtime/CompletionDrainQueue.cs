using System.Collections.Concurrent;
using Jint.Native;
using Orkeon.Cli.Scripting.Dispatch;

namespace Orkeon.Cli.Scripting.Runtime;

/// <summary>
/// Per-engine queue of async <c>completed</c> callbacks waiting to be replayed on the engine
/// thread (design §4.3). The agent's response (a pool thread) enqueues; the next command
/// invocation on the owning engine drains under the engine lock — JS is never called from the
/// background thread.
/// </summary>
/// <remarks>
/// One instance per <c>*.cmd.ts</c> file (shared by all that file's commands, which share one
/// Jint engine). Thread-safe by construction.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The type is a genuine FIFO queue abstraction (enqueue/drain) and the 'Queue' suffix accurately describes its semantics; it is not intended to derive from System.Collections.Queue.")]
public sealed class CompletionDrainQueue
{
    private readonly ConcurrentQueue<PendingCompletion> _pending = new();

    /// <summary>Enqueue a terminal instance + its (optional) <c>completed</c> callback.</summary>
    public void Enqueue(JsValue? completed, CommandInstance instance)
        => _pending.Enqueue(new PendingCompletion(completed, instance));

    /// <summary>Drain all pending completions, oldest first. Each is removed exactly once.</summary>
    public IReadOnlyList<PendingCompletion> DrainAll()
    {
        if (_pending.IsEmpty) return Array.Empty<PendingCompletion>();
        var drained = new List<PendingCompletion>();
        while (_pending.TryDequeue(out var item))
            drained.Add(item);
        return drained;
    }
}

/// <summary>A terminal async instance and the <c>completed</c> closure to replay for it (if any).</summary>
/// <param name="Completed">The descriptor's <c>completed(result, ctx)</c> closure, or <see langword="null"/>.</param>
/// <param name="Instance">The terminal command instance.</param>
public readonly record struct PendingCompletion(JsValue? Completed, CommandInstance Instance);
