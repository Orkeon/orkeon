using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Application.Memory;
using Orkeon.Application.Execution;
using TaskOutput = Orkeon.Domain.Task.ValueObjects.TaskOutput;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Application.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Application.Tests.Services;

/// <summary>
/// Simple application layer tests following Clean Architecture.
/// </summary>
public class SimpleApplicationTests
{
    [Fact]
    public void ShouldBeCreatable_WhenUsingMemoryService()
    {
        // Arrange
        var providerFactory = new TestMemoryProviderFactory();
        var logger = new TestLogger<MemoryService>();

        // Act
        using var service = new MemoryService(providerFactory, logger);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void ShouldBeCreatable_WhenUsingCallbackOrchestrator()
    {
        // Arrange
        var logger = new TestLogger<CallbackOrchestrator>();

        // Act
        var orchestrator = new CallbackOrchestrator(logger);

        // Assert
        Assert.NotNull(orchestrator);
    }

    [Fact]
    public void ShouldStoreVariables_WhenUsingDictionary()
    {
        // Arrange
        var variables = new Dictionary<string, object>
        {
            // Act
            ["key1"] = "value1",
            ["key2"] = 42
        };

        // Assert
        Assert.Equal("value1", variables["key1"]);
        Assert.Equal(42, variables["key2"]);
    }

    [Fact]
    public void ShouldGenerateUniqueIds_WhenUsingAgentId()
    {
        // Arrange & Act
        var id1 = Orkeon.Domain.Common.AgentId.Create();
        var id2 = Orkeon.Domain.Common.AgentId.Create();

        // Assert
        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id1.Value, id2.Value);
    }

    [Fact]
    public void ShouldGenerateUniqueIds_WhenUsingTaskId()
    {
        // Arrange & Act
        var id1 = Orkeon.Domain.Common.TaskId.Create();
        var id2 = Orkeon.Domain.Common.TaskId.Create();

        // Assert
        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id1.Value, id2.Value);
    }

    [Fact]
    public void ShouldGenerateUniqueIds_WhenUsingCrewId()
    {
        // Arrange & Act
        var id1 = Orkeon.Domain.Common.CrewId.Create();
        var id2 = Orkeon.Domain.Common.CrewId.Create();

        // Assert
        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id1.Value, id2.Value);
    }

    [Fact]
    public void ShouldHaveCorrectProperties_WhenUsingTaskOutputUsingSuccess()
    {
        // Arrange & Act
        var output = TaskOutput.Create("Task completed successfully", success: true);

        // Assert
        Assert.True(output.Success);
        Assert.Equal("Task completed successfully", output.RawOutput);
        Assert.Null(output.FormattedOutput);
    }

    [Fact]
    public void ShouldHaveCorrectProperties_WhenUsingTaskOutputUsingFailure()
    {
        // Arrange & Act
        var output = TaskOutput.Create("Task failed with error", success: false);

        // Assert
        Assert.False(output.Success);
        Assert.Equal("Task failed with error", output.RawOutput);
        Assert.Null(output.FormattedOutput);
    }

    [Fact]
    public void ShouldValidateNonEmptyValue_WhenUsingAgentRole()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => AgentRole.From(""));
        Assert.Throws<ArgumentException>(() => AgentRole.From("   "));

        var validRole = AgentRole.From(RoleDeveloper);
        Assert.Equal(RoleDeveloper, validRole.Value);
    }

    [Fact]
    public void ShouldValidateNonEmptyValue_WhenUsingAgentGoal()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => AgentGoal.From(""));
        Assert.Throws<ArgumentException>(() => AgentGoal.From("   "));

        var validGoal = AgentGoal.From("Build quality software");
        Assert.Equal("Build quality software", validGoal.Value);
    }

    [Fact]
    public void ShouldValidateNonEmptyValue_WhenUsingTaskDescription()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => TaskDescription.From(""));
        Assert.Throws<ArgumentException>(() => TaskDescription.From("   "));

        var validDescription = TaskDescription.From("Implement feature X");
        Assert.Equal("Implement feature X", validDescription.Value);
    }

    [Fact]
    public void ShouldHaveCorrectValues_WhenUsingProcessType()
    {
        // Arrange & Act & Assert
        Assert.Equal("Sequential", ProcessType.Sequential.Value);
        Assert.Equal("Hierarchical", ProcessType.Hierarchical.Value);
        Assert.Equal("Consensual", ProcessType.Consensual.Value);
        Assert.Equal("Parallel", ProcessType.Parallel.Value);
    }

    [Fact]
    public void ShouldHaveCorrectValues_WhenUsingTaskPriority()
    {
        // Arrange & Act & Assert
        Assert.Equal(1, TaskPriority.Low.Order);
        Assert.Equal(2, TaskPriority.Normal.Order);
        Assert.Equal(3, TaskPriority.High.Order);
        Assert.Equal(4, TaskPriority.Critical.Order);
        Assert.Equal(5, TaskPriority.Urgent.Order);
    }

    [Fact]
    public void ShouldHaveCorrectValues_WhenUsingTaskStatus()
    {
        // Arrange & Act & Assert
        Assert.Equal(Pending, Orkeon.Domain.Task.ValueObjects.TaskStatus.Pending.Value);
        Assert.Equal(InProgress, Orkeon.Domain.Task.ValueObjects.TaskStatus.InProgress.Value);
        Assert.Equal(Completed, Orkeon.Domain.Task.ValueObjects.TaskStatus.Completed.Value);
        Assert.Equal(Failed, Orkeon.Domain.Task.ValueObjects.TaskStatus.Failed.Value);
        Assert.Equal("Cancelled", Orkeon.Domain.Task.ValueObjects.TaskStatus.Cancelled.Value);
        Assert.Equal("Blocked", Orkeon.Domain.Task.ValueObjects.TaskStatus.Blocked.Value);
    }
}
