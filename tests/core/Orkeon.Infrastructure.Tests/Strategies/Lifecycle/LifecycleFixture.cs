using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.Events;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.Events;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.Events;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Infrastructure.Tests.Strategies.Lifecycle;

/// <summary>
/// GAP-21 — what the six "the run drives the task and agent lifecycle" suites share: in-memory
/// repositories, an execution double that fails the tasks a test names and records, for each task
/// it runs, how many events had been dispatched when it ran, and a dispatcher recording every event
/// the run hands it.
/// </summary>
internal sealed class LifecycleFixture : IDisposable
{
    private readonly ConcurrentQueue<(string Task, int DispatchedBefore)> _executed = new();
    private readonly ConcurrentDictionary<string, string> _failures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _failingAttempts = new(StringComparer.Ordinal);
    private readonly InMemoryTaskRepository _tasks;
    private readonly InMemoryAgentRepository _agents;

    public LifecycleFixture()
    {
        var unitOfWork = new NullUnitOfWork();
        _tasks = new InMemoryTaskRepository(unitOfWork);
        _agents = new InMemoryAgentRepository(unitOfWork);
        Execution.SetExecuteFunc((_, task, _, _) =>
        {
            var name = task.Description.Value;
            _executed.Enqueue((name, Events.Dispatched.Count));
            return FailsNow(name, out var error)
                ? new TaskResult(false, "", null, [], TimeSpan.Zero, Error: error)
                : new TaskResult(true, $"{name} done", null, [], TimeSpan.Zero);
        });
    }

    public MockAgentExecutionService Execution { get; } = new();
    public MockMemoryScope MemoryScope { get; } = new();
    public MockCrewExecutionHook Hook { get; } = new();
    public RecordingDomainEventDispatcher Events { get; } = new();

    public CrewStrategyDependencies Dependencies => new(_tasks, _agents, Execution, MemoryScope, Events);

    public InMemoryTaskRepository Tasks => _tasks;
    public InMemoryAgentRepository Agents => _agents;

    /// <summary>How many events the run had dispatched when the agent was asked to run <paramref name="task"/>.</summary>
    public int DispatchedBefore(string task) => _executed.First(e => e.Task == task).DispatchedBefore;

    public DomainAgent Agent(string role, bool allowDelegation = false)
    {
        var agent = new AgentBuilder()
            .Role(role).Goal($"Goal of {role}").Backstory($"{role} works")
            .AllowDelegation(allowDelegation)
            .Build();
        _agents.AddAsync(agent).GetAwaiter().GetResult();
        return agent;
    }

    public CrewTask Task(string name, params CrewTask[] dependsOn)
    {
        var builder = new CrewTaskBuilder().Description(name).ExpectedOutput(name);
        if (dependsOn.Length > 0)
            builder.DependsOn(dependsOn);
        var task = builder.Build();
        _tasks.AddAsync(task).GetAwaiter().GetResult();
        return task;
    }

    /// <summary>Every attempt at <paramref name="name"/> fails with <paramref name="error"/>.</summary>
    public void Fail(string name, string error) => _failures[name] = error;

    private bool FailsNow(string name, out string error)
    {
        if (!_failures.TryGetValue(name, out error!))
            return false;
        if (!_failingAttempts.TryGetValue(name, out var left))
            return true;
        _failingAttempts[name] = left - 1;
        return left > 0;
    }

    /// <summary>The first <paramref name="attempts"/> attempts at <paramref name="name"/> fail, the next succeed.</summary>
    public void FailFirst(string name, int attempts, string error)
    {
        _failures[name] = error;
        _failingAttempts[name] = attempts;
    }

    public static DomainCrew Build(CrewBuilder builder, IEnumerable<DomainAgent> agents, IEnumerable<CrewTask> tasks)
    {
        foreach (var agent in agents)
            builder.WithAgent(agent);
        foreach (var task in tasks)
            builder.WithTask(task);
        return builder.Build();
    }

    public IReadOnlyList<T> Dispatched<T>() where T : DomainEvent => [.. Events.Dispatched.OfType<T>()];

    public int IndexOf(DomainEvent domainEvent) => Events.Dispatched.ToList().IndexOf(domainEvent);

