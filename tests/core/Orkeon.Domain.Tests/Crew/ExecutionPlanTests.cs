using Orkeon.Domain.Crew;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.Tests.Crew;

public class ExecutionPlanTests
{
    [Fact]
    public void ShouldCreatePlanWithOrderedTasks_WhenCreatingWithTaskIds()
    {
        // Arrange
        var taskIds = new List<TaskId>
        {
            TaskId.Create(),
            TaskId.Create(),
            TaskId.Create()
        };

        // Act
        var plan = ExecutionPlan.Create(taskIds);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(3, plan.Tasks.Count);
        for (int i = 0; i < taskIds.Count; i++)
        {
            Assert.Equal(taskIds[i], plan.Tasks[i].TaskId);
            Assert.Equal(i, plan.Tasks[i].ExecutionOrder);
        }
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenCreatingWithNullTaskIds()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => ExecutionPlan.Create((IEnumerable<TaskId>)null!));
        Assert.Equal("taskIds", exception.ParamName);
    }

    [Fact]
    public void ShouldCreateEmptyPlan_WhenCreatingWithEmptyTaskIds()
    {
        // Arrange
        var taskIds = new List<TaskId>();

        // Act
        var plan = ExecutionPlan.Create(taskIds);

        // Assert
        Assert.Empty(plan.Tasks);
        Assert.Empty(plan.Assignments);
    }

    [Fact]
    public void ShouldCreateEmptyPlan_WhenCreatingUsingParameterless()
    {
        // Act
        var plan = ExecutionPlan.Create();

        // Assert
        Assert.NotNull(plan);
        Assert.Empty(plan.Tasks);
        Assert.Empty(plan.Assignments);
    }

    [Fact]
    public void ShouldReturnNewPlanWithTask_WhenUsingWithTaskWithValidTask()
    {
        // Arrange
        var plan = ExecutionPlan.Create();
        var taskId = TaskId.Create();
        var plannedTask = PlannedTask.Create(taskId, 0);

        // Act
        var newPlan = plan.WithTask(plannedTask);

        // Assert
        Assert.Empty(plan.Tasks); // Original unchanged
        Assert.Single(newPlan.Tasks);
        Assert.Equal(plannedTask, newPlan.Tasks[0]);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingWithTaskWithNullTask()
    {
        // Arrange
        var plan = ExecutionPlan.Create();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => plan.WithTask(null!));
        Assert.Equal("task", exception.ParamName);
    }

    [Fact]
    public void ShouldMaintainInsertionOrder_WhenUsingWithTaskWithMultipleTasks()
    {
        // Arrange
        var task1 = PlannedTask.Create(TaskId.Create(), 2);
        var task2 = PlannedTask.Create(TaskId.Create(), 0);
        var task3 = PlannedTask.Create(TaskId.Create(), 1);

        // Act
        var plan = ExecutionPlan.Create()
            .WithTask(task1)
            .WithTask(task2)
            .WithTask(task3);

        // Assert
        Assert.Equal(3, plan.Tasks.Count);
        Assert.Equal(task1, plan.Tasks[0]);
        Assert.Equal(task2, plan.Tasks[1]);
        Assert.Equal(task3, plan.Tasks[2]);
    }

    [Fact]
    public void ShouldReturnNewPlanWithAssignment_WhenUsingWithAgentAssignmentWithValidIds()
    {
        // Arrange
        var plan = ExecutionPlan.Create();
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();

        // Act
        var newPlan = plan.WithAgentAssignment(taskId, agentId);

        // Assert
        Assert.Empty(plan.Assignments); // Original unchanged
        Assert.Single(newPlan.Assignments);
        Assert.Equal(agentId, newPlan.Assignments[taskId]);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingWithAgentAssignmentWithNullTaskId()
    {
        // Arrange
        var plan = ExecutionPlan.Create();
        var agentId = AgentId.Create();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => plan.WithAgentAssignment(null!, agentId));
        Assert.Equal("taskId", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingWithAgentAssignmentWithNullAgentId()
    {
        // Arrange
        var plan = ExecutionPlan.Create();
        var taskId = TaskId.Create();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => plan.WithAgentAssignment(taskId, null!));
        Assert.Equal("agentId", exception.ParamName);
    }

    [Fact]
    public void ShouldUpdateAssignment_WhenUsingWithAgentAssignmentReassigningSameTask()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId1 = AgentId.Create();
        var agentId2 = AgentId.Create();

        // Act
        var plan = ExecutionPlan.Create()
            .WithAgentAssignment(taskId, agentId1)
            .WithAgentAssignment(taskId, agentId2);

        // Assert
        Assert.Single(plan.Assignments);
        Assert.Equal(agentId2, plan.Assignments[taskId]);
    }

    [Fact]
    public void ShouldReturnAgent_WhenUsingGetAssignedAgentWithExistingAssignment()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var plan = ExecutionPlan.Create()
            .WithAgentAssignment(taskId, agentId);

        // Act
        var result = plan.GetAssignedAgent(taskId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(agentId, result);
    }

    [Fact]
    public void ShouldReturnNull_WhenUsingGetAssignedAgentWithNoAssignment()
    {
        // Arrange
        var plan = ExecutionPlan.Create();
        var taskId = TaskId.Create();

        // Act
        var result = plan.GetAssignedAgent(taskId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldReturnTasksSortedByExecutionOrder_WhenGettingTasksInOrder()
    {
        // Arrange
        var task1 = PlannedTask.Create(TaskId.Create(), 2);
        var task2 = PlannedTask.Create(TaskId.Create(), 0);
        var task3 = PlannedTask.Create(TaskId.Create(), 1);

        var plan = ExecutionPlan.Create()
            .WithTask(task1)
            .WithTask(task2)
            .WithTask(task3);

        // Act
        var orderedTasks = plan.GetTasksInOrder().ToList();

        // Assert
        Assert.Equal(3, orderedTasks.Count);
        Assert.Equal(task2, orderedTasks[0]); // Order 0
        Assert.Equal(task3, orderedTasks[1]); // Order 1
        Assert.Equal(task1, orderedTasks[2]); // Order 2
    }

    [Fact]
    public void ShouldReturnEmpty_WhenGettingTasksInOrderWithEmptyPlan()
    {
        // Arrange
        var plan = ExecutionPlan.Create();

        // Act
        var orderedTasks = plan.GetTasksInOrder();

        // Assert
        Assert.Empty(orderedTasks);
    }

    [Fact]
    public void ShouldGroupByExecutionOrder_WhenGettingParallelGroupsWithNoParallelGroups()
    {
        // Arrange
        var plan = ExecutionPlan.Create()
            .WithTask(PlannedTask.Create(TaskId.Create(), 0))
            .WithTask(PlannedTask.Create(TaskId.Create(), 1))
            .WithTask(PlannedTask.Create(TaskId.Create(), 2));

        // Act
        var groups = plan.GetParallelGroups().ToList();

        // Assert
        Assert.Equal(3, groups.Count);
        foreach (var group in groups)
        {
            Assert.Single(group);
        }
    }

    [Fact]
    public void ShouldGroupCorrectly_WhenGettingParallelGroupsWithParallelTasks()
    {
        // Arrange
        var plan = ExecutionPlan.Create()
            .WithTask(PlannedTask.Create(TaskId.Create(), 0, parallelGroup: 0))
            .WithTask(PlannedTask.Create(TaskId.Create(), 1, parallelGroup: 0))
            .WithTask(PlannedTask.Create(TaskId.Create(), 2, parallelGroup: 1))
            .WithTask(PlannedTask.Create(TaskId.Create(), 3, parallelGroup: 1))
            .WithTask(PlannedTask.Create(TaskId.Create(), 4)); // No parallel group

        // Act
        var groups = plan.GetParallelGroups().OrderBy(g => g.Key).ToList();

        // Assert
        Assert.Equal(3, groups.Count);
        Assert.Equal(2, groups[0].Count()); // Group 0
        Assert.Equal(2, groups[1].Count()); // Group 1
        Assert.Single(groups[2]); // Task without parallel group
    }

    [Fact]
    public void ShouldBeEqual_WhenGettingEqualityComponentsWithSameTasks()
    {
        // Arrange
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();
        var agentId = AgentId.Create();

        var plan1 = ExecutionPlan.Create()
            .WithTask(PlannedTask.Create(taskId1, 0))
            .WithTask(PlannedTask.Create(taskId2, 1))
            .WithAgentAssignment(taskId1, agentId);

        var plan2 = ExecutionPlan.Create()
            .WithTask(PlannedTask.Create(taskId1, 0))
            .WithTask(PlannedTask.Create(taskId2, 1))
            .WithAgentAssignment(taskId1, agentId);

        // Act & Assert
        Assert.Equal(plan1, plan2);
        Assert.Equal(plan1.GetHashCode(), plan2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenGettingEqualityComponentsWithDifferentTasks()
    {
        // Arrange
        var plan1 = ExecutionPlan.Create()
            .WithTask(PlannedTask.Create(TaskId.Create(), 0));

        var plan2 = ExecutionPlan.Create()
            .WithTask(PlannedTask.Create(TaskId.Create(), 0));

        // Act & Assert
        Assert.NotEqual(plan1, plan2);
    }

    [Fact]
    public void ShouldNotMutateOriginal_WhenUsingWithTaskOnExistingPlan()
    {
        // Arrange
        var original = ExecutionPlan.Create();
        var task = PlannedTask.Create(TaskId.Create(), 0);

        // Act
        var modified = original.WithTask(task);

        // Assert
        Assert.Empty(original.Tasks);
        Assert.Single(modified.Tasks);
    }

    [Fact]
    public void ShouldNotMutateOriginal_WhenUsingWithAgentAssignmentOnExistingPlan()
    {
        // Arrange
        var original = ExecutionPlan.Create();
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();

        // Act
        var modified = original.WithAgentAssignment(taskId, agentId);

        // Assert
        Assert.Empty(original.Assignments);
        Assert.Single(modified.Assignments);
    }

    [Fact]
    public void ShouldInitialize_WhenUsingPlannedTaskCreateWithValidParameters()
    {
        // Arrange
        var taskId = TaskId.Create();
        var executionOrder = 5;
        var parallelGroup = 2;
        var dependencies = new List<TaskId> { TaskId.Create(), TaskId.Create() };
        var instructions = "Special handling required";

        // Act
        var plannedTask = PlannedTask.Create(
            taskId,
            executionOrder,
            parallelGroup,
            dependencies,
            instructions);

        // Assert
        Assert.Equal(taskId, plannedTask.TaskId);
        Assert.Equal(executionOrder, plannedTask.ExecutionOrder);
        Assert.Equal(parallelGroup, plannedTask.ParallelGroup);
        Assert.Equal(2, plannedTask.Dependencies.Count);
        Assert.Equal(instructions, plannedTask.Instructions);
    }

    [Fact]
    public void ShouldUseDefaults_WhenUsingPlannedTaskCreateWithMinimalParameters()
    {
        // Arrange
        var taskId = TaskId.Create();
        var executionOrder = 0;

        // Act
        var plannedTask = PlannedTask.Create(taskId, executionOrder);

        // Assert
        Assert.Equal(taskId, plannedTask.TaskId);
        Assert.Equal(executionOrder, plannedTask.ExecutionOrder);
        Assert.Null(plannedTask.ParallelGroup);
        Assert.Empty(plannedTask.Dependencies);
        Assert.Null(plannedTask.Instructions);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingPlannedTaskCreateWithNullTaskId()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => PlannedTask.Create(null!, 0));
        Assert.Equal("taskId", exception.ParamName);
    }

    [Fact]
    public void ShouldCreateEmptyList_WhenUsingPlannedTaskCreateWithNullDependencies()
    {
        // Arrange
        var taskId = TaskId.Create();

        // Act
        var plannedTask = PlannedTask.Create(taskId, 0, dependencies: null);

        // Assert
        Assert.NotNull(plannedTask.Dependencies);
        Assert.Empty(plannedTask.Dependencies);
    }

    [Fact]
    public void ShouldBeEqual_WhenUsingPlannedTaskGettingEqualityComponentsWithSameValues()
    {
        // Arrange
        var taskId = TaskId.Create();
        var deps = new List<TaskId> { TaskId.Create() };

        var task1 = PlannedTask.Create(taskId, 1, 2, deps, "instructions");
        var task2 = PlannedTask.Create(taskId, 1, 2, deps, "instructions");

        // Act & Assert
        Assert.Equal(task1, task2);
        Assert.Equal(task1.GetHashCode(), task2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenUsingPlannedTaskGettingEqualityComponentsWithDifferentValues()
    {
        // Arrange
        var taskId = TaskId.Create();

        var task1 = PlannedTask.Create(taskId, 1);
        var task2 = PlannedTask.Create(taskId, 2);

        // Act & Assert
        Assert.NotEqual(task1, task2);
    }

    [Fact]
    public void ShouldBeReadOnly_WhenUsingPlannedTaskUsingDependencies()
    {
        // Arrange
        var dependencies = new List<TaskId> { TaskId.Create() };

        // Act
        var plannedTask = PlannedTask.Create(
            TaskId.Create(),
            0,
            dependencies: dependencies);

        // Assert
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<TaskId>>(
            plannedTask.Dependencies);
    }

    [Fact]
    public void ShouldBeReadOnly_WhenUsingAssignments()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();

        // Act
        var plan = ExecutionPlan.Create()
            .WithAgentAssignment(taskId, agentId);
        var assignments = plan.Assignments;

        // Assert
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyDictionary<TaskId, AgentId>>(
            assignments);
    }
}
