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
    internal static async System.Threading.Tasks.Task<T?> FirstAttemptAsync<T>(TaskId taskId, Func<System.Threading.Tasks.Task<T?>> read)
        where T : class
    {
        if (Ambient.Value is not { } baselines)
            return await read().ConfigureAwait(false);

        var first = baselines.GetOrAdd(taskId, _ => new Lazy<System.Threading.Tasks.Task<object?>>(async () => await read().ConfigureAwait(false)));
        try
        {
            return (T?)await first.Value.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A read cut short (the attempt's deadline, the run's cancellation) is no state: the next attempt reads again.
            baselines.TryRemove(new KeyValuePair<TaskId, Lazy<System.Threading.Tasks.Task<object?>>>(taskId, first));
            throw;
        }
    }

    private sealed class Scope(ConcurrentDictionary<TaskId, Lazy<System.Threading.Tasks.Task<object?>>>? enclosing) : IDisposable
    {
        public void Dispose() => Ambient.Value = enclosing;
    }
}
