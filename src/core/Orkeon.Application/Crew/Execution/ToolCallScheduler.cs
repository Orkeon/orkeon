using System.Runtime.ExceptionServices;
using Orkeon.Domain.Tools;

namespace Orkeon.Application.Crew.Execution;

/// <summary>One maximal run of parallel reads, or one sequential call (a barrier).</summary>
public sealed record ToolCallGroup<TCall>(bool IsParallel, IReadOnlyList<TCall> Calls);

/// <summary>Runs the calls of one model turn: reads together between barriers, the rest one by one.</summary>
public static class ToolCallScheduler
{
    private static readonly HashSet<string> s_parallelReadTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "file_read", "directory_read", "directory_search", "count_pattern", "glob", "grep",
        "rag_search", "index_status", "node_outline", "node_read",
    };

    private static readonly string[] s_parallelReadPrefixes = ["codebase_", "symbol_"];

    /// <summary>True for an allow-listed read tool whose <paramref name="access"/> is <see cref="ToolAccess.Read"/>.</summary>
    public static bool IsParallelRead(string toolName, ToolAccess access) =>
        access == ToolAccess.Read
        && (s_parallelReadTools.Contains(toolName)
            || s_parallelReadPrefixes.Any(p => toolName.StartsWith(p, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Splits <paramref name="calls"/> into runs of parallel reads and single sequential calls, in call order.</summary>
    public static IReadOnlyList<ToolCallGroup<TCall>> Partition<TCall>(
        IReadOnlyList<TCall> calls, Func<TCall, bool> isParallelRead)
    {
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(isParallelRead);
        var groups = new List<ToolCallGroup<TCall>>();
        List<TCall>? reads = null;
        foreach (var call in calls)
        {
            if (isParallelRead(call))
            {
                reads ??= [];
                reads.Add(call);
                continue;
            }

            if (reads is not null)
            {
                groups.Add(new ToolCallGroup<TCall>(true, reads));
                reads = null;
            }
            groups.Add(new ToolCallGroup<TCall>(false, [call]));
        }

        if (reads is not null) groups.Add(new ToolCallGroup<TCall>(true, reads));
        return groups;
    }

    /// <summary>
    /// Executes the calls group by group; a group of reads runs at most <paramref name="maxParallel"/> at once.
    /// Outcomes come back in call order. When calls of a group fault, the others complete first, then the first
    /// fault in call order is rethrown.
    /// </summary>
    public static async System.Threading.Tasks.Task<IReadOnlyList<TOutcome>> RunAsync<TCall, TOutcome>(
        IReadOnlyList<TCall> calls,
        Func<TCall, bool> isParallelRead,
        Func<TCall, CancellationToken, System.Threading.Tasks.Task<TOutcome>> execute,
        int maxParallel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(execute);
        var outcomes = new List<TOutcome>(calls.Count);
        using var slots = new SemaphoreSlim(Math.Max(1, maxParallel));
        foreach (var group in Partition(calls, isParallelRead))
        {
            if (!group.IsParallel)
            {
                outcomes.Add(await execute(group.Calls[0], cancellationToken).ConfigureAwait(false));
                continue;
            }

            var running = group.Calls
                .Select(async call =>
                {
                    await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        return await execute(call, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        slots.Release();
                    }
                })
                .ToList();
            await System.Threading.Tasks.Task.WhenAll(running.Select(WaitQuietlyAsync)).ConfigureAwait(false);

            var fault = running.Find(t => t.IsFaulted || t.IsCanceled);
            if (fault is not null)
            {
                ExceptionDispatchInfo.Capture(fault.Exception?.InnerException ?? new OperationCanceledException(cancellationToken)).Throw();
            }

            outcomes.AddRange(running.Select(t => t.Result));
        }

        return outcomes;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fault barrier: every call of a group must end before the first fault is rethrown; the fault stays on its task.")]
    private static async System.Threading.Tasks.Task WaitQuietlyAsync(System.Threading.Tasks.Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The fault stays on the task: RunAsync rethrows the first one after the group.
        }
    }
}
