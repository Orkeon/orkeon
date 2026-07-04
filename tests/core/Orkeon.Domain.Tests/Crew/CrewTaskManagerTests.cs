using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Domain.Tests.Crew;

/// <summary>
/// Tests for CrewTaskManager internal domain helper.
/// Validates task management operations: add, remove, query.
/// </summary>
public class CrewTaskManagerTests
{
    #region Constructor

    [Fact]
    public void ShouldInitialize_WhenConstructingWithEmptyList()
    {
        // Arrange
        var tasks = new List<TaskId>();

        // Act
        var manager = new CrewTaskManager(tasks);

        // Assert
        Assert.Empty(manager.Tasks);
        Assert.False(manager.Any());
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullList()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CrewTaskManager(null!));
    }

    #endregion

    #region AddTask

    [Fact]
    public void ShouldAddTask_WhenStatusIsIdle()
    {
        // Arrange
        var manager = new CrewTaskManager([]);
        var taskId = TaskId.Create();

        // Act
        manager.AddTask(taskId, CrewStatus.Idle);

        // Assert
        Assert.Single(manager.Tasks);
        Assert.Equal(taskId, manager.Tasks[0]);
    }

    [Fact]
    public void ShouldAddMultipleTasks_WhenAddingSequentially()
    {
        // Arrange
        var manager = new CrewTaskManager([]);
        var task1 = TaskId.Create();
        var task2 = TaskId.Create();
        var task3 = TaskId.Create();

        // Act
        manager.AddTask(task1, CrewStatus.Idle);
        manager.AddTask(task2, CrewStatus.Idle);
        manager.AddTask(task3, CrewStatus.Idle);

        // Assert
        Assert.Equal(3, manager.Tasks.Count);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenAddingNullTaskId()
    {
        // Arrange
        var manager = new CrewTaskManager([]);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => manager.AddTask(null!, CrewStatus.Idle));
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenAddingDuplicateTask()
    {
        // Arrange
        var manager = new CrewTaskManager([]);
        var taskId = TaskId.Create();
        manager.AddTask(taskId, CrewStatus.Idle);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => manager.AddTask(taskId, CrewStatus.Idle));
        Assert.Contains("already in this crew", ex.Message);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenAddingTaskDuringExecution()
    {
        // Arrange
        var manager = new CrewTaskManager([]);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => manager.AddTask(TaskId.Create(), CrewStatus.Executing));
        Assert.Contains("Cannot add tasks while crew is executing", ex.Message);
    }

    [Theory]
    [InlineData("Created")]
    [InlineData("Idle")]
    [InlineData(Failed)]
    [InlineData(Completed)]
    [InlineData("Paused")]
    public void ShouldAddTask_WhenStatusIsNotExecuting(string statusStr)
    {
        // Arrange
        var status = CrewStatus.From(statusStr);
        var manager = new CrewTaskManager([]);
        var taskId = TaskId.Create();

        // Act
        manager.AddTask(taskId, status);

        // Assert
        Assert.Single(manager.Tasks);
    }

    #endregion

    #region RemoveTask

    [Fact]
    public void ShouldRemoveTask_WhenTaskExistsAndNotExecuting()
    {
        // Arrange
        var manager = new CrewTaskManager([]);
        var taskId = TaskId.Create();
        manager.AddTask(taskId, CrewStatus.Idle);

        // Act
        manager.RemoveTask(taskId, "No longer needed", CrewStatus.Idle);

        // Assert
        Assert.Empty(manager.Tasks);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenRemovingNullTaskId()
    {
        // Arrange
        var manager = new CrewTaskManager([]);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => manager.RemoveTask(null!, "reason", CrewStatus.Idle));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ShouldThrowArgumentException_WhenRemovingWithEmptyReason(string reason)
    {
        // Arrange
        var manager = new CrewTaskManager([]);
        var taskId = TaskId.Create();
        manager.AddTask(taskId, CrewStatus.Idle);

        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => manager.RemoveTask(taskId, reason, CrewStatus.Idle));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenRemovingWithNullReason()
    {
        // Arrange
        var manager = new CrewTaskManager([]);
        var taskId = TaskId.Create();
        manager.AddTask(taskId, CrewStatus.Idle);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => manager.RemoveTask(taskId, null!, CrewStatus.Idle));
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenRemovingTaskNotInCrew()
    {
        // Arrange
        var manager = new CrewTaskManager([]);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => manager.RemoveTask(TaskId.Create(), "reason", CrewStatus.Idle));
        Assert.Contains("is not in this crew", ex.Message);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenRemovingTaskDuringExecution()
    {
        // Arrange
        var manager = new CrewTaskManager([]);
        var taskId = TaskId.Create();
        manager.AddTask(taskId, CrewStatus.Idle);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => manager.RemoveTask(taskId, "reason", CrewStatus.Executing));
        Assert.Contains("Cannot remove tasks while crew is executing", ex.Message);
    }

    #endregion

    #region Any

    [Fact]
    public void ShouldReturnTrue_WhenCrewHasTasks()
    {
        // Arrange
        var manager = new CrewTaskManager([]);
        manager.AddTask(TaskId.Create(), CrewStatus.Idle);

        // Act & Assert
        Assert.True(manager.Any());
    }

    [Fact]
    public void ShouldReturnFalse_WhenCrewHasNoTasks()
    {
        // Arrange
        var manager = new CrewTaskManager([]);

        // Act & Assert
        Assert.False(manager.Any());
    }

    [Fact]
    public void ShouldReturnFalse_WhenAllTasksAreRemoved()
    {
        // Arrange
        var manager = new CrewTaskManager([]);
        var taskId = TaskId.Create();
        manager.AddTask(taskId, CrewStatus.Idle);
        manager.RemoveTask(taskId, "Cleanup", CrewStatus.Idle);

        // Act & Assert
        Assert.False(manager.Any());
    }

    #endregion
}
