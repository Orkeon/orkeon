using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Domain.Tests.Process;

/// <summary>
/// Tests for TaskAssignment following Clean Architecture principles.
/// Tests the task assignment record for tracking agent-task relationships.
/// </summary>
public class TaskAssignmentTests
{
    #region Constructor and Basic Property Tests

    [Fact]
    public void ShouldCreateValidAssignment_WhenConstructingWithValidParameters()
    {
        // Arrange
        var taskId = TaskId.Create();
        var assignedAgent = AgentId.Create();
        var reason = "Agent has required expertise";
        var assignedAt = DateTime.UtcNow;

        // Act
        var assignment = new TaskAssignment(taskId, assignedAgent, reason, assignedAt);

        // Assert
        Assert.Equal(taskId, assignment.TaskId);
        Assert.Equal(assignedAgent, assignment.AssignedAgent);
        Assert.Equal(reason, assignment.Reason);
        Assert.Equal(assignedAt, assignment.AssignedAt);
    }

    [Fact]
    public void ShouldAcceptEmptyString_WhenConstructingWithEmptyReason()
    {
        // Arrange
        var taskId = TaskId.Create();
        var assignedAgent = AgentId.Create();
        var emptyReason = string.Empty;
        var assignedAt = DateTime.UtcNow;

        // Act
        var assignment = new TaskAssignment(taskId, assignedAgent, emptyReason, assignedAt);

        // Assert
        Assert.Equal(emptyReason, assignment.Reason);
        Assert.Equal(string.Empty, assignment.Reason);
    }

    [Fact]
    public void ShouldPreserveDateTime_WhenConstructingWithSpecificDateTime()
    {
        // Arrange
        var taskId = TaskId.Create();
        var assignedAgent = AgentId.Create();
        var reason = "Automatic assignment";
        var specificDateTime = new DateTime(2024, 1, 15, 14, 30, 0, DateTimeKind.Utc);

        // Act
        var assignment = new TaskAssignment(taskId, assignedAgent, reason, specificDateTime);

        // Assert
        Assert.Equal(specificDateTime, assignment.AssignedAt);
        Assert.Equal(DateTimeKind.Utc, assignment.AssignedAt.Kind);
        Assert.Equal(2024, assignment.AssignedAt.Year);
        Assert.Equal(1, assignment.AssignedAt.Month);
        Assert.Equal(15, assignment.AssignedAt.Day);
    }

    [Theory]
    [InlineData("Agent has domain expertise")]
    [InlineData("Load balancing assignment")]
    [InlineData("Reassignment due to agent availability")]
    [InlineData("Manual assignment by manager")]
    [InlineData("")] // Empty reason
    public void ShouldAcceptAll_WhenConstructingWithVariousReasons(string reason)
    {
        // Arrange
        var taskId = TaskId.Create();
        var assignedAgent = AgentId.Create();
        var assignedAt = DateTime.UtcNow;

        // Act
        var assignment = new TaskAssignment(taskId, assignedAgent, reason, assignedAt);

        // Assert
        Assert.Equal(reason, assignment.Reason);
        Assert.Equal(taskId, assignment.TaskId);
        Assert.Equal(assignedAgent, assignment.AssignedAgent);
    }

    #endregion

    #region Identity Properties Tests

    [Fact]
    public void ShouldHaveValidGuidValue_WhenUsingTaskId()
    {
        // Arrange & Act
        var taskId = TaskId.Create();
        var assignment = new TaskAssignment(taskId, AgentId.Create(), "Test reason", DateTime.UtcNow);

        // Assert
        Assert.NotEqual<object>(Guid.Empty, assignment.TaskId.Value);
        Assert.Equal(taskId.Value, assignment.TaskId.Value);
    }

    [Fact]
    public void ShouldHaveValidGuidValue_WhenUsingAssignedAgent()
    {
        // Arrange & Act
        var agentId = AgentId.Create();
        var assignment = new TaskAssignment(TaskId.Create(), agentId, "Test reason", DateTime.UtcNow);

        // Assert
        Assert.NotEqual<object>(Guid.Empty, assignment.AssignedAgent.Value);
        Assert.Equal(agentId.Value, assignment.AssignedAgent.Value);
    }

