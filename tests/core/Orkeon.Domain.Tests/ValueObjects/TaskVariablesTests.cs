using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

public class TaskVariablesTests
{
    #region Empty Instance Tests

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var variables = TaskVariables.Empty;

        // Assert
        Assert.NotNull(variables);
        Assert.Empty(variables.Keys);
        Assert.Null(variables.GetString("any"));
        Assert.Null(variables.GetInt("any"));
        Assert.Null(variables.GetBool("any"));
        Assert.Null(variables.GetDouble("any"));
        Assert.Null(variables.GetObject<object>("any"));
    }

    #endregion

    #region Getter Method Tests

    [Fact]
    public void ShouldReturnValue_WhenGettingStringWithExistingKey()
    {
        // Arrange
        var variables = TaskVariables.CreateBuilder()
            .AddString("name", "test-value")
            .Build();

        // Act
        var result = variables.GetString("name");

        // Assert
        Assert.Equal("test-value", result);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingStringWithNonExistentKey()
    {
        // Arrange
        var variables = TaskVariables.Empty;

        // Act
        var result = variables.GetString("missing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingIntWithExistingKey()
    {
        // Arrange
        var variables = TaskVariables.CreateBuilder()
            .AddInt("count", 42)
            .Build();

        // Act
        var result = variables.GetInt("count");

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingIntWithNonExistentKey()
    {
        // Arrange
        var variables = TaskVariables.Empty;

        // Act
        var result = variables.GetInt("missing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingBoolWithExistingKey()
    {
        // Arrange
        var variables = TaskVariables.CreateBuilder()
            .AddBool("enabled", true)
            .Build();

        // Act
        var result = variables.GetBool("enabled");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingBoolWithNonExistentKey()
    {
        // Arrange
        var variables = TaskVariables.Empty;

        // Act
        var result = variables.GetBool("missing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingDoubleWithExistingKey()
    {
        // Arrange
        var variables = TaskVariables.CreateBuilder()
            .AddDouble("rate", 3.14)
            .Build();

        // Act
        var result = variables.GetDouble("rate");

        // Assert
        Assert.Equal(3.14, result);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingDoubleWithNonExistentKey()
    {
        // Arrange
        var variables = TaskVariables.Empty;

        // Act
        var result = variables.GetDouble("missing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingObjectWithExistingKey()
    {
        // Arrange
        var testObject = new { name = "test", value = 123 };
        var variables = TaskVariables.CreateBuilder()
            .AddObject("data", testObject)
            .Build();

        // Act
        var result = variables.GetObject<object>("data");

        // Assert
        Assert.Same(testObject, result);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingObjectWithNonExistentKey()
    {
        // Arrange
        var variables = TaskVariables.Empty;

        // Act
        var result = variables.GetObject<object>("missing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingObjectWithWrongType()
    {
        // Arrange
        var variables = TaskVariables.CreateBuilder()
            .AddObject("data", "string value")
            .Build();

        // Act
        var result = variables.GetObject<string>("data");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Keys Property Tests

    [Fact]
    public void ShouldReturnEmpty_WhenUsingKeysWithEmptyVariables()
    {
        // Act
        var keys = TaskVariables.Empty.Keys.ToList();

        // Assert
        Assert.Empty(keys);
    }

    [Fact]
    public void ShouldReturnAllKeys_WhenUsingKeysWithSingleTypeVariables()
    {
        // Arrange
        var variables = TaskVariables.CreateBuilder()
            .AddString("str1", "value1")
            .AddString("str2", "value2")
            .Build();

        // Act
        var keys = variables.Keys.ToList();

        // Assert
        Assert.Equal(2, keys.Count);
        Assert.Contains("str1", keys);
        Assert.Contains("str2", keys);
    }

    [Fact]
    public void ShouldReturnAllKeys_WhenUsingKeysWithMixedTypeVariables()
    {
        // Arrange
        var variables = TaskVariables.CreateBuilder()
            .AddString("name", "test")
            .AddInt("count", 10)
            .AddBool("enabled", true)
            .AddDouble("rate", 2.5)
            .AddObject("data", new object())
            .Build();

        // Act
        var keys = variables.Keys.ToList();

        // Assert
        Assert.Equal(5, keys.Count);
        Assert.Contains("name", keys);
        Assert.Contains("count", keys);
        Assert.Contains("enabled", keys);
        Assert.Contains("rate", keys);
        Assert.Contains("data", keys);
    }

    [Fact]
    public void ShouldReturnDistinctKeys_WhenUsingKeysWithDuplicateKeysAcrossTypes()
    {
        // Arrange
        var variables = TaskVariables.CreateBuilder()
            .AddString("key", "string")
            .AddInt("key", 42)
            .Build();

        // Act
        var keys = variables.Keys.ToList();

        // Assert
        Assert.Single(keys);
        Assert.Equal("key", keys[0]);
    }

    #endregion

    #region With Method Tests

    [Fact]
    public void ShouldCreateNewInstanceWithValue_WhenUsingWithStringValue()
    {
        // Arrange
        var original = TaskVariables.Empty;

        // Act
        var modified = original.With("name", "test-value");

        // Assert
        Assert.NotSame(original, modified);
        Assert.Null(original.GetString("name"));
        Assert.Equal("test-value", modified.GetString("name"));
    }

    [Fact]
    public void ShouldCreateNewInstanceWithValue_WhenUsingWithIntValue()
    {
        // Arrange
        var original = TaskVariables.Empty;

        // Act
        var modified = original.With("count", 100);

        // Assert
        Assert.NotSame(original, modified);
        Assert.Null(original.GetInt("count"));
        Assert.Equal(100, modified.GetInt("count"));
    }

    [Fact]
    public void ShouldCreateNewInstanceWithValue_WhenUsingWithBoolValue()
    {
        // Arrange
        var original = TaskVariables.Empty;

        // Act
        var modified = original.With("enabled", true);

        // Assert
        Assert.NotSame(original, modified);
        Assert.Null(original.GetBool("enabled"));
        Assert.True(modified.GetBool("enabled"));
    }

    [Fact]
    public void ShouldCreateNewInstanceWithValue_WhenUsingWithDoubleValue()
    {
        // Arrange
        var original = TaskVariables.Empty;

        // Act
        var modified = original.With("rate", 1.23);

        // Assert
        Assert.NotSame(original, modified);
        Assert.Null(original.GetDouble("rate"));
        Assert.Equal(1.23, modified.GetDouble("rate"));
    }

    [Fact]
    public void ShouldCreateNewInstanceWithValue_WhenUsingWithObjectValue()
    {
        // Arrange
        var original = TaskVariables.Empty;
        var data = new { id = 1, name = "test" };

        // Act
        var modified = original.With("data", data);

        // Assert
        Assert.NotSame(original, modified);
        Assert.Null(original.GetObject<object>("data"));
        Assert.Same(data, modified.GetObject<object>("data"));
    }

    [Fact]
    public void ShouldUpdateValue_WhenUsingWithWithExistingKey()
    {
        // Arrange
        var original = TaskVariables.CreateBuilder()
            .AddString("name", "original")
            .Build();

        // Act
        var modified = original.With("name", "updated");

        // Assert
        Assert.Equal("original", original.GetString("name"));
        Assert.Equal("updated", modified.GetString("name"));
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingWithWithEmptyKey()
    {
        // Arrange
        var variables = TaskVariables.Empty;

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() => variables.With("", "value"));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingWithWithNullKey()
    {
        // Arrange
        var variables = TaskVariables.Empty;

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() => variables.With(null!, "value"));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingWithWhitespaceKey()
    {
        // Arrange
        var variables = TaskVariables.Empty;

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() => variables.With("   ", "value"));
        Assert.Equal("key", exception.ParamName);
    }

    #endregion

    #region Builder Tests

    [Fact]
    public void ShouldReturnBuilder_WhenUsingCreateBuilder()
    {
        // Act
        var builder = TaskVariables.CreateBuilder();

        // Assert
        Assert.NotNull(builder);
    }

    [Fact]
    public void ShouldAddStringVariable_WhenUsingBuilderAddString()
    {
        // Act
        var variables = TaskVariables.CreateBuilder()
            .AddString("text", "sample text")
            .Build();

        // Assert
        Assert.Equal("sample text", variables.GetString("text"));
    }

    [Fact]
    public void ShouldAddIntVariable_WhenUsingBuilderAddInt()
    {
        // Act
        var variables = TaskVariables.CreateBuilder()
            .AddInt("number", 789)
            .Build();

        // Assert
        Assert.Equal(789, variables.GetInt("number"));
    }

    [Fact]
    public void ShouldAddBoolVariable_WhenUsingBuilderAddBool()
    {
        // Act
        var variables = TaskVariables.CreateBuilder()
            .AddBool("flag", false)
            .Build();

        // Assert
        Assert.False(variables.GetBool("flag"));
    }

    [Fact]
    public void ShouldAddDoubleVariable_WhenUsingBuilderAddDouble()
    {
        // Act
        var variables = TaskVariables.CreateBuilder()
            .AddDouble("percentage", 95.5)
            .Build();

        // Assert
        Assert.Equal(95.5, variables.GetDouble("percentage"));
    }

    [Fact]
    public void ShouldAddObjectVariable_WhenUsingBuilderAddObject()
    {
        // Arrange
        var config = new { timeout = 30, retries = 3 };

        // Act
        var variables = TaskVariables.CreateBuilder()
            .AddObject("config", config)
            .Build();

        // Assert
        Assert.Same(config, variables.GetObject<object>("config"));
    }

    [Fact]
    public void ShouldAddAllVariables_WhenUsingBuilderChainedCalls()
    {
        // Arrange
        var metadata = new { version = "1.0", author = "test" };

        // Act
        var variables = TaskVariables.CreateBuilder()
            .AddString("name", "task")
            .AddInt("priority", 5)
            .AddBool("active", true)
            .AddDouble("progress", 0.75)
            .AddObject("metadata", metadata)
            .Build();

        // Assert
        Assert.Equal("task", variables.GetString("name"));
        Assert.Equal(5, variables.GetInt("priority"));
        Assert.True(variables.GetBool("active"));
        Assert.Equal(0.75, variables.GetDouble("progress"));
        Assert.Same(metadata, variables.GetObject<object>("metadata"));
    }

    [Fact]
    public void ShouldKeepLastValue_WhenUsingBuilderOverwriteExistingKey()
    {
        // Act
        var variables = TaskVariables.CreateBuilder()
            .AddString("key", "first")
            .AddString("key", "second")
            .Build();

        // Assert
        Assert.Equal("second", variables.GetString("key"));
    }

    [Fact]
    public void ShouldReturnImmutableInstance_WhenUsingBuilderBuild()
    {
        // Arrange
        var builder = TaskVariables.CreateBuilder()
            .AddString("test", "value");

        // Act
        var variables1 = builder.Build();
        var variables2 = builder.Build();

        // Assert
        Assert.NotSame(variables1, variables2);
        Assert.Equal("value", variables1.GetString("test"));
        Assert.Equal("value", variables2.GetString("test"));
    }

    #endregion

    #region Type Separation Tests

    [Fact]
    public void ShouldStoreSeparately_WhenUsingTypeSeparationWithSameKeyDifferentTypes()
    {
        // Act
        var variables = TaskVariables.CreateBuilder()
            .AddString("value", "text")
            .AddInt("value", 42)
            .AddBool("value", true)
            .AddDouble("value", 3.14)
            .AddObject("value", new object())
            .Build();

        // Assert - All should be accessible with same key
        Assert.Equal("text", variables.GetString("value"));
        Assert.Equal(42, variables.GetInt("value"));
        Assert.True(variables.GetBool("value"));
        Assert.Equal(3.14, variables.GetDouble("value"));
        Assert.NotNull(variables.GetObject<object>("value"));
    }

    [Fact]
    public void ShouldReturnUniqueKeys_WhenUsingTypeSeparationKeys()
    {
        // Arrange
        var variables = TaskVariables.CreateBuilder()
            .AddString("shared", "text")
            .AddInt("shared", 123)
            .AddString("unique", "only-string")
            .Build();

        // Act
        var keys = variables.Keys.ToList();

        // Assert
        Assert.Equal(2, keys.Count);
        Assert.Contains("shared", keys);
        Assert.Contains("unique", keys);
    }

    #endregion

    #region Immutability Tests

    [Fact]
    public void ShouldNotModifyOriginal_WhenUsingImmutabilityWithMethod()
    {
        // Arrange
        var original = TaskVariables.CreateBuilder()
            .AddString("original", "value")
            .Build();

        // Act
        var modified1 = original.With("new1", "value1");
        var modified2 = modified1.With("new2", 42);
        var modified3 = modified2.With("original", "updated");

        // Assert
        Assert.Equal("value", original.GetString("original"));
        Assert.Null(original.GetString("new1"));
        Assert.Null(original.GetInt("new2"));

        Assert.Equal("value", modified1.GetString("original"));
        Assert.Equal("value1", modified1.GetString("new1"));
        Assert.Null(modified1.GetString("new2"));

        Assert.Equal("value", modified2.GetString("original"));
        Assert.Equal("value1", modified2.GetString("new1"));
        Assert.Equal(42, modified2.GetInt("new2"));

        Assert.Equal("updated", modified3.GetString("original"));
        Assert.Equal("value1", modified3.GetString("new1"));
        Assert.Equal(42, modified3.GetInt("new2"));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithEmptyStringValue()
    {
        // Act
        var variables = TaskVariables.CreateBuilder()
            .AddString("empty", "")
            .Build();

        // Assert
        Assert.Equal("", variables.GetString("empty"));
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithNegativeNumbers()
    {
        // Act
        var variables = TaskVariables.CreateBuilder()
            .AddInt("negInt", -100)
            .AddDouble("negDouble", -3.14)
            .Build();

        // Assert
        Assert.Equal(-100, variables.GetInt("negInt"));
        Assert.Equal(-3.14, variables.GetDouble("negDouble"));
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithZeroValues()
    {
        // Act
        var variables = TaskVariables.CreateBuilder()
            .AddInt("zero", 0)
            .AddDouble("zeroDouble", 0.0)
            .Build();

        // Assert
        Assert.Equal(0, variables.GetInt("zero"));
        Assert.Equal(0.0, variables.GetDouble("zeroDouble"));
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithSpecialDoubleValues()
    {
        // Act
        var variables = TaskVariables.CreateBuilder()
            .AddDouble("infinity", double.PositiveInfinity)
            .AddDouble("negInfinity", double.NegativeInfinity)
            .AddDouble("nan", double.NaN)
            .Build();

        // Assert
        Assert.Equal(double.PositiveInfinity, variables.GetDouble("infinity"));
        Assert.Equal(double.NegativeInfinity, variables.GetDouble("negInfinity"));
        Assert.True(double.IsNaN(variables.GetDouble("nan")!.Value));
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithMinMaxValues()
    {
        // Act
        var variables = TaskVariables.CreateBuilder()
            .AddInt("maxInt", int.MaxValue)
            .AddInt("minInt", int.MinValue)
            .AddDouble("maxDouble", double.MaxValue)
            .AddDouble("minDouble", double.MinValue)
            .Build();

        // Assert
        Assert.Equal(int.MaxValue, variables.GetInt("maxInt"));
        Assert.Equal(int.MinValue, variables.GetInt("minInt"));
        Assert.Equal(double.MaxValue, variables.GetDouble("maxDouble"));
        Assert.Equal(double.MinValue, variables.GetDouble("minDouble"));
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithComplexObjectTypes()
    {
        // Arrange
        var list = new List<string> { "item1", "item2" };
        var dict = new Dictionary<string, int> { { "key", 42 } };
        var array = new[] { 1, 2, 3 };

        // Act
        var variables = TaskVariables.CreateBuilder()
            .AddObject("list", list)
            .AddObject("dict", dict)
            .AddObject("array", array)
            .Build();

        // Assert
        Assert.Same(list, variables.GetObject<List<string>>("list"));
        Assert.Same(dict, variables.GetObject<Dictionary<string, int>>("dict"));
        Assert.Same(array, variables.GetObject<int[]>("array"));
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldTaskExecutionVariables_WhenUsingComplexScenario()
    {
        // Simulate variables for a data processing task
        var taskConfig = new
        {
            batchSize = 1000,
            timeout = TimeSpan.FromMinutes(30),
            retryPolicy = new { maxRetries = 3, backoffMs = 1000 }
        };

        var variables = TaskVariables.CreateBuilder()
            .AddString("taskId", "task-12345")
            .AddString("dataSource", "/data/input.csv")
            .AddString("outputPath", "/data/output.json")
            .AddInt("maxRecords", 50000)
            .AddInt("parallelThreads", 4)
            .AddBool("validateInput", true)
            .AddBool("compressOutput", false)
            .AddDouble("errorThreshold", 0.05)
            .AddDouble("progressCheckInterval", 10.0)
            .AddObject("configuration", taskConfig)
            .Build();

        // Assert all variables are accessible
        Assert.Equal("task-12345", variables.GetString("taskId"));
        Assert.Equal("/data/input.csv", variables.GetString("dataSource"));
        Assert.Equal("/data/output.json", variables.GetString("outputPath"));
        Assert.Equal(50000, variables.GetInt("maxRecords"));
        Assert.Equal(4, variables.GetInt("parallelThreads"));
        Assert.True(variables.GetBool("validateInput"));
        Assert.False(variables.GetBool("compressOutput"));
        Assert.Equal(0.05, variables.GetDouble("errorThreshold"));
        Assert.Equal(10.0, variables.GetDouble("progressCheckInterval"));
        Assert.Same(taskConfig, variables.GetObject<object>("configuration"));

        // Verify keys count
        Assert.Equal(10, variables.Keys.Count());
    }

    [Fact]
    public void ShouldVariableEvolution_WhenUsingComplexScenario()
    {
        // Start with basic variables
        var initial = TaskVariables.CreateBuilder()
            .AddString("status", "initializing")
            .AddInt("progress", 0)
            .AddBool("started", false)
            .Build();

        // Update status to started
        var started = initial
            .With("status", "started")
            .With("started", true)
            .With("startTime", DateTime.UtcNow);

        // Add progress tracking
        var progressing = started
            .With("progress", 25)
            .With("status", "processing")
            .With("currentBatch", 1);

        // Add error handling
        var withErrors = progressing
            .With("hasErrors", true)
            .With("errorCount", 3)
            .With("lastError", "Connection timeout");

        // Complete processing
        var completed = withErrors
            .With("status", "completed")
            .With("progress", 100)
            .With("endTime", DateTime.UtcNow)
            .With("totalProcessed", 10000);

        // Assert evolution
        Assert.Equal("initializing", initial.GetString("status"));
        Assert.Equal(0, initial.GetInt("progress"));
        Assert.False(initial.GetBool("started"));

        Assert.Equal("started", started.GetString("status"));
        Assert.True(started.GetBool("started"));
        Assert.Contains("startTime", started.Keys);

        Assert.Equal("processing", progressing.GetString("status"));
        Assert.Equal(25, progressing.GetInt("progress"));
        Assert.Equal(1, progressing.GetInt("currentBatch"));

        Assert.True(withErrors.GetBool("hasErrors"));
        Assert.Equal(3, withErrors.GetInt("errorCount"));
        Assert.Equal("Connection timeout", withErrors.GetString("lastError"));

        Assert.Equal("completed", completed.GetString("status"));
        Assert.Equal(100, completed.GetInt("progress"));
        Assert.Equal(10000, completed.GetInt("totalProcessed"));
        Assert.Contains("endTime", completed.Keys);
    }

    [Fact]
    public void ShouldMultipleVariablesWithSameKeys_WhenUsingComplexScenario()
    {
        // Test storing different types under same logical key
        var variables = TaskVariables.CreateBuilder()
            .AddString("result", "Task completed successfully")
            .AddInt("result", 200) // HTTP status code
            .AddBool("result", true) // Success flag
            .AddDouble("result", 99.5) // Success percentage
            .AddObject("result", new
            {
                status = "success",
                code = 200,
                message = "Task completed",
                details = new { duration = "5.2s", records = 1500 }
            })
            .Build();

        // All types should be accessible independently
        Assert.Equal("Task completed successfully", variables.GetString("result"));
        Assert.Equal(200, variables.GetInt("result"));
        Assert.True(variables.GetBool("result"));
        Assert.Equal(99.5, variables.GetDouble("result"));

        var resultObject = variables.GetObject<object>("result");
        Assert.NotNull(resultObject);

        // Keys should only show "result" once
        var keys = variables.Keys.ToList();
        Assert.Single(keys);
        Assert.Equal("result", keys[0]);
    }

    #endregion
}
