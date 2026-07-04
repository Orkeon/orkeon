using Orkeon.Domain.Common;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Tests.Common;

/// <summary>
/// Tests for ProcessOutputs following Clean Architecture principles.
/// Tests the business rules and validation logic of the ProcessOutputs class.
/// </summary>
public class ProcessOutputsTests
{
    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var outputs = ProcessOutputs.Empty;

        // Assert
        Assert.NotNull(outputs);
        Assert.Equal(0, outputs.Count);
        Assert.Empty(outputs.Keys);
    }

    [Fact]
    public void ShouldReturnSameInstance_WhenUsingEmpty()
    {
        // Act
        var outputs1 = ProcessOutputs.Empty;
        var outputs2 = ProcessOutputs.Empty;

        // Assert
        Assert.Same(outputs1, outputs2);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithAddedOutput_WhenUsingWith()
    {
        // Arrange
        var outputs = ProcessOutputs.Empty;

        // Act
        var newOutputs = outputs.With("key1", "value1");

        // Assert
        Assert.NotSame(outputs, newOutputs);
        Assert.Equal(0, outputs.Count);
        Assert.Equal(1, newOutputs.Count);
        Assert.True(newOutputs.Contains("key1"));
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingWithExistingKey()
    {
        // Arrange
        var outputs = ProcessOutputs.Empty
            .With("stringKey", "stringValue")
            .With("intKey", 42);

        // Act
        var stringValue = outputs.Get<string>("stringKey");
        // Cannot use Get<int> directly due to generic constraint
        // Would need to access the value differently

        // Assert
        Assert.Equal("stringValue", stringValue);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingWithNonExistingKey()
    {
        // Arrange
        var outputs = ProcessOutputs.Empty;

        // Act
        var value = outputs.Get<string>("nonExistingKey");

        // Assert
        Assert.Null(value);
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingRequiredWithExistingKey()
    {
        // Arrange
        var outputs = ProcessOutputs.Empty.With("key", "value");

        // Act
        var value = outputs.GetRequired<string>("key");

        // Assert
        Assert.Equal("value", value);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenGettingRequiredWithNonExistingKey()
    {
        // Arrange
        var outputs = ProcessOutputs.Empty;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => outputs.GetRequired<string>("nonExistingKey"));
        Assert.Contains("Required output 'nonExistingKey' not found", exception.Message);
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingContains()
    {
        // Arrange
        var outputs = ProcessOutputs.Empty.With("existingKey", "value");

        // Act & Assert
        Assert.True(outputs.Contains("existingKey"));
        Assert.False(outputs.Contains("nonExistingKey"));
    }

    [Fact]
    public void ShouldReturnAllKeys_WhenUsingKeys()
    {
        // Arrange
        var outputs = ProcessOutputs.Empty
            .With("key1", "value1")
            .With("key2", 2)
            .With("key3", true);

        // Act
        var keys = outputs.Keys.ToList();

        // Assert
        Assert.Equal(3, keys.Count);
        Assert.Contains("key1", keys);
        Assert.Contains("key2", keys);
        Assert.Contains("key3", keys);
    }

    [Fact]
    public void ShouldAddStringValue_WhenUsingBuilderAddString()
    {
        // Arrange
        var builder = ProcessOutputs.CreateBuilder();

        // Act
        var outputs = builder
            .AddString("name", "John")
            .AddString("city", "London")
            .Build();

        // Assert
        Assert.Equal(2, outputs.Count);
        Assert.Equal("John", outputs.Get<string>("name"));
        Assert.Equal("London", outputs.Get<string>("city"));
    }

    [Fact]
    public void ShouldAddIntValue_WhenUsingBuilderAddInt()
    {
        // Arrange & Act
        var outputs = ProcessOutputs.CreateBuilder()
            .AddInt("age", 30)
            .AddInt("score", 100)
            .Build();

        // Assert
        Assert.Equal(2, outputs.Count);
        Assert.True(outputs.Contains("age"));
        Assert.True(outputs.Contains("score"));
    }

    [Fact]
    public void ShouldAddDoubleValue_WhenUsingBuilderAddDouble()
    {
        // Arrange & Act
        var outputs = ProcessOutputs.CreateBuilder()
            .AddDouble("temperature", 36.6)
            .AddDouble("percentage", 99.9)
            .Build();

        // Assert
        Assert.Equal(2, outputs.Count);
        Assert.True(outputs.Contains("temperature"));
        Assert.True(outputs.Contains("percentage"));
    }

    [Fact]
    public void ShouldAddBoolValue_WhenUsingBuilderAddBool()
    {
        // Arrange & Act
        var outputs = ProcessOutputs.CreateBuilder()
            .AddBool("isActive", true)
            .AddBool("isCompleted", false)
            .Build();

        // Assert
        Assert.Equal(2, outputs.Count);
        Assert.True(outputs.Contains("isActive"));
        Assert.True(outputs.Contains("isCompleted"));
    }

    [Fact]
    public void ShouldAddComplexObject_WhenUsingBuilderAddObject()
    {
        // Arrange
        var customObject = new TestObject { Id = 1, Name = "Test" };

        // Act
        var outputs = ProcessOutputs.CreateBuilder()
            .AddObject("testObject", customObject)
            .Build();

        // Assert
        var retrieved = outputs.Get<TestObject>("testObject");
        Assert.NotNull(retrieved);
        Assert.Equal(1, retrieved!.Id);
        Assert.Equal("Test", retrieved.Name);
    }

    [Fact]
    public void ShouldAddWithPrefix_WhenUsingBuilderAddTaskOutput()
    {
        // Arrange
        var taskOutput = TaskOutput.Text("Task completed");

        // Act
        var outputs = ProcessOutputs.CreateBuilder()
            .AddTaskOutput("task123", taskOutput)
            .Build();

        // Assert
        Assert.True(outputs.Contains("task_task123"));
        var retrieved = outputs.Get<TaskOutput>("task_task123");
        Assert.NotNull(retrieved);
        Assert.Equal("Task completed", retrieved!.RawOutput);
    }

    [Fact]
    public void ShouldAddWithErrorPrefix_WhenUsingBuilderAddError()
    {
        // Arrange & Act
        var outputs = ProcessOutputs.CreateBuilder()
            .AddError("validation", "Invalid input")
            .AddError("network", "Connection timeout")
            .Build();

        // Assert
        Assert.True(outputs.Contains("error_validation"));
        Assert.True(outputs.Contains("error_network"));
        Assert.Equal("Invalid input", outputs.Get<string>("error_validation"));
        Assert.Equal("Connection timeout", outputs.Get<string>("error_network"));
    }

    [Fact]
    public void ShouldReplaceValue_WhenUsingBuilderOverwriteKey()
    {
        // Arrange & Act
        var outputs = ProcessOutputs.CreateBuilder()
            .AddString("key", "original")
            .AddString("key", "updated")
            .Build();

        // Assert
        Assert.Equal("updated", outputs.Get<string>("key"));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingProcessOutputValueFromWithNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ProcessOutputValue.From(null!));
    }

    [Fact]
    public void ShouldThrowInvalidCastException_WhenUsingProcessOutputValueGettingValueWithWrongType()
    {
        // Arrange
        var value = ProcessOutputValue.From("string value");

        // Act & Assert
        // GetValue only works with reference types due to generic constraint where T : class
        // Attempting to cast to wrong reference type should throw InvalidCastException
        Assert.Throws<InvalidCastException>(() => value.GetValue<Uri>());
    }

    [Fact]
    public void ShouldReturnCorrectValues_WhenUsingProcessOutputValueUsingProperties()
    {
        // Arrange
        var input = "test value";
        var value = ProcessOutputValue.From(input);

        // Act & Assert
        Assert.Equal(input, value.RawValue);
        Assert.Equal(typeof(string), value.ValueType);
        Assert.Equal(input, value.GetValue<string>());
    }

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingTaskOutputMapWithEmpty()
    {
        // Act
        var collection = TaskOutputMap.Empty;

        // Assert
        Assert.NotNull(collection);
        Assert.Equal(0, collection.Count);
        Assert.Empty(collection.TaskIds);
    }

    [Fact]
    public void ShouldAddOutput_WhenUsingTaskOutputMapWith()
    {
        // Arrange
        var collection = TaskOutputMap.Empty;
        var taskOutput = TaskOutput.Text("Result");

        // Act
        var newCollection = collection.With("task1", taskOutput);

        // Assert
        Assert.Equal(1, newCollection.Count);
        Assert.Equal(taskOutput, newCollection.Get("task1"));
    }

    [Fact]
    public void ShouldReturnNull_WhenUsingTaskOutputMapGettingWithNonExistingKey()
    {
        // Arrange
        var collection = TaskOutputMap.Empty;

        // Act
        var output = collection.Get("nonExisting");

        // Assert
        Assert.Null(output);
    }

    [Fact]
    public void ShouldCreateCollection_WhenUsingTaskOutputMapFromOutputs()
    {
        // Arrange
        var outputs = new[]
        {
            TaskOutput.Create("output1", "text", taskId: TaskId.Create()),
            TaskOutput.Create("output2", "json", taskId: TaskId.Create())
        };

        // Act
        var collection = TaskOutputMap.FromOutputs(outputs);

        // Assert
        Assert.Equal(2, collection.Count);
        Assert.All(outputs, o => Assert.NotNull(collection.Get(o.TaskId!.ToString())));
    }

    private class TestObject
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
