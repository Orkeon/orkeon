using Orkeon.Application.Crew;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;
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
/// It also drives the lifecycle of the tasks and agents as the run goes (GAP-21), through
/// <see cref="TaskLifecycle"/>: a start assigns the task to its agent and starts both; a success
/// completes them, a failure fails them; a skipped task, a task the run never reached and a task an
/// interruption caught are cancelled — with the reason —, the agents running the last failing it.
/// A task an earlier run of the crew ended is reopened the first time this run records it. The
/// six modes call it, never the aggregates.
/// </para>
/// <para>
/// Not thread-safe: a mode running tasks concurrently records them from its own flow — Parallel
/// records a wave's starts as it launches them and their results once the wave has joined, in
/// declaration order; Sequential records an <c>asyncExecution</c> task's start when it launches it
/// and its result when a task — or the end of the run — waits for it (GAP-22).
/// </para>
/// </summary>
internal sealed class CrewRunOutcome
{
    private readonly TaskLifecycle _lifecycle;
    private readonly List<string> _failures = [];
    private readonly HashSet<TaskId> _notSucceeded = [];
    private readonly Dictionary<TaskId, RunningTask> _running = [];
    private readonly HashSet<TaskId> _recorded = [];
    private string? _headline;

    /// <summary>Starts recording a run whose tasks move through <paramref name="lifecycle"/>.</summary>
    internal CrewRunOutcome(TaskLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        _lifecycle = lifecycle;
    }

    /// <summary>A task this run started and has not ended, and the agents running it.</summary>
    private sealed record RunningTask(DomainTask Task, List<DomainAgent> Agents);

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

    /// <summary>
    /// <paramref name="task"/> starts under <paramref name="agent"/>: assigned to it — re-assigned
    /// when the run chose another agent than the one it had —, started, and given to the agent, who
    /// starts it. A task this run already runs (a graph retry) goes on: under the same agent nothing
    /// moves, under another it is handed over.
    /// </summary>
    internal System.Threading.Tasks.Task RecordStartAsync(DomainTask task, DomainAgent agent)
    {
        if (_running.TryGetValue(task.Id, out var running))
        {
            return running.Agents.Exists(a => a.Id == agent.Id)
                ? System.Threading.Tasks.Task.CompletedTask
                : RecordHandOverAsync(task, agent, $"handed over to {agent.Role.Value}");
        }

        var reopen = FirstRecord(task);
        _running[task.Id] = new RunningTask(task, [agent]);
        return _lifecycle.ApplyAsync(task, [agent], () =>
        {
            if (reopen)
                task.Reopen();
            task.AssignTo(agent.Id);
            task.Start(agent.Id);
            Begin(agent, task.Id);
        });
    }

    /// <summary>
    /// <paramref name="task"/> starts once with no single agent — a consensual task: every agent of
    /// <paramref name="agents"/> answers it, so each is given the task and starts it.
    /// </summary>
    internal System.Threading.Tasks.Task RecordStartWithoutAgentAsync(DomainTask task, IReadOnlyList<DomainAgent> agents)
    {
        var reopen = FirstRecord(task);
        _running[task.Id] = new RunningTask(task, [.. agents]);
        return _lifecycle.ApplyAsync(task, agents, () =>
        {
            if (reopen)
                task.Reopen();
            task.Start();
            foreach (var agent in agents)
                Begin(agent, task.Id);
        });
    }

    /// <summary>
    /// The running <paramref name="task"/> goes to <paramref name="to"/> — a failed task delegated to
    /// a peer: the agent running it fails it with <paramref name="reason"/>, the task is assigned to
    /// <paramref name="to"/>, who is given it and starts it. The task itself goes on.
    /// </summary>
    internal System.Threading.Tasks.Task RecordHandOverAsync(DomainTask task, DomainAgent to, string reason)
    {
        if (!_running.TryGetValue(task.Id, out var running))
            return RecordStartAsync(task, to);

        var from = running.Agents.Where(a => a.Id != to.Id).ToList();
        running.Agents.Clear();
        running.Agents.Add(to);
        return _lifecycle.ApplyAsync(task, [.. from, to], () =>
        {
            foreach (var agent in from)
                End(agent, task.Id, output: null, reason);
            task.AssignTo(to.Id);
            Begin(to, task.Id);
        });
    }