    [Fact]
    public void ShouldBeDifferent_WhenUsingTaskIdAndAgentId()
    {
        // Arrange & Act
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var assignment = new TaskAssignment(taskId, agentId, "Test reason", DateTime.UtcNow);

        // Assert
        Assert.NotEqual(assignment.TaskId.Value, assignment.AssignedAgent.Value);
    }

    [Fact]
    public void ShouldPreserveGuids_WhenConstructingWithSpecificGuids()
    {
        // Arrange
        var taskGuid = Guid.NewGuid();
        var agentGuid = Guid.NewGuid();
        var taskId = TaskId.From(taskGuid);
        var agentId = AgentId.From(agentGuid);

        // Act
        var assignment = new TaskAssignment(taskId, agentId, "Specific GUID test", DateTime.UtcNow);

        // Assert — verify identifiers were preserved through Guid -> ULID conversion
        Assert.Equal(taskId, assignment.TaskId);
        Assert.Equal(agentId, assignment.AssignedAgent);
    }

    #endregion

    #region Record Equality and HashCode Tests

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var taskId = TaskId.From(Guid.NewGuid());
        var agentId = AgentId.From(Guid.NewGuid());
        var reason = "Same reason";
        var assignedAt = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var assignment1 = new TaskAssignment(taskId, agentId, reason, assignedAt);
        var assignment2 = new TaskAssignment(taskId, agentId, reason, assignedAt);

