using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IExecutionOrchestrator"/>: the LLM turn of an agent, scripted. It lets a
/// test run the real <c>AgentExecutionService</c> — and what it stores in the crew's memory — with
/// the answers it chooses (<see cref="Answer"/>).
/// </summary>
public sealed class StubExecutionOrchestrator : IExecutionOrchestrator
{
    /// <summary>What an agent answers to a task. By default, a successful <c>"answer of {role}"</c>.</summary>
    public Func<DomainAgent, CrewTask, TaskResult> Answer { get; set; } =
        (agent, _) => new TaskResult(true, $"answer of {agent.Role.Value}", null, [], TimeSpan.Zero);

    public System.Threading.Tasks.Task<TaskResult> ExecuteTaskCoreAsync(
        DomainAgent agent, CrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(Answer(agent, task));

    public System.Threading.Tasks.Task<TaskExecutionPlan> PlanExecutionAsync(
        DomainAgent agent, CrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(new TaskExecutionPlan(agent.Id, [], TimeSpan.Zero, 1.0));

    public System.Threading.Tasks.Task<ValidationResult> ValidateExecutionAsync(
        DomainAgent agent, CrewTask task, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(new ValidationResult(CanExecute: true));

    public SimpleTaskExecutionContext MapExecutionContext(SimpleExecutionContext applicationContext, DomainAgent agent)
    {
        ArgumentNullException.ThrowIfNull(applicationContext);
        return SimpleTaskExecutionContext.Create(
            variables: applicationContext.Variables,
            previousOutputs: [],
            memory: null,
            availableAgents: null);
    }
}