    /// <summary>
    /// One of the agents running <paramref name="task"/> is done with it while the task goes on — a
    /// consensual candidate once the vote is over: it completes the task with its own answer, or
    /// fails it with its own error.
    /// </summary>
    internal System.Threading.Tasks.Task RecordAnswerAsync(DomainTask task, DomainAgent agent, Application.Interfaces.Services.TaskResult answer)
    {
        if (!_running.TryGetValue(task.Id, out var running) || running.Agents.RemoveAll(a => a.Id == agent.Id) == 0)
            return System.Threading.Tasks.Task.CompletedTask;

        var output = answer.Success ? OutputOf(task, agent, answer) : null;
        return _lifecycle.ApplyAsync(task, [agent], () =>
            End(agent, task.Id, output, answer.Error ?? answer.LastError ?? "unknown error"));
    }

    /// <summary>
    /// <paramref name="task"/> succeeded with <paramref name="output"/>, under <paramref name="author"/>
    /// — the agent running it when null: the task is completed under that agent, and every agent
    /// still running it completes it.
    /// </summary>
    internal System.Threading.Tasks.Task RecordSuccessAsync(DomainTask task, DomainTaskOutput output, DomainAgent? author = null)
    {
        _running.Remove(task.Id, out var running);
        var agents = running?.Agents ?? [];
        var completer = author ?? agents.FirstOrDefault();
        var reopen = running is null && FirstRecord(task);
        List<DomainAgent> touched = [.. agents];
        if (completer is not null && !touched.Exists(a => a.Id == completer.Id))
            touched.Add(completer);

        return _lifecycle.ApplyAsync(task, touched, () =>
        {
            if (completer is null)
                throw new InvalidOperationException($"No agent produced the output of task {task.Id}.");
            if (reopen)
                task.Reopen();
            task.AssignTo(completer.Id);
            task.Complete(completer.Id, output);
            foreach (var agent in agents)
                End(agent, task.Id, output, reason: null);
        });
    }

    /// <summary>
    /// Records a task that failed, with the cause its agent reported: the task fails — before it
    /// started, when the run never got to run it — and every agent running it fails it.
    /// </summary>
    internal async System.Threading.Tasks.Task<string> RecordFailureAsync(DomainTask task, string agentRole, string? error)
    {
        var cause = string.IsNullOrWhiteSpace(error) ? "unknown error" : error;
        var reason = $"Task {task.Id} ({agentRole}) failed: {cause}";
        _notSucceeded.Add(task.Id);
        _failures.Add(reason);

        _running.Remove(task.Id, out var running);
        var agents = running?.Agents ?? [];
        var reopen = running is null && FirstRecord(task);
        await _lifecycle.ApplyAsync(task, agents, () =>
        {
            if (reopen)
                task.Reopen();
            task.Fail(cause);
            foreach (var agent in agents)
                End(agent, task.Id, output: null, cause);
        }).ConfigureAwait(false);
        return reason;
    }

    /// <summary>
    /// Records a task that never ran because <paramref name="blockedBy"/> did not succeed: it is
    /// cancelled with that reason, neither started nor ended.
    /// </summary>
    internal async System.Threading.Tasks.Task<string> RecordSkipAsync(DomainTask task, string agentRole, TaskId blockedBy)
    {
        var reason = $"Task {task.Id} ({agentRole}) skipped: it depends on task {blockedBy}, which did not succeed";
        _notSucceeded.Add(task.Id);
        _failures.Add(reason);
        await CancelAsync(task, $"skipped: it depends on task {blockedBy}, which did not succeed").ConfigureAwait(false);
        return reason;
    }

    /// <summary>
    /// Records a cause that belongs to the run rather than to one task (an exhausted execution
    /// budget). It heads the crew's error.
    /// </summary>
    internal void RecordRunFailure(string headline) => _headline ??= headline;

