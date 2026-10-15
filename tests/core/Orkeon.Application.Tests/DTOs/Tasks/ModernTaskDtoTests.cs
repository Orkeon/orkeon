using System.Collections.Immutable;
using Orkeon.Application.Task.DTOs;
using Orkeon.Application.Common.DTOs;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.DTOs.Tasks;

public class TaskDtoTests
{
    private static readonly string[] s_deps12 = [TaskId1, TaskId2];
    private static readonly string[] s_tools12 = ["FileReader", "DataProcessor"];
    private static readonly string[] s_depsAB = ["task-a", "task-b"];
    private static readonly string[] s_tools123 = ["Tool1", "Tool2", "Tool3"];
    private static readonly string[] s_allTools = ["FileReadTool", "WebScrapeTool", "DataProcessorTool", "EmailSenderTool"];

    [Fact]
    public void ShouldCreateValidDto_WhenConstructingWithRequiredProperties()
    {
        // Arrange & Act
        var dto = new TaskDto
        {
            Id = "task-123",
            Name = "Test Task",
            Description = "Perform testing operations",
            ExpectedOutput = "Test results",
            Status = Pending,
            Priority = "Medium"
        };

        // Assert
        Assert.Equal("task-123", dto.Id);
        Assert.Equal("Test Task", dto.Name);
        Assert.Equal("Perform testing operations", dto.Description);
        Assert.Equal("Test results", dto.ExpectedOutput);
        Assert.Equal(Pending, dto.Status);
        Assert.Equal("Medium", dto.Priority);
        Assert.Null(dto.AssignedAgent);
        Assert.True(dto.CreatedAt <= DateTime.UtcNow);
    }

    [Theory]
    [InlineData("Low")]
    [InlineData("Medium")]
    [InlineData("High")]
    [InlineData("Critical")]
    public void ShouldBeSettable_WhenUsingPriorityWithDifferentLevels(string priority)
    {
        // Arrange & Act
        var dto = new TaskDto
        {
            Id = TaskId1,
            Name = "Task",
            Description = "Description",
            ExpectedOutput = "Output",
            Status = Pending,
            Priority = priority
        };

        // Assert
        Assert.Equal(priority, dto.Priority);
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var id = "task-123";
        var name = "Test Task";
        var description = "Description";
        var expectedOutput = "Output";
        var status = Pending;
        var priority = "High";
        var createdAt = DateTime.UtcNow;

        // Act
        var dto1 = new TaskDto
        {
            Id = id,
            Name = name,
            Description = description,
            ExpectedOutput = expectedOutput,
            Status = status,
            Priority = priority,
            CreatedAt = createdAt
        };

        var dto2 = new TaskDto
        {
            Id = id,
            Name = name,
            Description = description,
            ExpectedOutput = expectedOutput,
            Status = status,
            Priority = priority,
            CreatedAt = createdAt
        };

        // Assert
        Assert.Equal(dto1, dto2);
        Assert.Equal(dto1.GetHashCode(), dto2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange & Act
        var dto1 = new TaskDto
        {
            Id = TaskId1,
            Name = "Task 1",
            Description = "Description 1",
            ExpectedOutput = "Output 1",
            Status = Pending,
            Priority = "Low"
        };

        var dto2 = new TaskDto
        {
            Id = TaskId2,
            Name = "Task 2",
            Description = "Description 2",
            ExpectedOutput = "Output 2",
            Status = InProgress,
            Priority = "High"
        };

        // Assert
        Assert.NotEqual(dto1, dto2);
    }

}
