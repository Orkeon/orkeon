using System.Collections.Concurrent;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// What System One picked for a task of an agent in a run (#434): the profile of an agent on
/// <c>llm.profile: auto</c>, its skills. Picked at the first attempt of the task by that agent and kept by its
/// retries, revisions and ballots; another agent on the same task (a voter of a consensual crew) picks its own,
/// and the next run picks again.
/// </summary>
/// <remarks>
/// Ambient, like <see cref="DeliverableBaselineScope"/>: backed by <see cref="AsyncLocal{T}"/>, opened by each
/// kickoff, it follows the run into its parallel waves and ballots; a crew run from inside a task opens its own.
/// Outside a run (a task executed on its own), the orchestrator keeps the picks of its own scope.
/// </remarks>
public static class TaskPickScope
{
    private static readonly AsyncLocal<TaskPicks?> Ambient = new();

    /// <summary>Opens the scope of a run; disposing the handle restores the enclosing one.</summary>
    /// <returns>The handle that restores the enclosing scope.</returns>
    public static IDisposable Begin()
    {
        var enclosing = Ambient.Value;
        Ambient.Value = new TaskPicks();
        return new Scope(enclosing);
    }

    /// <summary>The picks of the run in progress; null outside a run.</summary>
    internal static TaskPicks? Current => Ambient.Value;

    private sealed class Scope(TaskPicks? enclosing) : IDisposable
    {
        public void Dispose() => Ambient.Value = enclosing;
    }
}

/// <summary>The picks of one run, or of one orchestrator outside a run, by kind, task and agent.</summary>
internal sealed class TaskPicks
{
    private readonly ConcurrentDictionary<(string Kind, string TaskId, string AgentId), Lazy<System.Threading.Tasks.Task<object?>>> _picks = new();

    /// <summary>
    /// The pick <paramref name="pick"/> gives at the first call for (<paramref name="kind"/>, <paramref name="taskId"/>,
    /// <paramref name="agentId"/>), made once and handed to every later call, those made at the same time included.
    /// The pick belongs to no single attempt, as a deliverable's baseline does: it runs uncancelled, a caller cancelled
    /// while it waits stops waiting alone, and a pick that failed is dropped, so the next call picks again.
    /// </summary>
    internal async System.Threading.Tasks.Task<T> OnceAsync<T>(
        string kind, string taskId, string agentId, Func<CancellationToken, System.Threading.Tasks.Task<T>> pick, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pick);
        var key = (kind, taskId, agentId);
        var first = _picks.GetOrAdd(
            key, _ => new Lazy<System.Threading.Tasks.Task<object?>>(async () => await pick(CancellationToken.None).ConfigureAwait(false)));
        var shared = first.Value;
        try
        {
            return (T)(await shared.WaitAsync(cancellationToken).ConfigureAwait(false))!;
        }
        catch (Exception) when (shared.IsCanceled || shared.IsFaulted)
        {
            _picks.TryRemove(new KeyValuePair<(string, string, string), Lazy<System.Threading.Tasks.Task<object?>>>(key, first));
            throw;
        }
    }
}
