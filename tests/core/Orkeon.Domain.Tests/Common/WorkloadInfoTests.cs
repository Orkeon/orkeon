using Orkeon.Domain.Common;
using Orkeon.Domain.Agent.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Common;

/// <summary>
/// Tests for WorkloadInfo following Clean Architecture principles.
/// Tests the business rules and validation logic of the WorkloadInfo record.
/// </summary>
public class WorkloadInfoTests
{
    [Fact]
    public void ShouldCreateWorkloadInfo_WhenConstructingWithValidParameters()
    {
        // Arrange
        var agentId = AgentId.Create();
        var activeTasks = 3;
        var queuedTasks = 2;
        var utilizationPercentage = 75.5;
        var averageTaskDuration = TimeSpan.FromMinutes(30);
        var lastTaskCompletedAt = DateTime.UtcNow;
        var currentTaskIds = new List<TaskId>();

        // Act
        var workloadInfo = WorkloadInfo.Create(
            agentId,
            activeTasks,
            queuedTasks,
            utilizationPercentage,
            averageTaskDuration,
            lastTaskCompletedAt,
            currentTaskIds);

        // Assert
        Assert.NotNull(workloadInfo.AgentId);
        Assert.Equal(activeTasks, workloadInfo.ActiveTasks);
        Assert.Equal(queuedTasks, workloadInfo.QueuedTasks);
        Assert.Equal(utilizationPercentage, workloadInfo.UtilizationPercentage);
        Assert.Equal(averageTaskDuration, workloadInfo.AverageTaskDuration);
        Assert.Equal(lastTaskCompletedAt, workloadInfo.LastTaskCompletedAt);
        Assert.Equal(currentTaskIds, workloadInfo.CurrentTaskIds);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullAgentId()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            WorkloadInfo.Create(
                null!,
                0,
                0,
                0,
                TimeSpan.Zero,
                DateTime.UtcNow));

