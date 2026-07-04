using Orkeon.Application.Context;
using Orkeon.Application.Interfaces;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using AppTaskOutput = Orkeon.Application.Execution.TaskOutput;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for IManagerAgent with call tracking and configurable results.
/// </summary>
public class MockManagerAgent : IManagerAgent
{
    private TaskAssignment _assignResult = new(
        TaskId.From(Guid.NewGuid()),
        AgentId.From(Guid.NewGuid()),
        "Default mock assignment",
        DateTime.UtcNow);

    private bool _reviewResult = true;

    // --- Tracking ---
    public int AssignTaskCallCount { get; private set; }
    public DomainTask? LastAssignedTask { get; private set; }
    public IReadOnlyList<DomainAgent>? LastAvailableAgents { get; private set; }
    public SimpleExecutionContext? LastAssignContext { get; private set; }

    public int ReviewOutputCallCount { get; private set; }
    public AppTaskOutput? LastReviewedOutput { get; private set; }
    public DomainTask? LastReviewedTask { get; private set; }

    // --- Configuration ---
    public void SetAssignResult(TaskAssignment result) => _assignResult = result;

    public void SetReviewResult(bool result) => _reviewResult = result;

    // --- IManagerAgent ---
    public Task<TaskAssignment> AssignTaskAsync(
        DomainTask task,
        IReadOnlyList<DomainAgent> availableAgents,
        SimpleExecutionContext context)
    {
        AssignTaskCallCount++;
        LastAssignedTask = task;
        LastAvailableAgents = availableAgents;
        LastAssignContext = context;
        return Task.FromResult(_assignResult);
    }

    public Task<bool> ReviewOutputAsync(
        AppTaskOutput output,
        DomainTask originalTask)
    {
        ReviewOutputCallCount++;
        LastReviewedOutput = output;
        LastReviewedTask = originalTask;
        return Task.FromResult(_reviewResult);
    }
}
