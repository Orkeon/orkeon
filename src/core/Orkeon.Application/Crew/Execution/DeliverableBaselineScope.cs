using System.Collections.Concurrent;
using Orkeon.Domain.Common;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// The state each task's <c>tool_call</c> deliverable had when the task's first attempt of the run began
/// (#392). The end of an attempt is compared with it: a retry, a hierarchical revision or another ballot of
/// a vote that keeps the file an earlier attempt of the same task wrote is not flagged missing, while a
/// file an earlier run left, untouched, still is.
/// </summary>
/// <remarks>
/// Ambient, like the plan (<see cref="CrewPlanScope"/>): backed by <see cref="AsyncLocal{T}"/>, it follows
/// the run into its parallel waves, the candidates and ballots of a vote and the retries; a crew run from
/// inside a task opens its own. Outside a run (a task executed on its own), each attempt reads its own
/// state, as before.
/// </remarks>
public static class DeliverableBaselineScope
{
    private static readonly AsyncLocal<ConcurrentDictionary<TaskId, Lazy<System.Threading.Tasks.Task<object?>>>?> Ambient = new();

    /// <summary>Opens the scope of a run; disposing the handle restores the enclosing one.</summary>
    /// <returns>The handle that restores the enclosing scope.</returns>
    public static IDisposable Begin()
    {
        var enclosing = Ambient.Value;
        Ambient.Value = new ConcurrentDictionary<TaskId, Lazy<System.Threading.Tasks.Task<object?>>>();
        return new Scope(enclosing);
    }

    /// <summary>Whether a run's scope is open.</summary>
    internal static bool IsOpen => Ambient.Value is not null;

    /// <summary>
    /// The state <paramref name="read"/> gives at the first attempt of <paramref name="taskId"/> in the run,
    /// read once and handed to every later attempt; read afresh at each call outside a run.
    /// </summary>
    /// <remarks>
    /// In a run, attempts of one task may wait on the same read at once (the ballots of a vote): that read
    /// belongs to none of them, so it is made with no cancellation, and each attempt stops waiting on its
    /// own <paramref name="cancellationToken"/>. An attempt cancelled while it waits leaves the read, and
    /// its result, to the others; a read that is itself cut short is dropped, and the next attempt reads again.
    /// </remarks>
    internal static async System.Threading.Tasks.Task<T?> FirstAttemptAsync<T>(
        TaskId taskId, Func<CancellationToken, System.Threading.Tasks.Task<T?>> read, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(read);
        if (Ambient.Value is not { } baselines)
            return await read(cancellationToken).ConfigureAwait(false);

        var first = baselines.GetOrAdd(
            taskId,
            _ => new Lazy<System.Threading.Tasks.Task<object?>>(async () => await read(CancellationToken.None).ConfigureAwait(false)));
        var shared = first.Value;
        try
        {
            return (T?)await shared.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shared.IsCanceled)
        {
            // The shared read itself was cut short: it is no state, the next attempt reads again.
            baselines.TryRemove(new KeyValuePair<TaskId, Lazy<System.Threading.Tasks.Task<object?>>>(taskId, first));
            throw;
        }
    }

    private sealed class Scope(ConcurrentDictionary<TaskId, Lazy<System.Threading.Tasks.Task<object?>>>? enclosing) : IDisposable
    {
        public void Dispose() => Ambient.Value = enclosing;
    }
}
