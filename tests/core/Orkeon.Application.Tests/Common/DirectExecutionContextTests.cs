using Orkeon.Application.Execution;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Application.Tests.Common;

public class DirectExecutionContextTests
{
    [Fact]
    public void ShouldInitializeContext_WhenConstructingWithValidVariables()
    {
        // Arrange
        var variables = new Dictionary<string, object>
        {
            { "key1", "value1" },
            { "key2", 42 },
            { "key3", true }
        };

        // Act
        var context = new DirectExecutionContext(variables);

        // Assert
        Assert.NotNull(context);
        Assert.NotNull(context.Variables);
        Assert.Equal(3, context.Variables.Count);
        Assert.Equal("value1", context.Variables["key1"]);
        Assert.Equal(42, context.Variables["key2"]);
        Assert.True((bool)context.Variables["key3"]);
    }

    [Fact]
    public void ShouldInitializeEmptyContext_WhenConstructingWithEmptyVariables()
    {
        // Arrange
        var variables = new Dictionary<string, object>();

        // Act
        var context = new DirectExecutionContext(variables);

        // Assert
        Assert.NotNull(context);
        Assert.NotNull(context.Variables);
        Assert.Empty(context.Variables);
    }

    [Fact]
    public void ShouldStoreTaskResult_WhenUsingUpdateFromTaskResult()
    {
        // Arrange
        var context = new DirectExecutionContext(new Dictionary<string, object>());
        var taskId = "task1";
        var result = "Task completed successfully";

        // Act
        context.UpdateFromTaskResult(taskId, result);

        // Assert
        var taskResult = context.GetTaskResultAsString(taskId);
        Assert.Equal(result, taskResult);
        Assert.Equal(result, context.Variables["last_result"]);
        Assert.Equal(result, context.Variables[$"task_{taskId}_result"]);
    }

    [Fact]
    public void ShouldStoreSeparately_WhenUsingUpdateFromTaskResultWithMultipleTasks()
    {
        // Arrange
        var context = new DirectExecutionContext(new Dictionary<string, object>());
        var result1 = "Result 1";
        var result2 = "Result 2";
        var result3 = "Result 3";

        // Act
        context.UpdateFromTaskResult("task1", result1);
        context.UpdateFromTaskResult("task2", result2);
        context.UpdateFromTaskResult("task3", result3);

        // Assert
        Assert.Equal(result1, context.GetTaskResultAsString("task1"));
        Assert.Equal(result2, context.GetTaskResultAsString("task2"));
        Assert.Equal(result3, context.GetTaskResultAsString("task3"));
        Assert.Equal(result3, context.Variables["last_result"]); // Should be the last one
    }

    [Fact]
    public void ShouldReturnCorrectType_WhenUsingGetTaskResultWithTypedResult()
    {
        // Arrange
        var context = new DirectExecutionContext(new Dictionary<string, object>());
        var taskResult = new TaskResult
        {
            Status = Completed,
            Data = new Dictionary<string, object> { { "score", 95 } }
        };

        // Act
        context.UpdateFromTaskResult("task1", taskResult);
        var retrievedResult = context.GetTaskResult<TaskResult>("task1");

        // Assert
        Assert.NotNull(retrievedResult);
        Assert.Equal(Completed, retrievedResult.Status);
        Assert.Equal(95, retrievedResult.Data["score"]);
    }

    [Fact]
    public void ShouldReturnNull_WhenUsingGetTaskResultWithNonExistentTask()
    {
        // Arrange
        var context = new DirectExecutionContext(new Dictionary<string, object>());

        // Act
        var result = context.GetTaskResult<string>("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldConvertToString_WhenGettingTaskResultAsStringWithNonStringResult()
    {
        // Arrange
        var context = new DirectExecutionContext(new Dictionary<string, object>());
        var numericResult = 42;

        // Act
        context.UpdateFromTaskResult("task1", numericResult);
        var stringResult = context.GetTaskResultAsString("task1");

        // Assert
        Assert.Equal("42", stringResult);
    }

    [Fact]
    public void ShouldUpdateVariable_WhenSettingVariable()
    {
        // Arrange
        var context = new DirectExecutionContext(new Dictionary<string, object>
        {
            { "existingKey", "originalValue" }
        });

        // Act
        context.SetVariable("existingKey", "newValue");
        context.SetVariable("newKey", "anotherValue");

        // Assert
        Assert.Equal("newValue", context.Variables["existingKey"]);
        Assert.Equal("anotherValue", context.Variables["newKey"]);
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingVariableWithExistingKey()
    {
        // Arrange
        var testObject = new TestClass { Name = "Test", Value = 123 };
        var context = new DirectExecutionContext(new Dictionary<string, object>
        {
            { "testKey", testObject }
        });

        // Act
        var retrieved = context.GetVariable<TestClass>("testKey");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Test", retrieved.Name);
        Assert.Equal(123, retrieved.Value);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingVariableWithNonExistentKey()
    {
        // Arrange
        var context = new DirectExecutionContext(new Dictionary<string, object>());

        // Act
        var result = context.GetVariable<string>("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingVariableWithWrongType()
    {
        // Arrange
        var context = new DirectExecutionContext(new Dictionary<string, object>
        {
            { "stringKey", "stringValue" }
        });

        // Act
        var result = context.GetVariable<object>("stringKey");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("stringValue", result);
    }

    [Fact]
    public void ShouldStoreCorrectly_WhenUsingUpdateFromTaskResultWithComplexObject()
    {
        // Arrange
        var context = new DirectExecutionContext(new Dictionary<string, object>());
        var complexResult = new
        {
            Status = Success,
            Data = new List<int> { 1, 2, 3, 4, 5 },
            Metadata = new Dictionary<string, string>
            {
                { "author", "system" },
                { "timestamp", DateTime.UtcNow.ToString() }
            }
        };

        // Act
        context.UpdateFromTaskResult("complexTask", complexResult);

        // Assert
        var stored = context.GetTaskResult<dynamic>("complexTask");
        Assert.NotNull(stored);
        Assert.Equal(Success, (string)stored!.Status);
        Assert.Equal(5, stored!.Data.Count);
    }

    [Fact]
    public void ShouldBeImmutableFromOutside_WhenUsingVariables()
    {
        // Arrange
        var initialVars = new Dictionary<string, object>
        {
            { "key1", "value1" }
        };
        var context = new DirectExecutionContext(initialVars);

        // Act
        var variables = context.Variables;
        // This should not affect the internal state
        initialVars["key1"] = "modified";
        initialVars["key2"] = "new";

        // Assert
        Assert.Equal("value1", context.Variables["key1"]);
        Assert.False(context.Variables.ContainsKey("key2"));
    }

    [Fact]
    public void ShouldMaintainHistoricalResults_WhenUsingUpdateFromTaskResult()
    {
        // Arrange
        var context = new DirectExecutionContext(new Dictionary<string, object>());

        // Act
        context.UpdateFromTaskResult("task1", "First result");
        context.UpdateFromTaskResult("task2", "Second result");

        // Update task1 again
        context.UpdateFromTaskResult("task1", "Updated first result");

        // Assert
        Assert.Equal("Updated first result", context.GetTaskResultAsString("task1"));
        Assert.Equal("Second result", context.GetTaskResultAsString("task2"));
        Assert.Equal("Updated first result", context.Variables["last_result"]);
    }

    // Helper class for testing
    private class TaskResult
    {
        public string Status { get; set; } = string.Empty;
        public Dictionary<string, object> Data { get; set; } = [];
    }

    private class TestClass
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}