    /// <summary>
    /// The rule of the six modes, for a crew of three tasks — <paramref name="done"/> succeeds,
    /// <paramref name="broken"/> fails with <paramref name="error"/>, and <paramref name="skipped"/>,
    /// which depends on it, is skipped: each task that ran was started once and ended once, the
    /// skipped one neither, and the repository holds what happened.
    /// </summary>
    public async Task AssertTaskLifecycleAsync(CrewTask done, CrewTask broken, CrewTask skipped, string error)
    {
        AssertSameTasks([done.Id, broken.Id], Dispatched<TaskStartedEvent>().Select(e => e.TaskId));
        Assert.Equal(done.Id, Assert.Single(Dispatched<TaskCompletedEvent>()).TaskId);
        var failed = Assert.Single(Dispatched<TaskFailedEvent>());
        Assert.Equal(broken.Id, failed.TaskId);
        Assert.Equal(error, failed.ErrorMessage);
        var cancelled = Assert.Single(Dispatched<TaskCancelledEvent>());
        Assert.Equal(skipped.Id, cancelled.TaskId);
        Assert.StartsWith($"skipped: it depends on task {broken.Id}", cancelled.Reason, StringComparison.Ordinal);

        var storedDone = await _tasks.GetByIdAsync(done.Id);
        Assert.NotNull(storedDone);
        Assert.Equal(TaskStatus.Completed, storedDone.Status);
        Assert.NotNull(storedDone.StartedAt);
        Assert.NotNull(storedDone.CompletedAt);
        Assert.Equal($"{done.Description.Value} done", storedDone.Output?.Output);

        var storedBroken = await _tasks.GetByIdAsync(broken.Id);
        Assert.NotNull(storedBroken);
        Assert.Equal(TaskStatus.Failed, storedBroken.Status);
        Assert.NotNull(storedBroken.StartedAt);
        Assert.NotNull(storedBroken.CompletedAt);

        var storedSkipped = await _tasks.GetByIdAsync(skipped.Id);
        Assert.NotNull(storedSkipped);
        Assert.Equal(TaskStatus.Cancelled, storedSkipped.Status);
        Assert.Null(storedSkipped.StartedAt);
        Assert.Null(storedSkipped.CompletedAt);
    }

    /// <summary>
    /// The agent side of the same rule: <paramref name="agent"/> started the two tasks that ran,
    /// completed the first, failed the second with <paramref name="error"/>, and runs nothing after.
    /// </summary>
    public void AssertAgentLifecycle(DomainAgent agent, CrewTask done, CrewTask broken, string error)
    {
        AssertSameTasks(
            [done.Id, broken.Id],
            Dispatched<AgentStartedTaskEvent>().Where(e => e.AgentId == agent.Id).Select(e => e.TaskId));
        Assert.Equal(
            done.Id,
            Assert.Single(Dispatched<AgentCompletedTaskEvent>(), e => e.AgentId == agent.Id).TaskId);
        var failed = Assert.Single(Dispatched<AgentFailedTaskEvent>(), e => e.AgentId == agent.Id);
        Assert.Equal(broken.Id, failed.TaskId);
        Assert.Equal(error, failed.Reason);
        Assert.Empty(agent.CurrentTasks);
        Assert.Equal(Orkeon.Domain.Agent.ValueObjects.AgentStatus.Idle, agent.Status);
    }

    /// <summary>
    /// The run dispatched as it went: when the agent was asked to run <paramref name="later"/>, the
    /// completion of <paramref name="earlier"/> had already gone out.
    /// </summary>
    public void AssertDispatchedAsTheRunWent(CrewTask earlier, CrewTask later)
    {
        var completion = Assert.Single(Dispatched<TaskCompletedEvent>(), e => e.TaskId == earlier.Id);
        Assert.True(
            IndexOf(completion) < DispatchedBefore(later.Description.Value),
            "the completion of the first task was dispatched only after the second one ran");
    }

    /// <summary>The same tasks, each once, in any order.</summary>
    public static void AssertSameTasks(Orkeon.Domain.Common.TaskId[] expected, IEnumerable<Orkeon.Domain.Common.TaskId> actual)
    {
        var list = actual.ToList();
        Assert.Equal(expected.Length, list.Count);
        foreach (var taskId in expected)
            Assert.Single(list, id => id == taskId);
    }

    public void Dispose() => MemoryScope.Dispose();
}
