using Orkeon.Application.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Application.Tests.Agent;

/// <summary>
/// Tests for the CastToDomainTask helper in AgentExecutionService.
/// </summary>
public class AgentExecutionServiceTests
{
    [Fact]
    public void CastToDomainTask_WithCrewTask_ReturnsCastTask()
    {
        // Arrange
        ICrewTask task = CrewTask.Create(
            TaskDescription.From("Test task"),
            ExpectedOutput.From("Expected output"));

        // Act
        var result = AgentExecutionService.CastToDomainTask(task);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<CrewTask>(result);
        Assert.Same(task, result);
    }

    [Fact]
    public void CastToDomainTask_WithNullTask_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            AgentExecutionService.CastToDomainTask(null!));
    }

    [Fact]
    public void CastToDomainTask_WithNonCrewTask_ThrowsInvalidOperationException()
    {
        // Arrange
        ICrewTask task = new FakeNonCrewTask();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AgentExecutionService.CastToDomainTask(task));
        Assert.Contains("Task must be a Domain Task", ex.Message);
    }

    /// <summary>
    /// A stub implementation of ICrewTask that is NOT a CrewTask,
    /// used to verify the cast failure path.
    /// </summary>
    private sealed class FakeNonCrewTask : ICrewTask
    {
        public TaskId TaskId { get; } = TaskId.Create();
        public TaskDescription Description { get; } = TaskDescription.From("Fake");
        public ExpectedOutput ExpectedOutput { get; } = ExpectedOutput.From("Fake");
        public AgentId? AssignedAgent => null;
        public TaskStatus Status => TaskStatus.Pending;
        public TaskOutput? Output => null;
        public IReadOnlyList<TaskId> Dependencies { get; } = [];
        public DateTime CreatedAt { get; } = DateTime.UtcNow;
        public DateTime? StartedAt => null;
        public DateTime? CompletedAt => null;
        public bool AsyncExecution => false;
        public JsonSchema? OutputJson => null;
        public Type? OutputPydantic => null;
        public string? OutputFile => null;
        public bool HumanInput => false;

        public void AssignTo(AgentId agentId) { }
        public void Start(AgentId agentId) { }
        public void Complete(AgentId agentId, TaskOutput output) { }
        public void Fail(string errorMessage, Exception? exception = null) { }
        public bool CanExecute(Func<TaskId, bool> isTaskCompleted) => true;
        public ValidationResult ValidateOutput(TaskOutput output) => ValidationResult.Success();
        public string GetContextSummary() => "Fake";
        public TimeSpan GetExecutionTime() => TimeSpan.Zero;
    }
}
