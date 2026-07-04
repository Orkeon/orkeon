using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
namespace Orkeon.Domain.Tests.Callbacks;

/// <summary>
/// Tests for StepProgressContext following Clean Architecture principles.
/// Tests the step progress context record for task execution notifications.
/// </summary>
public class StepProgressContextTests
{
    [Fact]
    public void ShouldCreateContext_WhenConstructingWithValidParameters()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var stepDescription = "Analyzing market data";
        var currentStep = 3;
        var totalSteps = 10;
        var timestamp = DateTime.UtcNow;

        // Act
        var context = new StepProgressContext(
            taskId, agentId, stepDescription, currentStep, totalSteps, timestamp);

        // Assert
        Assert.Equal(taskId, context.TaskId);
        Assert.Equal(agentId, context.AgentId);
        Assert.Equal(stepDescription, context.StepDescription);
        Assert.Equal(currentStep, context.CurrentStep);
        Assert.Equal(totalSteps, context.TotalSteps);
        Assert.Equal(timestamp, context.Timestamp);
    }

    [Fact]
    public void ShouldAcceptAllValues_WhenConstructingWithAllStringParameters()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var stepDescription = "Complex step with special chars: []{}|\\:;\"'<>,.?/~`";
        var currentStep = 1;
        var totalSteps = 1;
        var timestamp = DateTime.MinValue;

        // Act
        var context = new StepProgressContext(
            taskId, agentId, stepDescription, currentStep, totalSteps, timestamp);

        // Assert
        Assert.Equal(taskId, context.TaskId);
        Assert.Equal(agentId, context.AgentId);
        Assert.Equal(stepDescription, context.StepDescription);
        Assert.Equal(currentStep, context.CurrentStep);
        Assert.Equal(totalSteps, context.TotalSteps);
        Assert.Equal(timestamp, context.Timestamp);
    }

    [Theory]
    [InlineData("")]
    [InlineData(TaskId1)]
    [InlineData("TASK_UPPERCASE")]
    [InlineData("task-with-dashes")]
    [InlineData("task.with.dots")]
    [InlineData("task_with_underscores")]
    [InlineData("very-long-task-identifier-with-many-characters-and-details")]
    public void ShouldAcceptAll_WhenConstructingWithVariousTaskIds(string taskId)
    {
        // Act
        var context = new StepProgressContext(
            taskId, AgentId1, "Step description", 1, 5, DateTime.UtcNow);

        // Assert
        Assert.Equal(taskId, context.TaskId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(AgentId1)]
    [InlineData("AGENT_UPPERCASE")]
    [InlineData("agent-with-dashes")]
    [InlineData("agent.with.dots")]
    [InlineData("agent_with_underscores")]
    [InlineData("123-numeric-agent")]
    public void ShouldAcceptAll_WhenConstructingWithVariousAgentIds(string agentId)
    {
        // Act
        var context = new StepProgressContext(
            TaskId1, agentId, "Step description", 1, 5, DateTime.UtcNow);

        // Assert
        Assert.Equal(agentId, context.AgentId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Simple step")]
    [InlineData("Complex step with detailed description and multiple clauses")]
    [InlineData("Step with numbers: 123, 456, 789")]
    [InlineData("Step with special chars: !@#$%^&*()[]{}|\\:;\"'<>,.?/~`")]
    [InlineData("Step with\nnewlines\nand\ttabs")]
    public void ShouldAcceptAll_WhenConstructingWithVariousStepDescriptions(string stepDescription)
    {
        // Act
        var context = new StepProgressContext(
            TaskId1, AgentId1, stepDescription, 1, 5, DateTime.UtcNow);

        // Assert
        Assert.Equal(stepDescription, context.StepDescription);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(1, 10)]
    [InlineData(5, 10)]
    [InlineData(10, 10)]
    [InlineData(100, 100)]
    [InlineData(-1, 5)] // Negative current step
    [InlineData(5, -1)] // Negative total steps
    [InlineData(int.MinValue, int.MaxValue)]
    [InlineData(int.MaxValue, int.MinValue)]
    public void ShouldAcceptAll_WhenConstructingWithVariousStepCounts(int currentStep, int totalSteps)
    {
        // Act
        var context = new StepProgressContext(
            TaskId1, AgentId1, "Step", currentStep, totalSteps, DateTime.UtcNow);

        // Assert
        Assert.Equal(currentStep, context.CurrentStep);
        Assert.Equal(totalSteps, context.TotalSteps);
    }

    [Fact]
    public void ShouldAcceptAll_WhenConstructingWithExtremeTimestamps()
    {
        // Arrange
        var timestamps = new[]
        {
            DateTime.MinValue,
            DateTime.MaxValue,
            DateTime.UnixEpoch,
            new DateTime(2000, 1, 1),
            new DateTime(2025, 12, 31, 23, 59, 59),
            DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Local),
            DateTime.UtcNow
        };

        foreach (var timestamp in timestamps)
        {
            // Act
            var context = new StepProgressContext(
                TaskId1, AgentId1, "Step", 1, 5, timestamp);

            // Assert
            Assert.Equal(timestamp, context.Timestamp);
        }
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var stepDescription = "Test step";
        var currentStep = 3;
        var totalSteps = 10;
        var timestamp = DateTime.UtcNow;

        var context1 = new StepProgressContext(taskId, agentId, stepDescription, currentStep, totalSteps, timestamp);
        var context2 = new StepProgressContext(taskId, agentId, stepDescription, currentStep, totalSteps, timestamp);

        // Act & Assert
        Assert.True(context1.Equals(context2));
        Assert.True(context2.Equals(context1));
        Assert.True(context1 == context2);
        Assert.False(context1 != context2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentTaskId()
    {
        // Arrange
        var agentId = AgentId.Create();
        var stepDescription = "Test step";
        var currentStep = 3;
        var totalSteps = 10;
        var timestamp = DateTime.UtcNow;

        var context1 = new StepProgressContext("task-123", agentId, stepDescription, currentStep, totalSteps, timestamp);
        var context2 = new StepProgressContext("task-456", agentId, stepDescription, currentStep, totalSteps, timestamp);

        // Act & Assert
        Assert.False(context1.Equals(context2));
        Assert.False(context2.Equals(context1));
        Assert.False(context1 == context2);
        Assert.True(context1 != context2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentAgentId()
    {
        // Arrange
        var taskId = TaskId.Create();
        var stepDescription = "Test step";
        var currentStep = 3;
        var totalSteps = 10;
        var timestamp = DateTime.UtcNow;

        var context1 = new StepProgressContext(taskId, "agent-123", stepDescription, currentStep, totalSteps, timestamp);
        var context2 = new StepProgressContext(taskId, "agent-456", stepDescription, currentStep, totalSteps, timestamp);

        // Act & Assert
        Assert.False(context1.Equals(context2));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentStepDescription()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var currentStep = 3;
        var totalSteps = 10;
        var timestamp = DateTime.UtcNow;

        var context1 = new StepProgressContext(taskId, agentId, "Step 1", currentStep, totalSteps, timestamp);
        var context2 = new StepProgressContext(taskId, agentId, "Step 2", currentStep, totalSteps, timestamp);

        // Act & Assert
        Assert.False(context1.Equals(context2));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentCurrentStep()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var stepDescription = "Test step";
        var totalSteps = 10;
        var timestamp = DateTime.UtcNow;

        var context1 = new StepProgressContext(taskId, agentId, stepDescription, 3, totalSteps, timestamp);
        var context2 = new StepProgressContext(taskId, agentId, stepDescription, 4, totalSteps, timestamp);

        // Act & Assert
        Assert.False(context1.Equals(context2));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentTotalSteps()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var stepDescription = "Test step";
        var currentStep = 3;
        var timestamp = DateTime.UtcNow;

        var context1 = new StepProgressContext(taskId, agentId, stepDescription, currentStep, 10, timestamp);
        var context2 = new StepProgressContext(taskId, agentId, stepDescription, currentStep, 5, timestamp);

        // Act & Assert
        Assert.False(context1.Equals(context2));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentTimestamp()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var stepDescription = "Test step";
        var currentStep = 3;
        var totalSteps = 10;

        var context1 = new StepProgressContext(taskId, agentId, stepDescription, currentStep, totalSteps, DateTime.UtcNow);
        var context2 = new StepProgressContext(taskId, agentId, stepDescription, currentStep, totalSteps, DateTime.UtcNow.AddSeconds(1));

        // Act & Assert
        Assert.False(context1.Equals(context2));
    }

    [Fact]
    public void ShouldReturnSameHashCode_WhenCallingGetHashCodeWithSameValues()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var stepDescription = "Test step";
        var currentStep = 3;
        var totalSteps = 10;
        var timestamp = DateTime.UtcNow;

        var context1 = new StepProgressContext(taskId, agentId, stepDescription, currentStep, totalSteps, timestamp);
        var context2 = new StepProgressContext(taskId, agentId, stepDescription, currentStep, totalSteps, timestamp);

        // Act
        var hash1 = context1.GetHashCode();
        var hash2 = context2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnDifferentHashCodes_WhenCallingGetHashCodeWithDifferentValues()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;

        var context1 = new StepProgressContext("task-123", "agent-456", "Step 1", 3, 10, timestamp);
        var context2 = new StepProgressContext("task-123", "agent-456", "Step 2", 3, 10, timestamp);

        // Act
        var hash1 = context1.GetHashCode();
        var hash2 = context2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ShouldIncludeAllProperties_WhenCallingToString()
    {
        // Arrange
        var context = new StepProgressContext(
            "task-123", "agent-456", "Analyzing data", 3, 10, DateTime.UtcNow);

        // Act
        var stringRepresentation = context.ToString();

        // Assert
        Assert.Contains("task-123", stringRepresentation);
        Assert.Contains("agent-456", stringRepresentation);
        Assert.Contains("Analyzing data", stringRepresentation);
        Assert.Contains("3", stringRepresentation);
        Assert.Contains("10", stringRepresentation);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingStepProgressContextInCollection()
    {
        // Arrange
        var contexts = new List<StepProgressContext>
        {
            new(TaskId1, AgentId1, "Step 1", 1, 5, DateTime.UtcNow),
            new(TaskId1, AgentId1, "Step 2", 2, 5, DateTime.UtcNow.AddMinutes(1)),
            new(TaskId1, AgentId1, "Step 3", 3, 5, DateTime.UtcNow.AddMinutes(2)),
            new(TaskId2, AgentId2, "Step 1", 1, 3, DateTime.UtcNow.AddMinutes(3))
        };

        // Act
        var task1Contexts = contexts.Where(c => c.TaskId == TaskId1).ToList();
        var agent1Contexts = contexts.Where(c => c.AgentId == AgentId1).ToList();
        var step1Contexts = contexts.Where(c => c.CurrentStep == 1).ToList();

        // Assert
        Assert.Equal(4, contexts.Count);
        Assert.Equal(3, task1Contexts.Count);
        Assert.Equal(3, agent1Contexts.Count);
        Assert.Equal(2, step1Contexts.Count);
    }

    [Fact]
    public void ShouldPreventDuplicates_WhenUsingStepProgressContextInHashSet()
    {
        // Arrange
        var hashSet = new HashSet<StepProgressContext>();
        var timestamp = DateTime.UtcNow;

        var context1 = new StepProgressContext(TaskId1, AgentId1, "Step", 1, 5, timestamp);
        var context2 = new StepProgressContext(TaskId1, AgentId1, "Step", 1, 5, timestamp); // Duplicate
        var context3 = new StepProgressContext(TaskId1, AgentId1, "Step", 2, 5, timestamp); // Different current step

        // Act
        hashSet.Add(context1);
        hashSet.Add(context2); // Should not be added due to equality
        hashSet.Add(context3);

        // Assert
        Assert.Equal(2, hashSet.Count);
        Assert.Contains(context1, hashSet);
        Assert.Contains(context3, hashSet);
    }

    [Fact]
    public void ShouldWorkAsKey_WhenUsingStepProgressContextInDictionary()
    {
        // Arrange
        var dictionary = new Dictionary<StepProgressContext, string>();
        var timestamp = DateTime.UtcNow;

        var key1 = new StepProgressContext(TaskId1, AgentId1, "Step 1", 1, 5, timestamp);
        var key2 = new StepProgressContext(TaskId1, AgentId1, "Step 1", 1, 5, timestamp); // Same values
        var key3 = new StepProgressContext(TaskId1, AgentId1, "Step 2", 2, 5, timestamp); // Different step

        // Act
        dictionary[key1] = "Value 1";
        dictionary[key3] = "Value 3";

        // Assert
        Assert.Equal(2, dictionary.Count);
        Assert.Equal("Value 1", dictionary[key2]); // Should find value using equivalent key
        Assert.True(dictionary.ContainsKey(key2));
    }

    [Fact]
    public void ShouldProvideUsefulMetrics_WhenUsingStepProgressContextWithProgressCalculation()
    {
        // Arrange
        var contexts = new[]
        {
            new StepProgressContext(TaskId1, AgentId1, "Step 1", 1, 10, DateTime.UtcNow),
            new StepProgressContext(TaskId1, AgentId1, "Step 5", 5, 10, DateTime.UtcNow),
            new StepProgressContext(TaskId1, AgentId1, "Step 10", 10, 10, DateTime.UtcNow)
        };

        // Act - Calculate progress percentages
        var progressPercentages = contexts.Select(c =>
            c.TotalSteps > 0 ? (double)c.CurrentStep / c.TotalSteps * 100 : 0).ToList();

        // Assert
        Assert.Equal(10.0, progressPercentages[0], 1);
        Assert.Equal(50.0, progressPercentages[1], 1);
        Assert.Equal(100.0, progressPercentages[2], 1);
    }

    [Fact]
    public void ShouldNotCauseError_WhenUsingStepProgressContextWithZeroTotalSteps()
    {
        // Act
        var context = new StepProgressContext(TaskId1, AgentId1, "Step", 1, 0, DateTime.UtcNow);

        // Assert
        Assert.Equal(1, context.CurrentStep);
        Assert.Equal(0, context.TotalSteps);

        // Progress calculation should handle division by zero
        var progressPercentage = context.TotalSteps > 0 ? (double)context.CurrentStep / context.TotalSteps * 100 : 0;
        Assert.Equal(0, progressPercentage);
    }

    [Fact]
    public void ShouldAllowInvalidState_WhenUsingStepProgressContextWithCurrentStepGreaterThanTotal()
    {
        // Act
        var context = new StepProgressContext(TaskId1, AgentId1, "Overflow step", 15, 10, DateTime.UtcNow);

        // Assert
        Assert.Equal(15, context.CurrentStep);
        Assert.Equal(10, context.TotalSteps);

        // Progress calculation might exceed 100%
        var progressPercentage = context.TotalSteps > 0 ? (double)context.CurrentStep / context.TotalSteps * 100 : 0;
        Assert.Equal(150.0, progressPercentage, 1);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingStepProgressContextWithNullStrings()
    {
        // Act & Assert - Record constructors should handle null appropriately
        var exception = Record.Exception(() =>
            new StepProgressContext(null!, null!, null!, 1, 5, DateTime.UtcNow));

        Assert.Null(exception); // Should not throw, but properties will be null
    }

    [Fact]
    public void ShouldNotAllowPropertyChanges_WhenUsingStepProgressContextUsingImmutability()
    {
        // Arrange
        var context = new StepProgressContext(TaskId1, AgentId1, "Step", 1, 5, DateTime.UtcNow);

        // Act & Assert - Properties should be read-only
        // This is enforced by the compiler for records, so we just verify the properties exist and are accessible
        Assert.Equal("Step", context.StepDescription);
        Assert.Equal(1, context.CurrentStep);
        Assert.Equal(5, context.TotalSteps);
        Assert.NotEqual(default(DateTime), context.Timestamp);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingStepProgressContextWithVeryLongStrings()
    {
        // Arrange
        var longTaskId = new string('T', 1000);
        var longAgentId = new string('A', 1000);
        var longDescription = new string('D', 5000);

        // Act
        var context = new StepProgressContext(longTaskId, longAgentId, longDescription, 1, 5, DateTime.UtcNow);

        // Assert
        Assert.Equal(1000, context.TaskId.Length);
        Assert.Equal(1000, context.AgentId.Length);
        Assert.Equal(5000, context.StepDescription.Length);
        Assert.Equal(longTaskId, context.TaskId);
        Assert.Equal(longAgentId, context.AgentId);
        Assert.Equal(longDescription, context.StepDescription);
    }
}
