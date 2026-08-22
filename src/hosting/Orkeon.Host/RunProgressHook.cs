using Orkeon.Application.Crew;

namespace Orkeon.Host;

/// <summary>
/// Feeds a hosted run's task completions to whoever is watching the conversation (GATE-03).
/// <para>
/// Scoped, like the run itself: <c>CrewRunner</c> binds the callback right after creating the
/// run's scope, and the orchestration strategies dispatch their per-task events into it.
/// Before this existed the runner emitted exactly one progress line per run — "Running…" —
/// while the documentation promised progress every couple of seconds; the throttling
/// machinery downstream was real and unreachable.
/// </para>
/// </summary>
internal sealed class RunProgressHook : ICrewExecutionHook
{
    private int _completed;

    /// <summary>Receives one line per finished task; null until the runner binds it.</summary>
    public Action<string>? OnProgress { get; set; }

    /// <inheritdoc />
    public Task OnTaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var index = Interlocked.Increment(ref _completed);
        var mark = snapshot.Success ? "✔" : "✘";
        var who = string.IsNullOrWhiteSpace(snapshot.AgentRole) ? "task" : snapshot.AgentRole;
        OnProgress?.Invoke($"{mark} {who} — step {index} done");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnCrewCompletedAsync(CrewExecutionSnapshot snapshot, CancellationToken ct) =>
        Task.CompletedTask;   // the completion is the final answer, delivered by the runner

    /// <inheritdoc />
    public Task OnCrewFailedAsync(CrewExecutionSnapshot snapshot, Exception? ex, CancellationToken ct) =>
        Task.CompletedTask;   // failure reaches the user through the runner's outcome
}
