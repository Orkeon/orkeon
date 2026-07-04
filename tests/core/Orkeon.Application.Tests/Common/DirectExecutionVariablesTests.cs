using Orkeon.Application.Execution;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Application.Tests.Common;

public class DirectExecutionVariablesTests
{
    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var variables = DirectExecutionVariables.Empty;

        // Assert
        Assert.NotNull(variables);
        Assert.Empty(variables.Keys);
        Assert.Empty(variables.ToDictionary());
    }

    [Fact]
    public void ShouldCreateNewInstanceWithAddedVariable_WhenSetting()
    {
        // Arrange
        var variables = DirectExecutionVariables.Empty;

        // Act
        var newVariables = variables.Set("key1", "value1");

        // Assert
        Assert.NotSame(variables, newVariables);
        Assert.Empty(variables.Keys);
        Assert.Single(newVariables.Keys);
        Assert.True(newVariables.ContainsKey("key1"));
        Assert.Equal("value1", newVariables.GetString("key1"));
    }

    [Fact]
    public void ShouldMaintainImmutability_WhenSettingWithMultipleCalls()
    {
        // Arrange
        var original = DirectExecutionVariables.Empty;

        // Act
        var first = original.Set("key1", "value1");
        var second = first.Set("key2", "value2");
        var third = second.Set("key1", "updatedValue1");

        // Assert
        Assert.Empty(original.Keys);
        Assert.Single(first.Keys);
        Assert.Equal(2, second.Keys.Count());
        Assert.Equal(2, third.Keys.Count());

        Assert.Equal("value1", first.GetString("key1"));
        Assert.Equal("updatedValue1", third.GetString("key1"));
        Assert.Equal("value2", third.GetString("key2"));
    }

    [Fact]
    public void ShouldAddAllVariables_WhenSettingMultiple()
    {
        // Arrange
        var variables = DirectExecutionVariables.Empty;
        var items = new[]
        {
            new KeyValuePair<string, object>("key1", "value1"),
            new KeyValuePair<string, object>("key2", 42),
            new KeyValuePair<string, object>("key3", true)
        };

        // Act
        var result = variables.SetMultiple(items);

        // Assert
        Assert.Equal(3, result.Keys.Count());
        Assert.Equal("value1", result.GetString("key1"));
        Assert.Equal("42", result.GetString("key2"));
        Assert.Equal("True", result.GetString("key3"));
    }

    [Fact]
    public void ShouldReturnCorrectValue_WhenGettingWithGenericType()
    {
        // Arrange
        var testObject = new TestClass { Name = "Test", Value = 123 };
        var variables = DirectExecutionVariables.Empty
            .Set("string", "hello")
            .Set("int", 42)
            .Set("object", testObject);

        // Act
        var stringValue = variables.Get<string>("string");
        var objectValue = variables.Get<TestClass>("object");

        // Assert
        Assert.Equal("hello", stringValue);
        Assert.NotNull(objectValue);
        Assert.Equal("Test", objectValue.Name);
        Assert.Equal(123, objectValue.Value);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingWithNonExistentKey()
    {
        // Arrange
        var variables = DirectExecutionVariables.Empty;

        // Act
        var result = variables.Get<string>("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldConvertToString_WhenGettingStringWithVariousTypes()
    {
        // Arrange
        var variables = DirectExecutionVariables.Empty
            .Set("string", "hello")
            .Set("int", 42)
            .Set("bool", true)
            .Set("double", 3.14);

        // Act & Assert
        Assert.Equal("hello", variables.GetString("string"));
        Assert.Equal("42", variables.GetString("int"));
        Assert.Equal("True", variables.GetString("bool"));
        Assert.Equal("3.14", variables.GetString("double"));
    }

    [Fact]
    public void ShouldReturnEmptyString_WhenGettingStringWithNonExistentKey()
    {
        // Arrange
        var variables = DirectExecutionVariables.Empty;

        // Act
        var result = variables.GetString("nonexistent");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingContainsKey()
    {
        // Arrange
        var variables = DirectExecutionVariables.Empty
            .Set("existing", "value");

        // Act & Assert
        Assert.True(variables.ContainsKey("existing"));
        Assert.False(variables.ContainsKey("nonexistent"));
    }

    [Fact]
    public void ShouldReturnAllVariables_WhenUsingToDictionary()
    {
        // Arrange
        var variables = DirectExecutionVariables.Empty
            .Set("key1", "value1")
            .Set("key2", 42)
            .Set("key3", new TestClass { Name = "Test" });

        // Act
        var dictionary = variables.ToDictionary();

        // Assert
        Assert.Equal(3, dictionary.Count);
        Assert.Equal("value1", dictionary["key1"]);
        Assert.Equal(42, dictionary["key2"]);
        Assert.IsType<TestClass>(dictionary["key3"]);
    }

    [Fact]
    public void ShouldCreateEmptyBuilder_WhenUsingCreateBuilder()
    {
        // Act
        var builder = DirectExecutionVariables.CreateBuilder();

        // Assert
        Assert.NotNull(builder);
        var variables = builder.Build();
        Assert.Empty(variables.Keys);
    }

    [Fact]
    public void ShouldAddVariables_WhenUsingBuilderAddMethods()
    {
        // Arrange
        var builder = DirectExecutionVariables.CreateBuilder();

        // Act
        var variables = builder
            .AddTaskId("task-123")
            .AddAgentId("agent-456")
            .AddInput("test input")
            .AddContext(new Dictionary<string, string> { { "env", "test" } })
            .Add("custom", "value")
            .Build();

        // Assert
        Assert.Equal(5, variables.Keys.Count());
        Assert.Equal("task-123", variables.GetString("task_id"));
        Assert.Equal("agent-456", variables.GetString("agent_id"));
        Assert.Equal("test input", variables.GetString("input"));
        Assert.NotNull(variables.Get<Dictionary<string, string>>("context"));
        Assert.Equal("value", variables.GetString("custom"));
    }

    [Fact]
    public void ShouldCopyExistingVariables_WhenCreatingBuilderFrom()
    {
        // Arrange
        var original = DirectExecutionVariables.Empty
            .Set("key1", "value1")
            .Set("key2", "value2");

        // Act
        var builder = DirectExecutionVariables.CreateBuilderFrom(original);
        var variables = builder
            .Add("key3", "value3")
            .Build();

        // Assert
        Assert.Equal(3, variables.Keys.Count());
        Assert.Equal("value1", variables.GetString("key1"));
        Assert.Equal("value2", variables.GetString("key2"));
        Assert.Equal("value3", variables.GetString("key3"));
    }

    [Fact]
    public void ShouldCreateVariables_WhenUsingFromDictionaryWithValidDictionary()
    {
        // Arrange
        var dictionary = new Dictionary<string, object>
        {
            { "key1", "value1" },
            { "key2", 42 },
            { "key3", new TestClass { Name = "Test" } }
        };

        // Act
        var variables = DirectExecutionVariables.FromDictionary(dictionary);

        // Assert
        Assert.Equal(3, variables.Keys.Count());
        Assert.Equal("value1", variables.GetString("key1"));
        Assert.Equal("42", variables.GetString("key2"));
        Assert.NotNull(variables.Get<TestClass>("key3"));
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithNullDictionary()
    {
        // Act
        var variables = DirectExecutionVariables.FromDictionary(null);

        // Assert
        Assert.Same(DirectExecutionVariables.Empty, variables);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithEmptyDictionary()
    {
        // Act
        var variables = DirectExecutionVariables.FromDictionary(new Dictionary<string, object>());

        // Assert
        Assert.Same(DirectExecutionVariables.Empty, variables);
    }

    [Fact]
    public void ShouldAttemptConversion_WhenGettingWithTypeConversion()
    {
        // Arrange
        var variables = DirectExecutionVariables.Empty
            .Set("stringNumber", "42")
            .Set("intValue", 123);

        // Act
        var stringValue = variables.Get<string>("intValue");

        // Assert
        Assert.Equal("123", stringValue);
    }

    [Fact]
    public void ShouldMaintainFluency_WhenUsingBuilderChainedOperations()
    {
        // Act
        var variables = DirectExecutionVariables
            .CreateBuilder()
            .AddTaskId(TaskId1)
            .AddAgentId(AgentId1)
            .Add("step", 1)
            .Add("status", "running")
            .Build();

        var extended = DirectExecutionVariables
            .CreateBuilderFrom(variables)
            .Add("result", "success")
            .Add("step", 2)
            .Build();

        // Assert
        Assert.Equal("1", variables.GetString("step"));
        Assert.Equal("2", extended.GetString("step"));
        Assert.False(variables.ContainsKey("result"));
        Assert.True(extended.ContainsKey("result"));
    }

    [Fact]
    public void ShouldReturnAllKeys_WhenUsingKeys()
    {
        // Arrange
        var variables = DirectExecutionVariables.Empty
            .Set("key1", "value1")
            .Set("key2", "value2")
            .Set("key3", "value3");

        // Act
        var keys = variables.Keys.ToList();

        // Assert
        Assert.Equal(3, keys.Count);
        Assert.Contains("key1", keys);
        Assert.Contains("key2", keys);
        Assert.Contains("key3", keys);
    }

    // Helper class for testing
    private class TestClass
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}
