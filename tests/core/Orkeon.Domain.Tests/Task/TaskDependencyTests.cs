using Orkeon.Domain.Task;

using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;
namespace Orkeon.Domain.Tests.Task;

public class TaskDependencyTests
{
    [Fact]
    public void ShouldInitializeWithDefaults_WhenConstructingUsingParameterless()
    {
        // Act
        var dependency = new TaskDependency();

        // Assert
        Assert.NotNull(dependency);
        Assert.NotNull(dependency.TaskId);
        Assert.NotNull(dependency.DependsOnTaskId);
        Assert.Equal(DependencyType.Hard, dependency.Type);
        Assert.Null(dependency.Condition);
        Assert.Null(dependency.MaxWaitTime);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenConstructingWithParameters()
    {
        // Arrange
        var taskId = TaskId.Create();
        var dependsOnTaskId = TaskId.Create();
        var type = DependencyType.Soft;

        // Act
        var dependency = new TaskDependency(taskId, dependsOnTaskId, type);

        // Assert
        Assert.Equal(taskId, dependency.TaskId);
        Assert.Equal(dependsOnTaskId, dependency.DependsOnTaskId);
        Assert.Equal(type, dependency.Type);
        Assert.Null(dependency.Condition);
        Assert.Null(dependency.MaxWaitTime);
    }

    [Fact]
    public void ShouldUseHardTypeAsDefault_WhenConstructingWithMinimalParameters()
    {
        // Arrange
        var taskId = TaskId.Create();
        var dependsOnTaskId = TaskId.Create();

        // Act
        var dependency = new TaskDependency(taskId, dependsOnTaskId);

        // Assert
        Assert.Equal(taskId, dependency.TaskId);
        Assert.Equal(dependsOnTaskId, dependency.DependsOnTaskId);
        Assert.Equal(DependencyType.Hard, dependency.Type);
    }

    [Fact]
    public void ShouldSetTaskId_WhenConstructingWithParameters()
    {
        // Arrange
        var taskId = TaskId.Create();
        var dependsOnTaskId = TaskId.Create();

        // Act
        var dependency = new TaskDependency(taskId, dependsOnTaskId);

        // Assert
        Assert.Equal(taskId, dependency.TaskId);
    }

    [Fact]
    public void ShouldSetDependsOnTaskId_WhenConstructingWithParameters()
    {
        // Arrange
        var taskId = TaskId.Create();
        var dependsOnTaskId = TaskId.Create();

        // Act
        var dependency = new TaskDependency(taskId, dependsOnTaskId);

        // Assert
        Assert.Equal(dependsOnTaskId, dependency.DependsOnTaskId);
    }

    [Fact]
    public void ShouldSetType_WhenConstructingWithEachDependencyType()
    {
        // Arrange
        var taskId = TaskId.Create();
        var dependsOnTaskId = TaskId.Create();

        // Act & Assert for each type
        var hard = new TaskDependency(taskId, dependsOnTaskId, DependencyType.Hard);
        Assert.Equal(DependencyType.Hard, hard.Type);

        var soft = new TaskDependency(taskId, dependsOnTaskId, DependencyType.Soft);
        Assert.Equal(DependencyType.Soft, soft.Type);

        var timed = new TaskDependency(taskId, dependsOnTaskId, DependencyType.Timed);
        Assert.Equal(DependencyType.Timed, timed.Type);
    }

    [Fact]
    public void ShouldSetCondition_WhenConstructingWithCondition()
    {
        // Arrange
        var taskId = TaskId.Create();
        var dependsOnTaskId = TaskId.Create();
        var condition = "result.success == true";

        // Act
        var dependency = new TaskDependency(taskId, dependsOnTaskId, condition: condition);

        // Assert
        Assert.Equal(condition, dependency.Condition);
    }

    [Fact]
    public void ShouldHaveNullConditionByDefault_WhenConstructingWithoutCondition()
    {
        // Arrange
        var dependency = new TaskDependency();

        // Assert
        Assert.Null(dependency.Condition);
    }

    [Fact]
    public void ShouldSetMaxWaitTime_WhenConstructingWithMaxWaitTime()
    {
        // Arrange
        var taskId = TaskId.Create();
        var dependsOnTaskId = TaskId.Create();
        var maxWaitTime = TimeoutStandard;

        // Act
        var dependency = new TaskDependency(taskId, dependsOnTaskId, maxWaitTime: maxWaitTime);

        // Assert
        Assert.Equal(maxWaitTime, dependency.MaxWaitTime);
    }

    [Fact]
    public void ShouldHaveNullMaxWaitTimeByDefault_WhenConstructingWithoutMaxWaitTime()
    {
        // Arrange
        var dependency = new TaskDependency();

        // Assert
        Assert.Null(dependency.MaxWaitTime);
    }

    [Fact]
    public void ShouldBeCreatedCorrectly_WhenUsingHardDependency()
    {
        // Arrange
        var task1 = TaskId.Create();
        var task2 = TaskId.Create();

        // Act
        var dependency = new TaskDependency(task1, task2, DependencyType.Hard);

        // Assert
        Assert.Equal(task2, dependency.DependsOnTaskId);
        Assert.Equal(DependencyType.Hard, dependency.Type);
    }

    [Fact]
    public void ShouldBeCreatedCorrectly_WhenUsingSoftDependency()
    {
        // Arrange
        var task1 = TaskId.Create();
        var task2 = TaskId.Create();

        // Act
        var dependency = new TaskDependency(task1, task2, DependencyType.Soft);

        // Assert
        Assert.Equal(task2, dependency.DependsOnTaskId);
        Assert.Equal(DependencyType.Soft, dependency.Type);
    }

    [Fact]
    public void ShouldBeCreatedWithMaxWaitTime_WhenUsingTimedDependency()
    {
        // Arrange
        var task1 = TaskId.Create();
        var task2 = TaskId.Create();

        // Act
        var dependency = new TaskDependency(task1, task2, DependencyType.Timed, maxWaitTime: TimeoutQuick);

        // Assert
        Assert.Equal(task2, dependency.DependsOnTaskId);
        Assert.Equal(DependencyType.Timed, dependency.Type);
        Assert.Equal(TimeoutQuick, dependency.MaxWaitTime);
    }

    [Fact]
    public void ShouldBeCreatedWithCondition_WhenUsingConditionalDependency()
    {
        // Arrange
        var task1 = TaskId.Create();
        var task2 = TaskId.Create();

        // Act
        var dependency = new TaskDependency(task1, task2, condition: "output.status == 'approved'");

        // Assert
        Assert.Equal(task2, dependency.DependsOnTaskId);
        Assert.Equal("output.status == 'approved'", dependency.Condition);
    }

    [Fact]
    public void ShouldSupportAllProperties_WhenUsingComplexDependency()
    {
        // Arrange
        var finalTask = TaskId.Create();
        var approvalTask = TaskId.Create();

        // Act
        var dependency = new TaskDependency(
            finalTask,
            approvalTask,
            DependencyType.Timed,
            condition: "approval.granted == true && approval.score > 0.8",
            maxWaitTime: TimeSpan.FromHours(2));

        // Assert
        Assert.Equal(approvalTask, dependency.DependsOnTaskId);
        Assert.Equal(DependencyType.Timed, dependency.Type);
        Assert.Equal("approval.granted == true && approval.score > 0.8", dependency.Condition);
        Assert.Equal(TimeSpan.FromHours(2), dependency.MaxWaitTime);
    }

    [Fact]
    public void ShouldCreateWithNewIds_WhenUsingDefaultConstructor()
    {
        // Act
        var dependency = new TaskDependency();

        // Assert
        Assert.NotNull(dependency.TaskId);
        Assert.NotNull(dependency.DependsOnTaskId);
    }

    [Fact]
    public void ShouldBeCorrect_WhenUsingDependencyTypeUsingEnumValues()
    {
        // Assert
        Assert.Equal(0, (int)DependencyType.Hard);
        Assert.Equal(1, (int)DependencyType.Soft);
        Assert.Equal(2, (int)DependencyType.Timed);
    }

    [Fact]
    public void ShouldSetAllProperties_WhenConstructingWithAllParameters()
    {
        // Arrange
        var taskId = TaskId.Create();
        var dependsOnTaskId = TaskId.Create();

        // Act
        var dependency = new TaskDependency(
            taskId,
            dependsOnTaskId,
            DependencyType.Soft,
            condition: "new condition",
            maxWaitTime: TimeoutExtended);

        // Assert - All properties should be set
        Assert.Equal(taskId, dependency.TaskId);
        Assert.Equal(dependsOnTaskId, dependency.DependsOnTaskId);
        Assert.Equal(DependencyType.Soft, dependency.Type);
        Assert.Equal("new condition", dependency.Condition);
        Assert.Equal(TimeoutExtended, dependency.MaxWaitTime);
    }
}
