using Orkeon.Application.Crew;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew.ValueObjects;
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using DomainTaskOutput = Orkeon.Domain.Task.ValueObjects.TaskOutput;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// The outcome rule every orchestration mode applies (GAP-03): a crew with a failed task is a
/// failed crew, and a task depending on one that did not succeed is skipped and counted as a
/// failure. Sequential set that rule (STUDIO-12 C5a, LLM-11); the five other modes reported
/// success whatever their tasks did, so the runner's exit code depended on the mode. A mode
/// that wants to tolerate a failure — Graph and its retries, Autonomous and its delegation —
/// does so <b>before</b> recording it here, never by masking the crew's result.
/// <para>
/// Not thread-safe: a mode running tasks concurrently records their outcomes once they have
/// joined (Parallel records a wave's results after the wave, in declaration order).
/// </para>
/// </summary>
internal sealed class CrewRunOutcome
{
    private readonly List<string> _failures = [];
    private readonly HashSet<TaskId> _notSucceeded = [];
    private string? _headline;

    /// <summary>Whether anything failed: a task, a skipped dependant, or the run itself.</summary>
    internal bool HasFailures => _headline is not null || _failures.Count > 0;

    /// <summary>Every failure reason recorded so far, in order.</summary>
    internal IReadOnlyList<string> Failures => _failures;

    /// <summary>
    /// The crew's error: the run-level cause first when there is one (an exhausted budget),
    /// then every failed or skipped task, each naming itself.
    /// </summary>
    internal string Reason => string.Join("; ", _headline is null ? _failures : [_headline, .. _failures]);

    /// <summary>Whether <paramref name="taskId"/> failed or was skipped.</summary>
    internal bool DidNotSucceed(TaskId taskId) => _notSucceeded.Contains(taskId);

    /// <summary>
    /// The first declared dependency of <paramref name="task"/> that failed or was skipped, or
    /// null when the task may run. Only the direct dependencies are read: a skipped task joins
    /// the failed set itself, so the transitive closure follows. A dependency the crew does not
    /// carry never blocks — the crew cannot wait for something it will never run.
    /// </summary>
    internal TaskId? BlockingDependency(DomainTask task) =>
        task.Dependencies.FirstOrDefault(_notSucceeded.Contains);

    /// <summary>Records a task that ran and failed, with the cause its agent reported.</summary>
    internal string RecordFailure(TaskId taskId, string agentRole, string? error)
    {
        var reason = $"Task {taskId} ({agentRole}) failed: {(string.IsNullOrWhiteSpace(error) ? "unknown error" : error)}";
        _notSucceeded.Add(taskId);
        _failures.Add(reason);
        return reason;
    }

    /// <summary>Records a task that never ran because <paramref name="blockedBy"/> did not succeed.</summary>
    internal string RecordSkip(TaskId taskId, string agentRole, TaskId blockedBy)
    {
        var reason = $"Task {taskId} ({agentRole}) skipped: it depends on task {blockedBy}, which did not succeed";
        _notSucceeded.Add(taskId);
        _failures.Add(reason);
        return reason;
    }

    /// <summary>
    /// Records a cause that belongs to the run rather than to one task (an exhausted execution
    /// budget). It heads the crew's error.
    /// </summary>
    internal void RecordRunFailure(string headline) => _headline ??= headline;

    /// <summary>
    /// Records tasks the run never reached (the budget ran out before them): each is named, and
    /// counted as not succeeded.
    /// </summary>
    internal void RecordNotExecuted(IReadOnlyCollection<TaskId> taskIds)
    {
        if (taskIds.Count == 0)
            return;

        foreach (var taskId in taskIds)
            _notSucceeded.Add(taskId);
        _failures.Add($"not executed: {string.Join(", ", taskIds.Select(id => $"task {id}"))}");
    }

    /// <summary>
    /// The text a task's output carries: the agent's answer, or — when it gave none — a
    /// placeholder saying so. A task output cannot be empty, and an agent that failed without a
    /// word (no final answer) used to make three modes throw instead of reporting the failure.
    /// </summary>
    internal static string RawOutputOf(Application.Interfaces.Services.TaskResult result)
    {
        if (!string.IsNullOrEmpty(result.Output))
            return result.Output;
        return result.Success ? "(no output)" : $"Task failed: {result.Error ?? result.LastError ?? "unknown error"}";
    }

    /// <summary>
    /// The failed output recorded for a skipped task — what the next tasks' context and the
    /// crew's result carry in its place. No agent is asked anything, so it costs nothing.
    /// </summary>
    internal static (DomainTaskOutput Domain, Application.Execution.TaskOutput Application) SkippedOutputs(
        TaskId taskId, string? agentId, TaskId blockedBy)
    {
        var rawOutput = $"Task skipped: dependency {blockedBy} did not succeed";
        var application = new Application.Execution.TaskOutput(
            TaskId: taskId.Value.ToString(),
            AgentId: agentId ?? string.Empty,
            Content: rawOutput,
            CompletedAt: DateTime.UtcNow,
            Success: false,
            ExecutionTime: TimeSpan.Zero);
        var domain = DomainTaskOutput.Create(
            rawOutput: rawOutput,
            format: "text",
            formattedOutput: null,
            taskId: taskId,
            success: false,
            executionTime: TimeSpan.Zero,
            structuredOutput: null,
            agentId: agentId);
        return (domain, application);
    }

    /// <summary>The snapshot a skipped task reports: marked skipped, with its reason.</summary>
    internal static TaskExecutionSnapshot SkippedSnapshot(TaskId taskId, string agentRole, string reason) =>
        new()
        {
            TaskId = taskId.Value.ToString(),
            AgentRole = agentRole,
            Success = false,
            Duration = TimeSpan.Zero,
            CompletedAt = DateTimeOffset.UtcNow,
            Skipped = true,
            SkipReason = reason,
        };

    /// <summary>
    /// Ends the run: a failed crew — <see cref="Reason"/> as its error, the outputs and the
    /// tokens it did produce kept — with a <see cref="CrewHookStatus.Failed"/> terminal event
    /// carrying the same reason; otherwise a successful crew and a completion event.
    /// </summary>
    internal async Task<DomainCrewOutput> CompleteAsync(
        CrewHookDispatcher hooks,
        string crewId,
        DateTimeOffset startedAt,
        IEnumerable<TaskExecutionSnapshot> taskSnapshots,
        IEnumerable<DomainTaskOutput> taskOutputs,
        TimeSpan executionTime,
        CrewMetadata metadata,
        string output)
    {
        if (HasFailures)
        {
            var reason = Reason;
            await hooks.CrewFailedAsync(
                CrewHookDispatcher.Snapshot(crewId, startedAt, taskSnapshots, CrewHookStatus.Failed, reason),
                null, CancellationToken.None).ConfigureAwait(false);

            return DomainCrewOutput.CreateFailure(
                error: reason,
                taskOutputs: taskOutputs,
                executionTime: executionTime,
                metadata: metadata,
                output: output);
        }

        await hooks.CrewCompletedAsync(
            CrewHookDispatcher.Snapshot(crewId, startedAt, taskSnapshots, CrewHookStatus.Completed),
            CancellationToken.None).ConfigureAwait(false);

        return DomainCrewOutput.CreateSuccess(
            output: output,
            structuredOutput: null,
            taskOutputs: taskOutputs,
            executionTime: executionTime,
            metadata: metadata);
    }
}