    /// <summary>
    /// Records tasks the run never reached (the budget ran out before them): each is named, counted
    /// as not succeeded, and cancelled with <paramref name="reason"/>.
    /// </summary>
    internal async System.Threading.Tasks.Task RecordNotExecutedAsync(IReadOnlyCollection<TaskId> taskIds, string reason)
    {
        if (taskIds.Count == 0)
            return;

        foreach (var taskId in taskIds)
            _notSucceeded.Add(taskId);
        _failures.Add($"not executed: {string.Join(", ", taskIds.Select(id => $"task {id}"))}");

        foreach (var taskId in taskIds)
        {
            if (await _lifecycle.FindAsync(taskId).ConfigureAwait(false) is { } task)
                await CancelAsync(task, $"not executed: {reason}").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The run stopped on <paramref name="exception"/> while tasks were running: a cancellation
    /// cancels each of them, any other exception fails it; the agents running it fail it with the
    /// same reason.
    /// </summary>
    internal async System.Threading.Tasks.Task RecordInterruptionAsync(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var cancelled = exception is OperationCanceledException;
        var reason = cancelled ? "the run was cancelled" : $"the run stopped: {exception.Message}";

        foreach (var running in _running.Values.ToList())
        {
            _running.Remove(running.Task.Id);
            await _lifecycle.ApplyAsync(running.Task, running.Agents, () =>
            {
                if (cancelled)
                    running.Task.Cancel(reason);
                else
                    running.Task.Fail(reason);
                foreach (var agent in running.Agents)
                    End(agent, running.Task.Id, output: null, reason);
            }).ConfigureAwait(false);
        }
    }

    /// <summary>Cancels <paramref name="task"/>, the agents running it failing it with the reason.</summary>
    private System.Threading.Tasks.Task CancelAsync(DomainTask task, string reason)
    {
        _running.Remove(task.Id, out var running);
        var agents = running?.Agents ?? [];
        var reopen = running is null && FirstRecord(task);
        return _lifecycle.ApplyAsync(task, agents, () =>
        {
            if (reopen)
                task.Reopen();
            task.Cancel(reason);
            foreach (var agent in agents)
                End(agent, task.Id, output: null, reason);
        });
    }

    /// <summary>
    /// Whether <paramref name="task"/> is recorded for the first time in this run while an earlier
    /// run left it started or ended — then the run reopens it before moving it.
    /// </summary>
    private bool FirstRecord(DomainTask task) =>
        _recorded.Add(task.Id) && task.Status != Orkeon.Domain.Task.ValueObjects.TaskStatus.Pending;

    /// <summary>The agent is given the task, unless it has it already, and starts it.</summary>
    private static void Begin(DomainAgent agent, TaskId taskId)
    {
        if (!agent.AssignedTasks.Contains(taskId))
            agent.AssignTask(taskId);
        if (!agent.CurrentTasks.Contains(taskId))
            agent.StartTask(taskId);
    }

    /// <summary>
    /// The agent ends a task it runs: completed with <paramref name="output"/> when there is one,
    /// failed with <paramref name="reason"/> otherwise.
    /// </summary>
    private static void End(DomainAgent agent, TaskId taskId, DomainTaskOutput? output, string? reason)
    {
        if (!agent.CurrentTasks.Contains(taskId))
            return;
        if (output is not null)
            agent.CompleteTask(taskId, output);
        else
            agent.FailTask(taskId, reason ?? "unknown error");
    }

    /// <summary>An agent's own answer to a task, as the output its completion carries.</summary>
    private static DomainTaskOutput OutputOf(DomainTask task, DomainAgent agent, Application.Interfaces.Services.TaskResult answer) =>
        DomainTaskOutput.Create(
            rawOutput: RawOutputOf(answer),
            format: "text",
            formattedOutput: null,
            taskId: task.Id,
            success: true,
            executionTime: answer.ExecutionTime,
            structuredOutput: answer.StructuredOutput,
            agentId: agent.Id.ToString());

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
