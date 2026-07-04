using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Domain.Tests.Agent;

public class TaskExecutionContextTests
{
    // Helper methods
    private static Orkeon.Domain.Agent.Agent CreateTestAgent(string role = RoleDeveloper, string goal = "Write quality code")
    {
        return Orkeon.Domain.Agent.Agent.Create(AgentRole.From(role), AgentGoal.From(goal));
    }

    private static Orkeon.Domain.Task.CrewTask CreateTestTask(string description = "Test task", string expectedOutput = "Test output")
    {
        return Orkeon.Domain.Task.CrewTask.Create(TaskDescription.From(description), ExpectedOutput.From(expectedOutput));
    }
    [Fact]
    public void ShouldInitializeContext_WhenConstructingWithValidParameters()
    {
        // Arrange
        var agent = Orkeon.Domain.Agent.Agent.Create(
            AgentRole.From(RoleDeveloper),
            AgentGoal.From("Write quality code"));
        var task = Orkeon.Domain.Task.CrewTask.Create(
            TaskDescription.From("Implement feature X"),
            ExpectedOutput.From("Working feature implementation"));

        // Act
        var context = TaskExecutionContext.For(agent, task);

        // Assert
        Assert.NotNull(context);
        Assert.Equal(agent, context.Agent);
        Assert.Equal(task, context.Task);
        Assert.NotNull(context.TypedVariables);
        Assert.NotNull(context.TypedMetadata);
        Assert.True(context.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullAgent()
    {
        // Arrange
        var task = Orkeon.Domain.Task.CrewTask.Create(
            TaskDescription.From("Test task"),
            ExpectedOutput.From("Expected output"));

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => TaskExecutionContext.For(null!, task));
        Assert.Equal("agent", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullTask()
    {
        // Arrange
        var agent = Orkeon.Domain.Agent.Agent.Create(
            AgentRole.From("Tester"),
            AgentGoal.From("Test software"));

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => TaskExecutionContext.For(agent, null!));
        Assert.Equal("task", exception.ParamName);
    }

    [Fact]
    public void ShouldStoreValue_WhenSettingVariableWithValidString()
    {
        // Arrange
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var context = TaskExecutionContext.For(agent, task);

        // Act
        context.SetVariable("projectName", "Orkeon");

        // Assert
        var retrieved = context.GetStringVariable("projectName");
        Assert.Equal("Orkeon", retrieved);
    }

    [Fact]
    public void ShouldStoreValue_WhenSettingVariableWithValidInt()
    {
        // Arrange
        var agent = CreateTestAgent("Researcher", "Conduct research");
        var task = CreateTestTask();
        var context = TaskExecutionContext.For(agent, task);

        // Act
        context.SetVariable("iterationCount", 5);

        // Assert
        var retrieved = context.GetIntVariable("iterationCount");
        Assert.Equal(5, retrieved);
    }

    [Fact]
    public void ShouldStoreValue_WhenSettingVariableWithComplexObject()
    {
        // Arrange
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var context = TaskExecutionContext.For(agent, task);
        var complexObject = new { Name = "Test", Value = 42, Items = new[] { "A", "B", "C" } };

        // Act
        context.SetVariable("config", complexObject);

        // Assert
        var retrieved = context.GetVariable<object>("config");
        Assert.NotNull(retrieved);
        dynamic dynamicObj = retrieved;
        Assert.Equal("Test", dynamicObj.Name);
        Assert.Equal(42, dynamicObj.Value);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenSettingVariableWithEmptyKey()
    {
        // Arrange
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var context = TaskExecutionContext.For(agent, task);

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => context.SetVariable("", "value"));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenSettingVariableWithNullValue()
    {
        // Arrange
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var context = TaskExecutionContext.For(agent, task);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => context.SetVariable("key", null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingVariableWithNonExistentKey()
    {
        // Arrange
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var context = TaskExecutionContext.For(agent, task);

        // Act
        var stringValue = context.GetStringVariable("nonexistent");
        var intValue = context.GetIntVariable("nonexistent");
        var objectValue = context.GetVariable<object>("nonexistent");

        // Assert
        Assert.Null(stringValue);
        Assert.Null(intValue);
        Assert.Null(objectValue);
    }

    [Fact]
    public void ShouldUpdateValue_WhenSettingVariableOverwriteExisting()
    {
        // Arrange
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var context = TaskExecutionContext.For(agent, task);

        // Act
        context.SetVariable("status", "pending");
        context.SetVariable("status", "completed");

        // Assert
        var status = context.GetStringVariable("status");
        Assert.Equal("completed", status);
    }

    [Fact]
    public void ShouldStoreMetadata_WhenUsingAddMetadataWithValidKeyValue()
    {
        // Arrange
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var context = TaskExecutionContext.For(agent, task);

        // Act
        context.AddMetadata("environment", "production");
        context.AddMetadata("version", "1.0.0");

        // Assert
        Assert.Contains("production", context.TypedMetadata.CustomProperties["environment"].ToString());
        Assert.Contains("1.0.0", context.TypedMetadata.CustomProperties["version"].ToString());
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingAddMetadataWithEmptyKey()
    {
        // Arrange
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var context = TaskExecutionContext.For(agent, task);

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => context.AddMetadata("", "value"));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingAddMetadataWithNullValue()
    {
        // Arrange
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var context = TaskExecutionContext.For(agent, task);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => context.AddMetadata("key", null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldMaintainTypeIntegrity_WhenUsingTypedVariables()
    {
        // Arrange
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var context = TaskExecutionContext.For(agent, task);

        // Act
        context.SetVariable("stringVar", "text");
        context.SetVariable("intVar", 42);
        context.SetVariable("boolVar", true);
        context.SetVariable("doubleVar", 3.14);

        // Assert
        Assert.Equal("text", context.GetStringVariable("stringVar"));
        Assert.Equal(42, context.GetIntVariable("intVar"));
        Assert.Null(context.GetStringVariable("intVar")); // Wrong type returns null
        Assert.Null(context.GetIntVariable("stringVar")); // Wrong type returns null
    }

    [Fact]
    public void ShouldHaveUniqueContexts_WhenUsingContextWithMultipleTasks()
    {
        // Arrange
        var agent = CreateTestAgent(RoleDeveloper, "Write quality code");
        var task1 = Orkeon.Domain.Task.CrewTask.Create(
            TaskDescription.From("Task 1"),
            ExpectedOutput.From("Output 1"));
        var task2 = Orkeon.Domain.Task.CrewTask.Create(
            TaskDescription.From("Task 2"),
            ExpectedOutput.From("Output 2"));

        // Act
        var context1 = TaskExecutionContext.For(agent, task1);
        var context2 = TaskExecutionContext.For(agent, task2);

        context1.SetVariable("taskSpecific", "value1");
        context2.SetVariable("taskSpecific", "value2");

        // Assert
        Assert.Equal("value1", context1.GetStringVariable("taskSpecific"));
        Assert.Equal("value2", context2.GetStringVariable("taskSpecific"));
    }

    [Fact]
    public void ShouldTrackExecutionId_WhenUsingTypedMetadata()
    {
        // Arrange
        var agent = CreateTestAgent();
        var task = CreateTestTask();

        // Act
        var context = TaskExecutionContext.For(agent, task);

        // Assert
        Assert.NotNull(context.TypedMetadata.ExecutionId);
        Assert.NotEmpty(context.TypedMetadata.ExecutionId);
        Assert.True(Guid.TryParse(context.TypedMetadata.ExecutionId, out _));
    }

    [Fact]
    public void ShouldStoreInCorrectCollections_WhenSettingVariableWithDifferentTypes()
    {
        // Arrange
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var context = TaskExecutionContext.For(agent, task);
        var customClass = new TestClass { Id = 1, Name = "Test" };

        // Act
        context.SetVariable("str", "string value");
        context.SetVariable("num", 123);
        context.SetVariable("flag", false);
        context.SetVariable("pi", 3.14159);
        context.SetVariable("obj", customClass);

        // Assert
        Assert.Equal("string value", context.GetStringVariable("str"));
        Assert.Equal(123, context.GetIntVariable("num"));
        Assert.Equal(3.14159, context.TypedVariables.GetDouble("pi"));
        Assert.False(context.TypedVariables.GetBool("flag"));

        var retrievedObj = context.GetVariable<TestClass>("obj");
        Assert.NotNull(retrievedObj);
        Assert.Equal(1, retrievedObj.Id);
        Assert.Equal("Test", retrievedObj.Name);
    }

    private class TestClass
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
}
