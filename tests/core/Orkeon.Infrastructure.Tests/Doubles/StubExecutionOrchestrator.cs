using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IExecutionOrchestrator"/>: the LLM turn of an agent, scripted. It lets a
/// test run the real <c>AgentExecutionService</c> — and what it stores in the crew's memory — with
/// the answers it chooses (<see cref="Answer"/>).
/// </summary>
public sealed class StubExecutionOrchestrator : IExecutionOrchestrator
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<AgentTurn> _executions = new();

    /// <summary>What an agent answers to a task. By default, a successful <c>"answer of {role}"</c>.</summary>
    public Func<DomainAgent, CrewTask, TaskResult> Answer { get; set; } =
        (agent, _) => new TaskResult(true, $"answer of {agent.Role.Value}", null, [], TimeSpan.Zero);

    /// <summary>Every execution — the task and the context its prompt is built from — in arrival order.</summary>
    public IReadOnlyList<AgentTurn> Contexts => [.. _executions];

    public System.Threading.Tasks.Task<TaskResult> ExecuteTaskCoreAsync(
        DomainAgent agent, CrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        _executions.Enqueue(new AgentTurn(agent, task, context));
        return System.Threading.Tasks.Task.FromResult(Answer(agent, task));
    }

    /// <summary>One execution of an agent turn.</summary>
    public sealed record AgentTurn(DomainAgent Agent, CrewTask Task, SimpleExecutionContext Context);
}
