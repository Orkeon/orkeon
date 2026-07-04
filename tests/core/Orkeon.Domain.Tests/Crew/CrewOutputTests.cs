using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Crew;

public class CrewOutputTests
{
    [Fact]
    public void ShouldInitializeOutput_WhenConstructingWithValidParameters()
    {
        // Arrange
        var output = "Crew execution completed successfully";
        var structuredOutput = new { result = "success", data = 123 };
        var taskOutputs = CreateTaskOutputs();
        var success = true;
        var executionTime = TimeoutStandard;
        var error = (string?)null;
        var metadata = CrewMetadata.CreateBuilder()
            .AddExecutionId("test-run")
            .AddTag("unit-test")
            .Build();

        // Act
        var crewOutput = new CrewOutput(
            output,
            structuredOutput,
            taskOutputs,
            success,
            executionTime,
            error,
            metadata);

        // Assert
        Assert.NotNull(crewOutput);
        Assert.Equal(output, crewOutput.Output);
        Assert.Equal(structuredOutput, crewOutput.StructuredOutput);
        Assert.Equal(2, crewOutput.TaskOutputs.Count);
        Assert.True(crewOutput.Success);
        Assert.Equal(executionTime, crewOutput.ExecutionTime);
        Assert.Null(crewOutput.Error);
        Assert.Equal(metadata, crewOutput.Metadata);
        Assert.True(crewOutput.CompletedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldUseEmptyString_WhenConstructingWithNullOutput()
    {
        // Arrange & Act
        var crewOutput = new CrewOutput(
            null!,
            null,
            [],
            true,
            TimeSpan.Zero);

        // Assert
        Assert.Equal(string.Empty, crewOutput.Output);
    }

    [Fact]
    public void ShouldUseEmptyList_WhenConstructingWithNullTaskOutputs()
    {
        // Arrange & Act
        var crewOutput = new CrewOutput(
            "output",
            null,
            null!,
            true,
            TimeSpan.Zero);

        // Assert
        Assert.NotNull(crewOutput.TaskOutputs);
        Assert.Empty(crewOutput.TaskOutputs);
    }

    [Fact]
    public void ShouldUseEmpty_WhenConstructingWithNullMetadata()
    {
        // Arrange & Act
        var crewOutput = new CrewOutput(
            "output",
            null,
            [],
            true,
            TimeSpan.Zero,
            null,
            null);

        // Assert
        Assert.Equal(CrewMetadata.Empty, crewOutput.Metadata);
    }

    [Fact]
    public void ShouldCreateSuccessfulOutput_WhenCreatingSuccess()
    {
        // Arrange
        var output = "Success output";
        var structuredOutput = new { status = "completed" };
        var taskOutputs = CreateTaskOutputs();
        var executionTime = TimeoutQuick;
        var metadata = CrewMetadata.CreateBuilder()
            .AddExecutionId("success-run")
            .AddTag("test")
            .Build();

        // Act
        var crewOutput = CrewOutput.CreateSuccess(
            output,
            structuredOutput,
            taskOutputs,
            executionTime,
            metadata);

        // Assert
        Assert.Equal(output, crewOutput.Output);
        Assert.Equal(structuredOutput, crewOutput.StructuredOutput);
        Assert.Equal(2, crewOutput.TaskOutputs.Count);
        Assert.True(crewOutput.Success);
        Assert.Equal(executionTime, crewOutput.ExecutionTime);
        Assert.Null(crewOutput.Error);
        Assert.Equal(metadata, crewOutput.Metadata);
    }

    [Fact]
    public void ShouldCreateFailedOutput_WhenCreatingFailure()
    {
        // Arrange
        var error = "Critical error during execution";
        var taskOutputs = CreateTaskOutputs();
        var executionTime = TimeSpan.FromSeconds(15);
        var metadata = CrewMetadata.CreateBuilder()
            .AddExecutionId("failure-run")
            .AddTag("test")
            .Build();

        // Act
        var crewOutput = CrewOutput.CreateFailure(
            error,
            taskOutputs,
            executionTime,
            metadata);

        // Assert
        Assert.Equal(string.Empty, crewOutput.Output);
        Assert.Null(crewOutput.StructuredOutput);
        Assert.Equal(2, crewOutput.TaskOutputs.Count);
        Assert.False(crewOutput.Success);
        Assert.Equal(executionTime, crewOutput.ExecutionTime);
        Assert.Equal(error, crewOutput.Error);
        Assert.Equal(metadata, crewOutput.Metadata);
    }

    [Fact]
    public void ShouldReturnNoTasksMessage_WhenGettingTaskSummaryWithNoTasks()
    {
        // Arrange
        var crewOutput = CrewOutput.CreateSuccess(
            "output",
            null,
            [],
            TimeSpan.Zero);

        // Act
        var summary = crewOutput.GetTaskSummary();

        // Assert
        Assert.Equal("No tasks executed.", summary);
    }

    [Fact]
    public void ShouldReturnFormattedSummary_WhenGettingTaskSummaryWithSuccessfulTasks()
    {
        // Arrange
        var taskOutputs = new List<TaskOutput>
        {
            TaskOutput.Create(
                "Task 1 completed",
                "text",
                null,
                TaskId.From(Guid.NewGuid()),
                true,
                TimeSpan.FromSeconds(10)),
            TaskOutput.Create(
                "Task 2 completed",
                "text",
                null,
                TaskId.From(Guid.NewGuid()),
                true,
                TimeSpan.FromSeconds(20))
        };

        var crewOutput = CrewOutput.CreateSuccess(
            "output",
            null,
            taskOutputs,
            TimeoutQuick);

        // Act
        var summary = crewOutput.GetTaskSummary();

        // Assert
        Assert.Contains(Success, summary);
        Assert.Contains("Task 1 completed", summary);
        Assert.Contains("Task 2 completed", summary);
        Assert.Equal(2, summary.Split('\n').Length);
    }

    [Fact]
    public void ShouldShowSuccessAndFailure_WhenGettingTaskSummaryWithMixedResults()
    {
        // Arrange
        var taskOutputs = new List<TaskOutput>
        {
            TaskOutput.Create(
                "Task succeeded",
                "text",
                null,
                TaskId.From(Guid.NewGuid()),
                true,
                TimeSpan.FromSeconds(10)),
            TaskOutput.Create(
                "Task failed with error",
                "text",
                null,
                TaskId.From(Guid.NewGuid()),
                false,
                TimeSpan.FromSeconds(5))
        };

        var crewOutput = new CrewOutput(
            "output",
            null,
            taskOutputs,
            false,
            TimeSpan.FromSeconds(15));

        // Act
        var summary = crewOutput.GetTaskSummary();

        // Assert
        Assert.Contains(Success, summary);
        Assert.Contains(Failed, summary);
        Assert.Contains("Task succeeded", summary);
        Assert.Contains("Task failed with error", summary);
    }

    [Fact]
    public void ShouldReturnZeroStatistics_WhenGettingStatisticsWithNoTasks()
    {
        // Arrange
        var crewOutput = CrewOutput.CreateSuccess(
            "output",
            null,
            [],
            TimeSpan.FromMinutes(1));

        // Act
        var stats = crewOutput.GetStatistics();

        // Assert
        Assert.Equal(0, stats.TotalTasks);
        Assert.Equal(0, stats.SuccessfulTasks);
        Assert.Equal(0, stats.FailedTasks);
        Assert.Equal(TimeSpan.FromMinutes(1), stats.TotalExecutionTime);
        Assert.Equal(TimeSpan.Zero, stats.AverageTaskTime);
        Assert.Equal(0, stats.SuccessRate);
    }

    [Fact]
    public void ShouldReturn100PercentSuccess_WhenGettingStatisticsWithAllSuccessfulTasks()
    {
        // Arrange
        var taskOutputs = new List<TaskOutput>
        {
            TaskOutput.Create(
                "Task 1",
                "text",
                null,
                TaskId.From(Guid.NewGuid()),
                true,
                TimeSpan.FromSeconds(10)),
            TaskOutput.Create(
                "Task 2",
                "text",
                null,
                TaskId.From(Guid.NewGuid()),
                true,
                TimeSpan.FromSeconds(20)),
            TaskOutput.Create(
                "Task 3",
                "text",
                null,
                TaskId.From(Guid.NewGuid()),
                true,
                TimeoutQuick)
        };

        var crewOutput = CrewOutput.CreateSuccess(
            "output",
            null,
            taskOutputs,
            TimeSpan.FromMinutes(1));

        // Act
        var stats = crewOutput.GetStatistics();

        // Assert
        Assert.Equal(3, stats.TotalTasks);
        Assert.Equal(3, stats.SuccessfulTasks);
        Assert.Equal(0, stats.FailedTasks);
        Assert.Equal(TimeSpan.FromMinutes(1), stats.TotalExecutionTime);
        Assert.Equal(TimeSpan.FromSeconds(20), stats.AverageTaskTime); // (10+20+30)/3 = 20
        Assert.Equal(100.0, stats.SuccessRate);
    }

    [Fact]
    public void ShouldCalculateCorrectPercentage_WhenGettingStatisticsWithMixedResults()
    {
        // Arrange
        var taskOutputs = new List<TaskOutput>
        {
            TaskOutput.Create(
                "Task 1",
                "text",
                null,
                TaskId.From(Guid.NewGuid()),
                true,
                TimeSpan.FromSeconds(10)),
            TaskOutput.Create(
                "Task 2",
                "text",
                null,
                TaskId.From(Guid.NewGuid()),
                false,
                TimeSpan.FromSeconds(5)),
            TaskOutput.Create(
                "Task 3",
                "text",
                null,
                TaskId.From(Guid.NewGuid()),
                true,
                TimeSpan.FromSeconds(15)),
            TaskOutput.Create(
                "Task 4",
                "text",
                null,
                TaskId.From(Guid.NewGuid()),
                false,
                TimeSpan.FromSeconds(10))
        };

        var crewOutput = new CrewOutput(
            "output",
            null,
            taskOutputs,
            false,
            TimeSpan.FromSeconds(40));

        // Act
        var stats = crewOutput.GetStatistics();

        // Assert
        Assert.Equal(4, stats.TotalTasks);
        Assert.Equal(2, stats.SuccessfulTasks);
        Assert.Equal(2, stats.FailedTasks);
        Assert.Equal(TimeSpan.FromSeconds(40), stats.TotalExecutionTime);
        Assert.Equal(TimeSpan.FromSeconds(10), stats.AverageTaskTime); // (10+5+15+10)/4 = 10
        Assert.Equal(50.0, stats.SuccessRate); // 2/4 * 100 = 50%
    }

    [Fact]
    public void ShouldBeReadOnly_WhenUsingTaskOutputs()
    {
        // Arrange
        var taskOutputs = new List<TaskOutput>
        {
            TaskOutput.Create(
                "Task 1",
                "text",
                null,
                TaskId.From(Guid.NewGuid()),
                true,
                TimeSpan.FromSeconds(10))
        };

        // Act
        var crewOutput = new CrewOutput(
            "output",
            null,
            taskOutputs,
            true,
            TimeSpan.Zero);

        // Assert
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<TaskOutput>>(crewOutput.TaskOutputs);
    }

    [Fact]
    public void ShouldBeSetToCurrentTime_WhenUsingCompletedAt()
    {
        // Arrange
        var timeBefore = DateTime.UtcNow;

        // Act
        var crewOutput = CrewOutput.CreateSuccess(
            "output",
            null,
            [],
            TimeSpan.Zero);
        var timeAfter = DateTime.UtcNow;

        // Assert
        Assert.True(crewOutput.CompletedAt >= timeBefore);
        Assert.True(crewOutput.CompletedAt <= timeAfter);
    }

    [Fact]
    public void ShouldReturnZero_WhenUsingExecutionStatisticsSuccessRateWithZeroTasks()
    {
        // Arrange
        var stats = new ExecutionStatistics(
            TotalTasks: 0,
            SuccessfulTasks: 0,
            FailedTasks: 0,
            TotalExecutionTime: TimeSpan.Zero,
            AverageTaskTime: TimeSpan.Zero);

        // Act & Assert
        Assert.Equal(0, stats.SuccessRate);
    }

    [Fact]
    public void ShouldCalculateCorrectly_WhenUsingExecutionStatisticsSuccessRate()
    {
        // Arrange
        var testCases = new[]
        {
            (total: 10, successful: 10, expected: 100.0),
            (total: 10, successful: 5, expected: 50.0),
            (total: 4, successful: 3, expected: 75.0),
            (total: 100, successful: 33, expected: 33.0),
            (total: 3, successful: 1, expected: 33.333333333333336)
        };

        foreach (var (total, successful, expected) in testCases)
        {
            // Arrange
            var stats = new ExecutionStatistics(
                TotalTasks: total,
                SuccessfulTasks: successful,
                FailedTasks: total - successful,
                TotalExecutionTime: TimeSpan.Zero,
                AverageTaskTime: TimeSpan.Zero);

            // Act & Assert
            Assert.Equal(expected, stats.SuccessRate, 10); // Use 10 decimal places precision
        }
    }

    // Helper method to create test task outputs
    private static List<TaskOutput> CreateTaskOutputs()
    {
        return
        [
            TaskOutput.Create(
                "First task output",
                "text",
                null,
                TaskId.From(Guid.NewGuid()),
                true,
                TimeSpan.FromSeconds(10)),
            TaskOutput.Create(
                "Second task output",
                "text",
                null,
                TaskId.From(Guid.NewGuid()),
                true,
                TimeSpan.FromSeconds(20))
        ];
    }
}
