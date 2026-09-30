using System.Collections.Concurrent;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;

namespace Orkeon.Infrastructure.Tests.Strategies.FailedTask;

/// <summary>
/// GAP-03 — what the six "a failed task fails the crew" suites share: in-memory repositories,
/// an execution double that fails the tasks a test names and records which ones it ran, a
/// recording hook, and the assertion every mode must pass — the rule Sequential set in
/// STUDIO-12 C5a / LLM-11.
/// </summary>
internal sealed class FailedTaskFixture : IDisposable
{
    private readonly ConcurrentQueue<string> _executed = new();
    private readonly ConcurrentDictionary<string, string> _failures = new(StringComparer.Ordinal);
    private readonly InMemoryTaskRepository _tasks;
    private readonly InMemoryAgentRepository _agents;

    public FailedTaskFixture()
    {
        var unitOfWork = new NullUnitOfWork();
        _tasks = new InMemoryTaskRepository(unitOfWork);
        _agents = new InMemoryAgentRepository(unitOfWork);
        Execution.SetExecuteFunc((_, task, _, _) =>
        {
            var name = task.Description.Value;
            _executed.Enqueue(name);
            return _failures.TryGetValue(name, out var error)
                ? new TaskResult(false, "", null, [], TimeSpan.Zero, Error: error)
                : new TaskResult(true, $"{name} done", null, [], TimeSpan.Zero);
        });
    }

    public MockAgentExecutionService Execution { get; } = new();
    public MockMemoryScope MemoryScope { get; } = new();
    public MockCrewExecutionHook Hook { get; } = new();

    /// <summary>The descriptions of the tasks an agent was asked to run, in order.</summary>
    public IReadOnlyList<string> Executed => [.. _executed];

    public CrewStrategyDependencies Dependencies =>
        new(_tasks, _agents, Execution, MemoryScope);

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

    public static DomainCrew Build(CrewBuilder builder, IEnumerable<DomainAgent> agents, IEnumerable<CrewTask> tasks)
    {
        foreach (var agent in agents)
            builder.WithAgent(agent);
        foreach (var task in tasks)
            builder.WithTask(task);
        return builder.Build();
    }

    /// <summary>
    /// The rule of the six modes: the crew failed, its error names every task that did not
    /// succeed, and the hook heard a <see cref="CrewHookStatus.Failed"/> carrying that same
    /// reason — never a completion.
    /// </summary>
    public void AssertFailed(DomainCrewOutput output, params CrewTask[] failedTasks)
    {
        Assert.False(output.Success);
        Assert.NotNull(output.Error);
        foreach (var task in failedTasks)
            Assert.Contains(task.Id.ToString(), output.Error, StringComparison.Ordinal);

        Assert.Empty(Hook.Completions);
        var failure = Assert.Single(Hook.Failures);
        Assert.Equal(CrewHookStatus.Failed, failure.Status);
        Assert.Equal(output.Error, failure.FailureReason);
    }

    public void AssertCompleted(DomainCrewOutput output)
    {
        Assert.True(output.Success, output.Error);
        Assert.Null(output.Error);
        Assert.Empty(Hook.Failures);
        Assert.Equal(CrewHookStatus.Completed, Assert.Single(Hook.Completions).Status);
    }

    public void Dispose() => MemoryScope.Dispose();
}
