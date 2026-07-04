using Orkeon.Domain.Common;
using Orkeon.Domain.Task;

namespace Orkeon.Domain.Tests.Builders;

public class CrewTaskBuilderTests
{
    private static CrewTaskBuilder MinimalTask() =>
        new CrewTaskBuilder().Description("Test task").ExpectedOutput("Expected result");

    [Fact]
    public void Build_WithDescriptionAndOutput_CreatesTask()
    {
        // Act
        var task = MinimalTask().Build();

        // Assert
        Assert.NotNull(task);
        Assert.Equal("Test task", task.Description.Value);
        Assert.Equal("Expected result", task.ExpectedOutput);
    }

    [Fact]
    public void Build_WithoutDescription_ThrowsValidation()
    {
        // Arrange
        var builder = new CrewTaskBuilder().ExpectedOutput("Expected result");

        // Act & Assert
        var ex = Assert.Throws<BuilderValidationException>(() => builder.Build());
        Assert.Equal("Task", ex.BuilderName);
        Assert.Contains("Description is required", ex.Message);
    }

    [Fact]
    public void Build_WithoutExpectedOutput_ThrowsValidation()
    {
        // Arrange
        var builder = new CrewTaskBuilder().Description("Test task");

        // Act & Assert
        var ex = Assert.Throws<BuilderValidationException>(() => builder.Build());
        Assert.Equal("Task", ex.BuilderName);
        Assert.Contains("ExpectedOutput is required", ex.Message);
    }

    [Fact]
    public void Build_WithDependencies_AddsDependencies()
    {
        // Arrange
        var dep1 = TaskId.Create();
        var dep2 = TaskId.Create();

        // Act
        var task = MinimalTask()
            .DependsOn(dep1)
            .DependsOn(dep2)
            .Build();

        // Assert
        Assert.Equal(2, task.Dependencies.Count);
        Assert.Contains(dep1, task.Dependencies);
        Assert.Contains(dep2, task.Dependencies);
    }

    [Fact]
    public void Build_WithContext_AddsContext()
    {
        // Act
        var task = MinimalTask()
            .WithContext("key1", "value1")
            .WithContext("key2", 42)
            .Build();

        // Assert
        Assert.Equal(2, task.Context.Count);
        Assert.Equal("value1", task.Context["key1"]);
        Assert.Equal(42, task.Context["key2"]);
    }

    [Fact]
    public void Build_WithAssignment_AssignsAgent()
    {
        // Arrange
        var agentId = AgentId.Create();

        // Act
        var task = MinimalTask()
            .AssignTo(agentId)
            .Build();

        // Assert
        Assert.NotNull(task.AssignedAgent);
        Assert.Equal(agentId, task.AssignedAgent);
    }

    [Fact]
    public void Build_WithOutputOptions_SetsAllOptions()
    {
        // Act
        var task = MinimalTask()
            .OutputFile("/tmp/output.txt")
            .Async()
            .Build();

        // Assert
        Assert.Equal("/tmp/output.txt", task.OutputFile);
        Assert.True(task.AsyncExecution);
    }

    [Fact]
    public void Build_WithMultipleDependencies_AddsAll()
    {
        // Arrange — create prerequisite tasks and use the DependsOn(params Task[]) overload
        var prereq1 = MinimalTask().Build();
        var prereq2 = MinimalTask().Build();

        // Act
        var task = MinimalTask()
            .DependsOn(prereq1, prereq2)
            .Build();

        // Assert
        Assert.Equal(2, task.Dependencies.Count);
        Assert.Contains(prereq1.Id, task.Dependencies);
        Assert.Contains(prereq2.Id, task.Dependencies);
    }
}