        // Act & Assert
        Assert.Equal(assignment1, assignment2);
        Assert.True(assignment1.Equals(assignment2));
        Assert.True(assignment1 == assignment2);
        Assert.False(assignment1 != assignment2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentTaskId()
    {
        // Arrange
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();
        var agentId = AgentId.Create();
        var reason = "Test reason";
        var assignedAt = DateTime.UtcNow;

        var assignment1 = new TaskAssignment(taskId1, agentId, reason, assignedAt);
        var assignment2 = new TaskAssignment(taskId2, agentId, reason, assignedAt);

        // Act & Assert
        Assert.NotEqual(assignment1, assignment2);
        Assert.False(assignment1.Equals(assignment2));
        Assert.False(assignment1 == assignment2);
        Assert.True(assignment1 != assignment2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentAssignedAgent()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId1 = AgentId.Create();
        var agentId2 = AgentId.Create();
        var reason = "Test reason";
        var assignedAt = DateTime.UtcNow;

        var assignment1 = new TaskAssignment(taskId, agentId1, reason, assignedAt);
        var assignment2 = new TaskAssignment(taskId, agentId2, reason, assignedAt);

        // Act & Assert
        Assert.NotEqual(assignment1, assignment2);
        Assert.False(assignment1.Equals(assignment2));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentReason()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var assignedAt = DateTime.UtcNow;

        var assignment1 = new TaskAssignment(taskId, agentId, "Reason 1", assignedAt);
        var assignment2 = new TaskAssignment(taskId, agentId, "Reason 2", assignedAt);

        // Act & Assert
        Assert.NotEqual(assignment1, assignment2);
        Assert.False(assignment1.Equals(assignment2));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentAssignedAt()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var reason = "Test reason";
        var assignedAt1 = DateTime.UtcNow;
        var assignedAt2 = DateTime.UtcNow.AddHours(1);

        var assignment1 = new TaskAssignment(taskId, agentId, reason, assignedAt1);
        var assignment2 = new TaskAssignment(taskId, agentId, reason, assignedAt2);

        // Act & Assert
        Assert.NotEqual(assignment1, assignment2);
        Assert.False(assignment1.Equals(assignment2));
    }

    [Fact]
    public void ShouldReturnSameHashCode_WhenCallingGetHashCodeWithSameValues()
    {
        // Arrange
        var taskId = TaskId.From(Guid.NewGuid());
        var agentId = AgentId.From(Guid.NewGuid());
        var reason = "Test reason";
        var assignedAt = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var assignment1 = new TaskAssignment(taskId, agentId, reason, assignedAt);
        var assignment2 = new TaskAssignment(taskId, agentId, reason, assignedAt);

        // Act
        var hash1 = assignment1.GetHashCode();
        var hash2 = assignment2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnDifferentHashCodes_WhenCallingGetHashCodeWithDifferentValues()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var assignedAt = DateTime.UtcNow;

        var assignment1 = new TaskAssignment(taskId, agentId, "Reason 1", assignedAt);
        var assignment2 = new TaskAssignment(taskId, agentId, "Reason 2", assignedAt);

        // Act
        var hash1 = assignment1.GetHashCode();
        var hash2 = assignment2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    #endregion

    #region Record Deconstruction Tests

    [Fact]
    public void ShouldExtractAllProperties_WhenUsingDeconstruct()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var reason = "Deconstruction test";
        var assignedAt = DateTime.UtcNow;

        var assignment = new TaskAssignment(taskId, agentId, reason, assignedAt);

        // Act
        var (extractedTaskId, extractedAgentId, extractedReason, extractedAssignedAt) = assignment;

        // Assert
        Assert.Equal(taskId, extractedTaskId);
        Assert.Equal(agentId, extractedAgentId);
        Assert.Equal(reason, extractedReason);
        Assert.Equal(assignedAt, extractedAssignedAt);
    }

    [Fact]
    public void ShouldExtractEmptyString_WhenUsingDeconstructWithEmptyReason()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var emptyReason = string.Empty;
        var assignedAt = DateTime.UtcNow;

        var assignment = new TaskAssignment(taskId, agentId, emptyReason, assignedAt);

        // Act
        var (_, _, extractedReason, _) = assignment;

        // Assert
        Assert.Equal(string.Empty, extractedReason);
    }

    #endregion

    #region Record With Expression Tests

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedReason_WhenUsingWithModifyReason()
    {
        // Arrange
        var originalAssignment = new TaskAssignment(
            TaskId.Create(),
            AgentId.Create(),
            "Original reason",
            DateTime.UtcNow);

        // Act
        var modifiedAssignment = originalAssignment with { Reason = "Modified reason" };

        // Assert
        Assert.Equal("Original reason", originalAssignment.Reason);
        Assert.Equal("Modified reason", modifiedAssignment.Reason);
        Assert.Equal(originalAssignment.TaskId, modifiedAssignment.TaskId);
        Assert.Equal(originalAssignment.AssignedAgent, modifiedAssignment.AssignedAgent);
        Assert.Equal(originalAssignment.AssignedAt, modifiedAssignment.AssignedAt);
        Assert.NotEqual(originalAssignment, modifiedAssignment);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedAgent_WhenUsingWithModifyAssignedAgent()
    {
        // Arrange
        var originalAgent = AgentId.Create();
        var newAgent = AgentId.Create();
        var originalAssignment = new TaskAssignment(
            TaskId.Create(),
            originalAgent,
            "Test reason",
            DateTime.UtcNow);

        // Act
        var modifiedAssignment = originalAssignment with { AssignedAgent = newAgent };

        // Assert
        Assert.Equal(originalAgent, originalAssignment.AssignedAgent);
        Assert.Equal(newAgent, modifiedAssignment.AssignedAgent);
        Assert.Equal(originalAssignment.TaskId, modifiedAssignment.TaskId);
        Assert.Equal(originalAssignment.Reason, modifiedAssignment.Reason);
        Assert.NotEqual(originalAssignment, modifiedAssignment);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedTaskId_WhenUsingWithModifyTaskId()
    {
        // Arrange
        var originalTaskId = TaskId.Create();
        var newTaskId = TaskId.Create();
        var originalAssignment = new TaskAssignment(
            originalTaskId,
            AgentId.Create(),
            "Test reason",
            DateTime.UtcNow);

        // Act
        var modifiedAssignment = originalAssignment with { TaskId = newTaskId };

        // Assert
        Assert.Equal(originalTaskId, originalAssignment.TaskId);
        Assert.Equal(newTaskId, modifiedAssignment.TaskId);
        Assert.Equal(originalAssignment.AssignedAgent, modifiedAssignment.AssignedAgent);
        Assert.Equal(originalAssignment.Reason, modifiedAssignment.Reason);
        Assert.NotEqual(originalAssignment, modifiedAssignment);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedDateTime_WhenUsingWithModifyAssignedAt()
    {
        // Arrange
        var originalDateTime = DateTime.UtcNow;
        var newDateTime = DateTime.UtcNow.AddHours(2);
        var originalAssignment = new TaskAssignment(
            TaskId.Create(),
            AgentId.Create(),
            "Test reason",
            originalDateTime);

        // Act
        var modifiedAssignment = originalAssignment with { AssignedAt = newDateTime };

        // Assert
        Assert.Equal(originalDateTime, originalAssignment.AssignedAt);
        Assert.Equal(newDateTime, modifiedAssignment.AssignedAt);
        Assert.Equal(originalAssignment.TaskId, modifiedAssignment.TaskId);
        Assert.Equal(originalAssignment.AssignedAgent, modifiedAssignment.AssignedAgent);
        Assert.NotEqual(originalAssignment, modifiedAssignment);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithAllUpdates_WhenUsingWithModifyMultipleProperties()
    {
        // Arrange
        var originalAssignment = new TaskAssignment(
            TaskId.Create(),
            AgentId.Create(),
            "Original reason",
            DateTime.UtcNow);

        var newAgent = AgentId.Create();
        var newReason = "Updated reason";
        var newDateTime = DateTime.UtcNow.AddDays(1);

        // Act
        var modifiedAssignment = originalAssignment with
        {
            AssignedAgent = newAgent,
            Reason = newReason,
            AssignedAt = newDateTime
        };

        // Assert
        Assert.Equal(originalAssignment.TaskId, modifiedAssignment.TaskId); // Unchanged
        Assert.Equal(newAgent, modifiedAssignment.AssignedAgent);
        Assert.Equal(newReason, modifiedAssignment.Reason);
        Assert.Equal(newDateTime, modifiedAssignment.AssignedAt);
        Assert.NotEqual(originalAssignment, modifiedAssignment);
    }

    #endregion

    #region DateTime Handling Tests

    [Fact]
    public void ShouldPreserveKind_WhenUsingAssignedAtWithDifferentDateTimeKinds()
    {
        // Arrange
        var utcDateTime = DateTime.UtcNow;
        var localDateTime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Local);
        var unspecifiedDateTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

        // Act
        var utcAssignment = new TaskAssignment(TaskId.Create(), AgentId.Create(), "UTC", utcDateTime);
        var localAssignment = new TaskAssignment(TaskId.Create(), AgentId.Create(), "Local", localDateTime);
        var unspecifiedAssignment = new TaskAssignment(TaskId.Create(), AgentId.Create(), "Unspecified", unspecifiedDateTime);

        // Assert
        Assert.Equal(DateTimeKind.Utc, utcAssignment.AssignedAt.Kind);
        Assert.Equal(DateTimeKind.Local, localAssignment.AssignedAt.Kind);
        Assert.Equal(DateTimeKind.Unspecified, unspecifiedAssignment.AssignedAt.Kind);
    }

    [Fact]
    public void ShouldPreservePrecision_WhenUsingAssignedAtWithPreciseDateTime()
    {
        // Arrange
        var preciseDateTime = new DateTime(2024, 3, 15, 14, 30, 25, 123, DateTimeKind.Utc)
            .AddTicks(4567); // Add microsecond precision

        // Act
        var assignment = new TaskAssignment(TaskId.Create(), AgentId.Create(), "Precision test", preciseDateTime);

        // Assert
        Assert.Equal(preciseDateTime, assignment.AssignedAt);
        Assert.Equal(preciseDateTime.Ticks, assignment.AssignedAt.Ticks);
        Assert.Equal(123, assignment.AssignedAt.Millisecond);
    }

    [Theory]
    [InlineData(2020, 1, 1)]
    [InlineData(2024, 12, 31)]
    [InlineData(2025, 6, 15)]
    public void ShouldPreserveDates_WhenUsingAssignedAtWithSpecificDates(int year, int month, int day)
    {
        // Arrange
        var specificDate = new DateTime(year, month, day, 10, 30, 0, DateTimeKind.Utc);

        // Act
        var assignment = new TaskAssignment(TaskId.Create(), AgentId.Create(), "Date test", specificDate);

        // Assert
        Assert.Equal(year, assignment.AssignedAt.Year);
        Assert.Equal(month, assignment.AssignedAt.Month);
        Assert.Equal(day, assignment.AssignedAt.Day);
        Assert.Equal(10, assignment.AssignedAt.Hour);
        Assert.Equal(30, assignment.AssignedAt.Minute);
    }

    #endregion

    #region Collection and Integration Tests

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingTaskAssignmentInCollection()
    {
        // Arrange
        var agent1 = AgentId.Create();
        var agent2 = AgentId.Create();
        var task1 = TaskId.Create();
        var task2 = TaskId.Create();
        var task3 = TaskId.Create();

        var assignments = new List<TaskAssignment>
        {
            new(task1, agent1, "Agent 1 expertise", DateTime.UtcNow),
            new(task2, agent2, "Agent 2 availability", DateTime.UtcNow.AddMinutes(5)),
            new(task3, agent1, "Agent 1 workload", DateTime.UtcNow.AddMinutes(10)),
        };

        // Act
        var agent1Assignments = assignments.Where(a => a.AssignedAgent.Equals(agent1)).ToList();
        var taskAssignments = assignments.Where(a => a.TaskId.Equals(task2)).ToList();
        var expertiseAssignments = assignments.Where(a => a.Reason.Contains("expertise")).ToList();

        // Assert
        Assert.Equal(3, assignments.Count);
        Assert.Equal(2, agent1Assignments.Count);
        Assert.Single(taskAssignments);
        Assert.Single(expertiseAssignments);
    }

    [Fact]
    public void ShouldPreventDuplicates_WhenUsingTaskAssignmentInHashSet()
    {
        // Arrange
        var taskId = TaskId.From(Guid.NewGuid());
        var agentId = AgentId.From(Guid.NewGuid());
        var reason = "Same assignment";
        var assignedAt = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var assignment1 = new TaskAssignment(taskId, agentId, reason, assignedAt);
        var assignment2 = new TaskAssignment(taskId, agentId, reason, assignedAt); // Identical
        var assignment3 = new TaskAssignment(taskId, agentId, "Different reason", assignedAt);

        var hashSet = new HashSet<TaskAssignment>
        {
            // Act
            assignment1,
            assignment2, // Should be treated as duplicate
            assignment3
        };

        // Assert
        Assert.Equal(2, hashSet.Count); // assignment1 and assignment2 are equal
        Assert.Contains(assignment1, hashSet);
        Assert.Contains(assignment3, hashSet);
    }

    [Fact]
    public void ShouldWorkAsKey_WhenUsingTaskAssignmentInDictionary()
    {
        // Arrange
        var assignment1 = new TaskAssignment(TaskId.Create(), AgentId.Create(), "Assignment 1", DateTime.UtcNow);
        var assignment2 = new TaskAssignment(TaskId.Create(), AgentId.Create(), "Assignment 2", DateTime.UtcNow.AddMinutes(1));

        var dictionary = new Dictionary<TaskAssignment, string>
        {
            { assignment1, "In progress" },
            { assignment2, Completed }
        };

        // Act & Assert
        Assert.Equal(2, dictionary.Count);
        Assert.Equal("In progress", dictionary[assignment1]);
        Assert.Equal(Completed, dictionary[assignment2]);
        Assert.True(dictionary.ContainsKey(assignment1));
        Assert.True(dictionary.ContainsKey(assignment2));
    }

    [Fact]
    public void ShouldSortCorrectly_WhenUsingTaskAssignmentOrderingByAssignedAt()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var assignments = new List<TaskAssignment>
        {
            new(TaskId.Create(), AgentId.Create(), "Third", baseTime.AddMinutes(10)),
            new(TaskId.Create(), AgentId.Create(), "First", baseTime),
            new(TaskId.Create(), AgentId.Create(), "Second", baseTime.AddMinutes(5))
        };

        // Act
        var sortedAssignments = assignments.OrderBy(a => a.AssignedAt).ToList();

        // Assert
        Assert.Equal("First", sortedAssignments[0].Reason);
        Assert.Equal("Second", sortedAssignments[1].Reason);
        Assert.Equal("Third", sortedAssignments[2].Reason);
    }

    #endregion

    #region Edge Cases and Complex Scenarios

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingTaskAssignmentWithVeryLongReason()
    {
        // Arrange
        var longReason = new string('A', 10000);

        // Act
        var assignment = new TaskAssignment(
            TaskId.Create(),
            AgentId.Create(),
            longReason,
            DateTime.UtcNow);

        // Assert
        Assert.Equal(10000, assignment.Reason.Length);
        Assert.Equal(longReason, assignment.Reason);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingTaskAssignmentWithSpecialCharactersInReason()
    {
        // Arrange
        var specialReason = "Reason with special chars: !@#$%^&*()[]{}|\\:;\"'<>,.?/~`±§";

        // Act
        var assignment = new TaskAssignment(
            TaskId.Create(),
            AgentId.Create(),
            specialReason,
            DateTime.UtcNow);

        // Assert
        Assert.Equal(specialReason, assignment.Reason);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingTaskAssignmentWithUnicodeInReason()
    {
        // Arrange
        var unicodeReason = "Reason with émojis 🚀🎉 and ütf-8 çharacters: 你好世界";

        // Act
        var assignment = new TaskAssignment(
            TaskId.Create(),
            AgentId.Create(),
            unicodeReason,
            DateTime.UtcNow);

        // Assert
        Assert.Equal(unicodeReason, assignment.Reason);
    }

    [Fact]
    public void ShouldMaintainIntegrity_WhenUsingTaskAssignmentUsingReassignmentScenario()
    {
        // Arrange
        var taskId = TaskId.Create();
        var originalAgent = AgentId.Create();
        var newAgent = AgentId.Create();
        var originalTime = DateTime.UtcNow;
        var reassignmentTime = originalTime.AddHours(2);

        // Act - Simulate reassignment
        var originalAssignment = new TaskAssignment(taskId, originalAgent, "Initial assignment", originalTime);
        var reassignment = new TaskAssignment(taskId, newAgent, "Reassigned due to workload", reassignmentTime);

        // Assert - Verify reassignment integrity
        Assert.Equal(taskId, originalAssignment.TaskId);
        Assert.Equal(taskId, reassignment.TaskId); // Same task
        Assert.NotEqual(originalAssignment.AssignedAgent, reassignment.AssignedAgent); // Different agents
        Assert.True(reassignment.AssignedAt > originalAssignment.AssignedAt); // Later assignment
        Assert.NotEqual(originalAssignment, reassignment); // Different records
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingTaskAssignmentToString()
    {
        // Arrange
        var assignment = new TaskAssignment(
            TaskId.Create(),
            AgentId.Create(),
            "Test assignment",
            DateTime.UtcNow);

        // Act
        var stringRepresentation = assignment.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        // For records, ToString() includes all property values
        Assert.Contains("TaskAssignment", stringRepresentation);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingTaskAssignmentWithMultipleAssignmentsForSameTask()
    {
        // Arrange - Multiple assignments for the same task (different times/agents)
        var taskId = TaskId.Create();
        var agent1 = AgentId.Create();
        var agent2 = AgentId.Create();
        var baseTime = DateTime.UtcNow;

        // Act
        var assignment1 = new TaskAssignment(taskId, agent1, "First assignment", baseTime);
        var assignment2 = new TaskAssignment(taskId, agent2, "Reassignment", baseTime.AddMinutes(30));

        // Assert
        Assert.Equal(taskId, assignment1.TaskId);
        Assert.Equal(taskId, assignment2.TaskId);
        Assert.NotEqual(assignment1.AssignedAgent, assignment2.AssignedAgent);
        Assert.NotEqual(assignment1, assignment2);
    }

    #endregion
}