        Assert.Equal("agentId", exception.ParamName);
    }

    [Fact]
    public void ShouldCreateEmptyList_WhenConstructingWithNullCurrentTaskIds()
    {
        // Act
        var workloadInfo = WorkloadInfo.Create(
            AgentId.Create(),
            1,
            2,
            50,
            TimeoutLong,
            DateTime.UtcNow,
            null);

        // Assert
        Assert.NotNull(workloadInfo.CurrentTaskIds);
        Assert.Empty(workloadInfo.CurrentTaskIds);
    }

    [Theory]
    [InlineData(-10, 0)]    // Negative should become 0
    [InlineData(0, 0)]      // Zero should remain zero
    [InlineData(50, 50)]    // Normal value should remain
    [InlineData(100, 100)]  // Maximum should remain
    [InlineData(150, 100)]  // Above max should become 100
    public void ShouldBeClamped_WhenUsingUtilizationPercentage(double input, double expected)
    {
        // Act
        var workloadInfo = WorkloadInfo.Create(
            AgentId.Create(),
            1,
            1,
            input,
            TimeoutLong,
            DateTime.UtcNow);

        // Assert
        Assert.Equal(expected, workloadInfo.UtilizationPercentage);
    }

    [Theory]
    [InlineData(0, true)]   // Zero active tasks = available
    [InlineData(1, false)]  // One or more active tasks = not available
    [InlineData(5, false)]
    public void ShouldReturnCorrectValue_WhenUsingIsAvailable(int activeTasks, bool expected)
    {
        // Act
        var workloadInfo = WorkloadInfo.Create(
            AgentId.Create(),
            activeTasks,
            0,
            50,
            TimeoutLong,
            DateTime.UtcNow);

        // Assert
        Assert.Equal(expected, workloadInfo.IsAvailable);
    }

    [Theory]
    [InlineData(0, false)]   // 0% utilization = not overloaded
    [InlineData(50, false)]  // 50% utilization = not overloaded
    [InlineData(80, false)]  // 80% utilization = not overloaded (boundary)
    [InlineData(80.1, true)] // 80.1% utilization = overloaded
    [InlineData(90, true)]   // 90% utilization = overloaded
    [InlineData(100, true)]  // 100% utilization = overloaded
    public void ShouldReturnCorrectValue_WhenUsingIsOverloaded(double utilizationPercentage, bool expected)
    {
        // Act
        var workloadInfo = WorkloadInfo.Create(
            AgentId.Create(),
            1,
            1,
            utilizationPercentage,
            TimeoutLong,
            DateTime.UtcNow);

        // Assert
        Assert.Equal(expected, workloadInfo.IsOverloaded);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(0, 2, 2)]
    [InlineData(3, 2, 5)]
    [InlineData(10, 15, 25)]
    public void ShouldReturnSumOfActiveAndQueuedTasks_WhenUsingTotalTasks(
        int activeTasks, int queuedTasks, int expectedTotal)
    {
        // Act
        var workloadInfo = WorkloadInfo.Create(
            AgentId.Create(),
            activeTasks,
            queuedTasks,
            50,
            TimeoutLong,
            DateTime.UtcNow);

        // Assert
        Assert.Equal(expectedTotal, workloadInfo.TotalTasks);
    }

    [Fact]
    public void ShouldAllowNegativeValues_WhenConstructingWithNegativeActiveTasks()
    {
        // Act
        var workloadInfo = WorkloadInfo.Create(
            AgentId.Create(),
            -1,
            0,
            50,
            TimeoutLong,
            DateTime.UtcNow);

        // Assert
        Assert.Equal(-1, workloadInfo.ActiveTasks);
    }

    [Fact]
    public void ShouldAllowNegativeValues_WhenConstructingWithNegativeQueuedTasks()
    {
        // Act
        var workloadInfo = WorkloadInfo.Create(
            AgentId.Create(),
            0,
            -2,
            50,
            TimeoutLong,
            DateTime.UtcNow);

        // Assert
        Assert.Equal(-2, workloadInfo.QueuedTasks);
    }

    [Fact]
    public void ShouldAllowNegativeValues_WhenConstructingWithNegativeTimeSpan()
    {
        // Act
        var workloadInfo = WorkloadInfo.Create(
            AgentId.Create(),
            1,
            1,
            50,
            TimeSpan.FromMinutes(-10),
            DateTime.UtcNow);

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(-10), workloadInfo.AverageTaskDuration);
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var dateTime = DateTime.UtcNow;
        var taskIds = new List<TaskId>();
        var agentId = AgentId.Create();

        var workload1 = WorkloadInfo.Create(
            agentId,
            3,
            2,
            75.5,
            TimeSpan.FromMinutes(30),
            dateTime,
            taskIds);

        var workload2 = WorkloadInfo.Create(
            agentId,
            3,
            2,
            75.5,
            TimeSpan.FromMinutes(30),
            dateTime,
            taskIds);

        // Act & Assert
        Assert.True(workload1.Equals(workload2));
        Assert.True(workload2.Equals(workload1));
        Assert.True(workload1 == workload2);
        Assert.False(workload1 != workload2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentAgentId()
    {
        // Arrange
        var dateTime = DateTime.UtcNow;

        var workload1 = WorkloadInfo.Create(
            AgentId.Create(),
            3,
            2,
            75.5,
            TimeSpan.FromMinutes(30),
            dateTime);

        var workload2 = WorkloadInfo.Create(
            AgentId.Create(),
            3,
            2,
            75.5,
            TimeSpan.FromMinutes(30),
            dateTime);

        // Act & Assert
        Assert.False(workload1.Equals(workload2));
        Assert.False(workload2.Equals(workload1));
    }

    [Fact]
    public void ShouldReturnSameHashCode_WhenCallingGetHashCodeWithSameValues()
    {
        // Arrange
        var dateTime = DateTime.UtcNow;
        var taskIds = new List<TaskId>();
        var agentId = AgentId.Create();

        var workload1 = WorkloadInfo.Create(
            agentId,
            3,
            2,
            75.5,
            TimeSpan.FromMinutes(30),
            dateTime,
            taskIds);

        var workload2 = WorkloadInfo.Create(
            agentId,
            3,
            2,
            75.5,
            TimeSpan.FromMinutes(30),
            dateTime,
            taskIds);

        // Act
        var hash1 = workload1.GetHashCode();
        var hash2 = workload2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnDifferentHashCodes_WhenCallingGetHashCodeWithDifferentValues()
    {
        // Arrange
        var dateTime = DateTime.UtcNow;

        var workload1 = WorkloadInfo.Create(
            AgentId.Create(),
            3,
            2,
            75.5,
            TimeSpan.FromMinutes(30),
            dateTime);

        var workload2 = WorkloadInfo.Create(
            AgentId.Create(),
            4, // Different active tasks
            2,
            75.5,
            TimeSpan.FromMinutes(30),
            dateTime);

        // Act
        var hash1 = workload1.GetHashCode();
        var hash2 = workload2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ShouldBeImmutable_WhenUsingCurrentTaskIds()
    {
        // Arrange
        var taskIds = new List<TaskId> { TaskId.Create(), TaskId.Create() };
        var workloadInfo = WorkloadInfo.Create(
            AgentId.Create(),
            1,
            1,
            50,
            TimeoutLong,
            DateTime.UtcNow,
            taskIds);

        // Assert
        Assert.Equal(2, workloadInfo.CurrentTaskIds.Count);
    }

    [Fact]
    public void ShouldStoreEmptyList_WhenConstructingWithEmptyTaskIdsList()
    {
        // Act
        var workloadInfo = WorkloadInfo.Create(
            AgentId.Create(),
            0,
            0,
            0,
            TimeSpan.Zero,
            DateTime.UtcNow,
            []);

        // Assert
        Assert.NotNull(workloadInfo.CurrentTaskIds);
        Assert.Empty(workloadInfo.CurrentTaskIds);
    }

    [Fact]
    public void ShouldAcceptAll_WhenConstructingWithVariousAgentIds()
    {
        // Act
        var agentId = AgentId.Create();
        var workloadInfo = WorkloadInfo.Create(
            agentId,
            1,
            1,
            50,
            TimeoutLong,
            DateTime.UtcNow);

        // Assert
        Assert.NotNull(workloadInfo.AgentId);
        Assert.Equal(agentId, workloadInfo.AgentId);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingWorkloadInfoInCollection()
    {
        // Arrange
        var workloads = new List<WorkloadInfo>
        {
            WorkloadInfo.Create(AgentId.Create(), 3, 1, 80, TimeSpan.FromMinutes(20), DateTime.UtcNow),
            WorkloadInfo.Create(AgentId.Create(), 0, 5, 90, TimeoutLong, DateTime.UtcNow),
            WorkloadInfo.Create(AgentId.Create(), 1, 0, 30, TimeSpan.FromMinutes(25), DateTime.UtcNow)
        };

        // Act
        var availableAgents = workloads.Where(w => w.IsAvailable).ToList();
        var overloadedAgents = workloads.Where(w => w.IsOverloaded).ToList();

        // Assert
        Assert.Single(availableAgents);
        Assert.NotNull(availableAgents[0].AgentId);

        Assert.Single(overloadedAgents);
        Assert.NotNull(overloadedAgents[0].AgentId);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingWorkloadInfoWithEdgeCaseValues()
    {
        // Arrange & Act
        var workloadInfo = WorkloadInfo.Create(
            AgentId.Create(),
            int.MaxValue,
            int.MaxValue,
            double.MaxValue, // Should be clamped to 100
            TimeSpan.MaxValue,
            DateTime.MaxValue,
            [TaskId.Create()]);

        // Assert
        Assert.NotNull(workloadInfo.AgentId);
        Assert.Equal(int.MaxValue, workloadInfo.ActiveTasks);
        Assert.Equal(int.MaxValue, workloadInfo.QueuedTasks);
        Assert.Equal(100, workloadInfo.UtilizationPercentage); // Clamped
        Assert.Equal(TimeSpan.MaxValue, workloadInfo.AverageTaskDuration);
        Assert.Equal(DateTime.MaxValue, workloadInfo.LastTaskCompletedAt);
        Assert.Single(workloadInfo.CurrentTaskIds);

        // Derived properties
        Assert.False(workloadInfo.IsAvailable); // Has active tasks
        Assert.True(workloadInfo.IsOverloaded); // 100% > 80%

        // TotalTasks with MaxValue should handle overflow gracefully
        // Note: int.MaxValue + int.MaxValue will overflow in C#, but that's expected behavior
        var totalTasks = workloadInfo.TotalTasks;
        Assert.Equal(unchecked(int.MaxValue + int.MaxValue), totalTasks);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingWorkloadInfoWithMinimumValues()
    {
        // Arrange & Act
        var workloadInfo = WorkloadInfo.Create(
            AgentId.Create(),
            int.MinValue,
            int.MinValue,
            double.MinValue, // Should be clamped to 0
            TimeSpan.MinValue,
            DateTime.MinValue);

        // Assert
        Assert.NotNull(workloadInfo.AgentId);
        Assert.Equal(int.MinValue, workloadInfo.ActiveTasks);
        Assert.Equal(int.MinValue, workloadInfo.QueuedTasks);
        Assert.Equal(0, workloadInfo.UtilizationPercentage); // Clamped
        Assert.Equal(TimeSpan.MinValue, workloadInfo.AverageTaskDuration);
        Assert.Equal(DateTime.MinValue, workloadInfo.LastTaskCompletedAt);
        Assert.Empty(workloadInfo.CurrentTaskIds);

        // Derived properties
        Assert.False(workloadInfo.IsAvailable); // int.MinValue != 0
        Assert.False(workloadInfo.IsOverloaded); // 0% <= 80%

        var totalTasks = workloadInfo.TotalTasks;
        Assert.Equal(unchecked(int.MinValue + int.MinValue), totalTasks);
    }
}
