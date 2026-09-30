using Orkeon.Application.Context;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Crew;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using TaskOutput = Orkeon.Application.Execution.TaskOutput;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IManagerAgent"/> double: assigns every task to the first worker and
/// rejects every output, so a hierarchical crew ends with a task "[NEEDS REVISION]".
/// </summary>
public sealed class StubRejectingManagerAgent : IManagerAgent
{
    public Task<TaskAssignment> AssignTaskAsync(
        DomainTask task, IReadOnlyList<DomainAgent> availableAgents, SimpleExecutionContext context) =>
        Task.FromResult(new TaskAssignment(task.Id, availableAgents[0].Id, "first worker", DateTime.UtcNow));

    public Task<bool> ReviewOutputAsync(TaskOutput output, DomainTask originalTask) =>
        Task.FromResult(false);
}
