using Orkeon.Application.Context;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Crew;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using TaskOutput = Orkeon.Application.Execution.TaskOutput;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IManagerAgent"/> that gives every task to the first worker and accepts every
/// output: a hierarchical crew runs offline.
/// </summary>
public sealed class StubAcceptingManagerAgent : IManagerAgent
{
    /// <inheritdoc />
    public Task<TaskAssignment> AssignTaskAsync(
        DomainTask task, IReadOnlyList<DomainAgent> availableAgents, SimpleExecutionContext context, ManagerLlm llm, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TaskAssignment(task.Id, availableAgents[0].Id, "first worker", DateTime.UtcNow));

    /// <inheritdoc />
    public Task<bool> ReviewOutputAsync(TaskOutput output, DomainTask originalTask, ManagerLlm llm, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}
