using Orkeon.Domain.Task.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Domain.Tests.ValueObjects;

public class TaskContextTests
{
    #region TaskContext Tests

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var context = TaskContext.Empty;

        // Assert
        Assert.NotNull(context);
        Assert.Empty(context.Keys);
        Assert.Equal(0, context.Count);
        Assert.False(context.ContainsKey("anyKey"));
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingWithExistingKey()
    {
        // Arrange
        var context = TaskContext.CreateBuilder()
            .AddInput("fileName", "document.txt")
            .AddOutput("result", "success")
            .AddPreviousResult("completed")
            .Build();

        // Act & Assert
        Assert.Equal("document.txt", context.Get<string>("input.fileName"));
        Assert.Equal("success", context.Get<string>("output.result"));
        Assert.Equal("completed", context.Get<string>("previous_result"));
    }

    [Fact]
    public void ShouldReturnDefault_WhenGettingWithNonExistentKey()
    {
        // Arrange
        var context = TaskContext.Empty;

        // Act & Assert
        Assert.Null(context.Get<string>("missing"));
        Assert.Equal(0, context.Get<int>("missing"));
        Assert.False(context.Get<bool>("missing"));
        Assert.Equal(0.0, context.Get<double>("missing"));
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingContainsKey()
    {
        // Arrange
        var context = TaskContext.CreateBuilder()
            .AddTool("calculator", new { version = "1.0" })
            .Build();

        // Act & Assert
        Assert.True(context.ContainsKey("tool.calculator"));
        Assert.False(context.ContainsKey("missing_key"));
    }

    [Fact]
    public void ShouldReturnAllKeys_WhenUsingKeys()
    {
        // Arrange
        var context = TaskContext.CreateBuilder()
            .AddInput("data", "test data")
            .AddAgentContext("agent1", "context data")
            .AddMemory("recent", "memory data")
            .Build();

        // Act
        var keys = context.Keys.ToList();

        // Assert
        Assert.Equal(3, keys.Count);
        Assert.Contains("input.data", keys);
        Assert.Contains("agent.agent1", keys);
        Assert.Contains("memory.recent", keys);
    }

    [Fact]
    public void ShouldReturnNumberOfItems_WhenCounting()
    {
        // Arrange
        var context = TaskContext.CreateBuilder()
            .AddInput("input1", "value1")
            .AddInput("input2", "value2")
            .AddOutput("output1", "result1")
            .AddCrewContext("config", "crew config")
            .Build();

        // Act & Assert
        Assert.Equal(4, context.Count);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedValue_WhenSetting()
    {
        // Arrange
        var original = TaskContext.CreateBuilder()
            .AddInput("count", 5)
            .Build();

        // Act
        var updated = original.Set("input.count", 10);
        var added = updated.Set("new_key", "new value");

        // Assert
        Assert.NotSame(original, updated);
        Assert.NotSame(updated, added);

        Assert.Equal(5, original.Get<int>("input.count"));
        Assert.Equal(10, updated.Get<int>("input.count"));
        Assert.Equal("new value", added.Get<string>("new_key"));

        Assert.Equal(1, original.Count);
        Assert.Equal(1, updated.Count);
        Assert.Equal(2, added.Count);
    }

    [Fact]
    public void ShouldUpdateMultipleValues_WhenSettingMultiple()
    {
        // Arrange
        var original = TaskContext.CreateBuilder()
            .AddInput("original", "value")
            .Build();

        var updates = new[]
        {
            new KeyValuePair<string, object>("input.original", "updated"),
            new KeyValuePair<string, object>("output.status", "completed"),
            new KeyValuePair<string, object>("progress", 0.95)
        };

        // Act
        var updated = original.SetMultiple(updates);

        // Assert
        Assert.Equal(3, updated.Count);
        Assert.Equal("updated", updated.Get<string>("input.original"));
        Assert.Equal("completed", updated.Get<string>("output.status"));
        Assert.Equal(0.95, updated.Get<double>("progress"));
    }

    [Fact]
    public void ShouldCreateNewInstanceWithoutKey_WhenRemoving()
    {
        // Arrange
        var original = TaskContext.CreateBuilder()
            .AddInput("keep", "value1")
            .AddInput("remove", "value2")
            .AddOutput("result", "success")
            .Build();

        // Act
        var modified = original.Remove("input.remove");

        // Assert
        Assert.Equal(3, original.Count);
        Assert.Equal(2, modified.Count);
        Assert.True(original.ContainsKey("input.remove"));
        Assert.False(modified.ContainsKey("input.remove"));
        Assert.Equal("value1", modified.Get<string>("input.keep"));
        Assert.Equal("success", modified.Get<string>("output.result"));
    }

    [Fact]
    public void ShouldReturnAllContext_WhenUsingToDictionary()
    {
        // Arrange
        var context = TaskContext.CreateBuilder()
            .AddInput("file", "data.csv")
            .AddOutput("records", 1000)
            .AddPreviousResult(true)
            .AddTool("parser", new { format = "csv" })
            .Build();

        // Act
        var dict = context.ToDictionary();

        // Assert
        Assert.Equal(4, dict.Count);
        Assert.Equal("data.csv", dict["input.file"]);
        Assert.Equal(1000, dict["output.records"]);
        Assert.True((bool)dict["previous_result"]);
        Assert.NotNull(dict["tool.parser"]);
    }

    [Fact]
    public void ShouldSetWithPrefix_WhenUsingBuilderAddInput()
    {
        // Act
        var context = TaskContext.CreateBuilder()
            .AddInput("fileName", "document.pdf")
            .AddInput("encoding", "utf-8")
            .AddInput("maxSize", 1024)
            .Build();

        // Assert
        Assert.Equal("document.pdf", context.Get<string>("input.fileName"));
        Assert.Equal("utf-8", context.Get<string>("input.encoding"));
        Assert.Equal(1024, context.Get<int>("input.maxSize"));
    }

    [Fact]
    public void ShouldSetWithPrefix_WhenUsingBuilderAddOutput()
    {
        // Act
        var context = TaskContext.CreateBuilder()
            .AddOutput("status", "completed")
            .AddOutput("records", 500)
            .AddOutput("duration", TimeSpan.FromMinutes(2))
            .Build();

        // Assert
        Assert.Equal("completed", context.Get<string>("output.status"));
        Assert.Equal(500, context.Get<int>("output.records"));
        Assert.Equal(TimeSpan.FromMinutes(2), context.Get<TimeSpan>("output.duration"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddPreviousResult()
    {
        // Act
        var context = TaskContext.CreateBuilder()
            .AddPreviousResult(new { success = true, data = "result data" })
            .Build();

        // Assert
        Assert.NotNull(context.Get<object>("previous_result"));
    }

    [Fact]
    public void ShouldSetWithPrefix_WhenUsingBuilderAddAgentContext()
    {
        // Act
        var context = TaskContext.CreateBuilder()
            .AddAgentContext("agent-001", new { role = "analyst", status = "active" })
            .AddAgentContext("agent-002", new { role = "writer", status = "idle" })
            .Build();

        // Assert
        Assert.NotNull(context.Get<object>("agent.agent-001"));
        Assert.NotNull(context.Get<object>("agent.agent-002"));
    }

    [Fact]
    public void ShouldSetWithPrefix_WhenUsingBuilderAddTool()
    {
        // Act
        var context = TaskContext.CreateBuilder()
            .AddTool("FileReader", new { version = "2.1", config = "default" })
            .AddTool("DataProcessor", new { threads = 4, memory = "512MB" })
            .Build();

        // Assert
        Assert.NotNull(context.Get<object>("tool.FileReader"));
        Assert.NotNull(context.Get<object>("tool.DataProcessor"));
    }

    [Fact]
    public void ShouldSetWithPrefix_WhenUsingBuilderAddMemory()
    {
        // Act
        var context = TaskContext.CreateBuilder()
            .AddMemory("shortTerm", new { items = 10, size = "1MB" })
            .AddMemory("longTerm", new { items = 1000, size = "10MB" })
            .Build();

        // Assert
        Assert.NotNull(context.Get<object>("memory.shortTerm"));
        Assert.NotNull(context.Get<object>("memory.longTerm"));
    }

    [Fact]
    public void ShouldSetWithPrefix_WhenUsingBuilderAddCrewContext()
    {
        // Act
        var context = TaskContext.CreateBuilder()
            .AddCrewContext("config", new { parallel = true, timeout = 300 })
            .AddCrewContext("status", "executing")
            .Build();

        // Assert
        Assert.NotNull(context.Get<object>("crew.config"));
        Assert.Equal("executing", context.Get<string>("crew.status"));
    }

    [Fact]
    public void ShouldAddCustomContext_WhenUsingBuilderAdd()
    {
        // Act
        var context = TaskContext.CreateBuilder()
            .Add("custom_string", "value")
            .Add("custom_int", 42)
            .Add("custom_bool", true)
            .Add("custom_object", new { type = "test", id = 123 })
            .Build();

        // Assert
        Assert.Equal("value", context.Get<string>("custom_string"));
        Assert.Equal(42, context.Get<int>("custom_int"));
        Assert.True(context.Get<bool>("custom_bool"));
        Assert.NotNull(context.Get<object>("custom_object"));
    }

    [Fact]
    public void ShouldAddAllContext_WhenUsingBuilderChainedCalls()
    {
        // Act
        var context = TaskContext.CreateBuilder()
            .AddInput("source", "database")
            .AddInput(ParamQuery, "SELECT * FROM users")
            .AddOutput("count", 1500)
            .AddPreviousResult("validation passed")
            .AddAgentContext("db-agent", "connected")
            .AddTool("SQLExecutor", "ready")
            .AddMemory("cache", "enabled")
            .AddCrewContext("mode", "production")
            .Add("timestamp", DateTime.UtcNow)
            .Build();

        // Assert
        Assert.Equal(9, context.Count);
        Assert.Equal("database", context.Get<string>("input.source"));
        Assert.Equal("SELECT * FROM users", context.Get<string>("input.query"));
        Assert.Equal(1500, context.Get<int>("output.count"));
        Assert.Equal("validation passed", context.Get<string>("previous_result"));
        Assert.Equal("connected", context.Get<string>("agent.db-agent"));
        Assert.Equal("ready", context.Get<string>("tool.SQLExecutor"));
        Assert.Equal("enabled", context.Get<string>("memory.cache"));
        Assert.Equal("production", context.Get<string>("crew.mode"));
        // DateTime is a value type, it's never null - removed Assert.NotNull
    }

    [Fact]
    public void ShouldKeepLastValue_WhenUsingBuilderOverwriteExistingKey()
    {
        // Act
        var context = TaskContext.CreateBuilder()
            .AddInput("value", "first")
            .AddInput("value", "second")
            .Build();

        // Assert
        Assert.Equal("second", context.Get<string>("input.value"));
    }

    [Fact]
    public void ShouldCopyExistingContext_WhenCreatingBuilderFrom()
    {
        // Arrange
        var original = TaskContext.CreateBuilder()
            .AddInput("data", "original")
            .AddOutput("result", "success")
            .Add("custom", "value")
            .Build();

        // Act
        var modified = TaskContext.CreateBuilderFrom(original)
            .AddInput("data", "modified")
            .AddInput("newData", "added")
            .Build();

        // Assert
        Assert.Equal(3, original.Count);
        Assert.Equal("original", original.Get<string>("input.data"));

        Assert.Equal(4, modified.Count);
        Assert.Equal("modified", modified.Get<string>("input.data"));
        Assert.Equal("added", modified.Get<string>("input.newData"));
        Assert.Equal("success", modified.Get<string>("output.result"));
        Assert.Equal("value", modified.Get<string>("custom"));
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithNull()
    {
        // Act
        var context = TaskContext.FromDictionary(null);

        // Assert
        Assert.Same(TaskContext.Empty, context);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithEmptyDictionary()
    {
        // Act
        var context = TaskContext.FromDictionary([]);

        // Assert
        Assert.Same(TaskContext.Empty, context);
    }

    [Fact]
    public void ShouldCreateContext_WhenUsingFromDictionaryWithValues()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "input.file", "data.json" },
            { "output.status", "processed" },
            { "agent.worker", "active" },
            { "tool.parser", "json-parser" },
            { "memory.cache", 100 }
        };

        // Act
        var context = TaskContext.FromDictionary(dict);

        // Assert
        Assert.Equal(5, context.Count);
        Assert.Equal("data.json", context.Get<string>("input.file"));
        Assert.Equal("processed", context.Get<string>("output.status"));
        Assert.Equal("active", context.Get<string>("agent.worker"));
        Assert.Equal("json-parser", context.Get<string>("tool.parser"));
        Assert.Equal(100, context.Get<int>("memory.cache"));
    }

    [Fact]
    public void ShouldPreserveValues_WhenUsingRoundTripToDictionaryFromDictionary()
    {
        // Arrange
        var original = TaskContext.CreateBuilder()
            .AddInput("source", "api")
            .AddOutput("records", 250)
            .AddPreviousResult(true)
            .AddAgentContext("processor", "running")
            .AddTool("HttpClient", new { timeout = 30 })
            .Build();

        // Act
        var dict = original.ToDictionary();
        var restored = TaskContext.FromDictionary(dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));

        // Assert
        Assert.Equal(original.Count, restored.Count);
        Assert.Equal("api", restored.Get<string>("input.source"));
        Assert.Equal(250, restored.Get<int>("output.records"));
        Assert.True(restored.Get<bool>("previous_result"));
        Assert.Equal("running", restored.Get<string>("agent.processor"));
        Assert.NotNull(restored.Get<object>("tool.HttpClient"));
    }

    #endregion

    #region TaskContextValue Tests

    [Fact]
    public void ShouldCreate_WhenUsingTaskContextValueFromWithValidValue()
    {
        // Act
        var stringValue = TaskContextValue.From("test");
        var intValue = TaskContextValue.From(42);
        var boolValue = TaskContextValue.From(true);
        var objectValue = TaskContextValue.From(new { id = 1, name = "test" });

        // Assert
        Assert.NotNull(stringValue);
        Assert.NotNull(intValue);
        Assert.NotNull(boolValue);
        Assert.NotNull(objectValue);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingTaskContextValueFromWithNull()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => TaskContextValue.From(null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingTaskContextValueGettingValueWithCorrectType()
    {
        // Arrange
        var value = TaskContextValue.From("context data");

        // Act
        var result = value.GetValue<string>();

        // Assert
        Assert.Equal("context data", result);
    }

    [Fact]
    public void ShouldConvert_WhenUsingTaskContextValueGettingValueWithConvertibleType()
    {
        // Arrange
        var intValue = TaskContextValue.From(456);

        // Act
        var asString = intValue.GetValue<string>();
        var asDouble = intValue.GetValue<double>();
        var asLong = intValue.GetValue<long>();

        // Assert
        Assert.Equal("456", asString);
        Assert.Equal(456.0, asDouble);
        Assert.Equal(456L, asLong);
    }

    [Fact]
    public void ShouldThrowInvalidCastException_WhenUsingTaskContextValueGettingValueWithIncompatibleType()
    {
        // Arrange
        var value = TaskContextValue.From("not a number");

        // Act & Assert
        var exception = Assert.Throws<InvalidCastException>(
            () => value.GetValue<int>());
        Assert.Contains("Cannot convert task context value", exception.Message);
    }

    [Fact]
    public void ShouldReturnOriginalValue_WhenUsingTaskContextValueUsingRawValue()
    {
        // Arrange
        var original = new { name = "Context", value = 789 };
        var value = TaskContextValue.From(original);

        // Act
        var raw = value.RawValue;

        // Assert
        Assert.Same(original, raw);
    }

    [Fact]
    public void ShouldReturnCorrectType_WhenUsingTaskContextValueUsingValueType()
    {
        // Arrange
        var stringValue = TaskContextValue.From("test");
        var intValue = TaskContextValue.From(42);
        var listValue = TaskContextValue.From(new List<string>());

        // Act & Assert
        Assert.Equal(typeof(string), stringValue.ValueType);
        Assert.Equal(typeof(int), intValue.ValueType);
        Assert.Equal(typeof(List<string>), listValue.ValueType);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldFullTaskExecution_WhenUsingComplexScenario()
    {
        // Simulate a complete task execution context
        var context = TaskContext.Empty;

        // Initialize with inputs
        context = TaskContext.CreateBuilderFrom(context)
            .AddInput("dataSource", "customer_data.json")
            .AddInput("parameters", new { limit = 1000, filter = "active" })
            .AddInput("format", "json")
            .Build();

        // Add previous task results
        context = context.Set("previous_result", new
        {
            status = "completed",
            recordsProcessed = 850,
            errors = 0
        });

        // Add agent contexts
        context = TaskContext.CreateBuilderFrom(context)
            .AddAgentContext("data-reader", new { status = "ready", version = "2.1" })
            .AddAgentContext("processor", new { threads = 4, memory = "2GB" })
            .Build();

        // Add tool configurations
        context = TaskContext.CreateBuilderFrom(context)
            .AddTool("JsonParser", new { validation = true, schema = "v2" })
            .AddTool("DataValidator", new { strictMode = true, timeout = 30 })
            .Build();

        // Add memory context
        context = TaskContext.CreateBuilderFrom(context)
            .AddMemory("recentQueries", new List<string> { "query1", "query2" })
            .AddMemory("cache", new { size = "100MB", hitRate = 0.85 })
            .Build();

        // Add crew-level context
        context = TaskContext.CreateBuilderFrom(context)
            .AddCrewContext("executionMode", "parallel")
            .AddCrewContext("priority", "high")
            .Build();

        // Assert final context
        Assert.Equal(12, context.Count);
        Assert.Equal("customer_data.json", context.Get<string>("input.dataSource"));
        Assert.NotNull(context.Get<object>("input.parameters"));
        Assert.NotNull(context.Get<object>("previous_result"));
        Assert.NotNull(context.Get<object>("agent.data-reader"));
        Assert.NotNull(context.Get<object>("tool.JsonParser"));
        Assert.NotNull(context.Get<object>("memory.recentQueries"));
        Assert.Equal("parallel", context.Get<string>("crew.executionMode"));
    }

    [Fact]
    public void ShouldImmutabilityCheck_WhenUsingComplexScenario()
    {
        // Arrange
        var original = TaskContext.CreateBuilder()
            .AddInput("data", "original")
            .AddOutput("result", 100)
            .Build();

        // Act - Multiple modifications
        var modified1 = original.Set("input.data", "modified1");
        var modified2 = modified1.Set("output.result", 200);
        var modified3 = modified2.Set("new_context", "added");

        // Assert - Each instance is independent
        Assert.Equal(2, original.Count);
        Assert.Equal("original", original.Get<string>("input.data"));
        Assert.Equal(100, original.Get<int>("output.result"));

        Assert.Equal(2, modified1.Count);
        Assert.Equal("modified1", modified1.Get<string>("input.data"));
        Assert.Equal(100, modified1.Get<int>("output.result"));

        Assert.Equal(2, modified2.Count);
        Assert.Equal("modified1", modified2.Get<string>("input.data"));
        Assert.Equal(200, modified2.Get<int>("output.result"));

        Assert.Equal(3, modified3.Count);
        Assert.Equal("modified1", modified3.Get<string>("input.data"));
        Assert.Equal(200, modified3.Get<int>("output.result"));
        Assert.Equal("added", modified3.Get<string>("new_context"));
    }

    [Fact]
    public void ShouldMultiplePrefixedContexts_WhenUsingComplexScenario()
    {
        // Test various prefixed contexts
        var context = TaskContext.CreateBuilder()
            .AddInput("file1", "data1.csv")
            .AddInput("file2", "data2.csv")
            .AddOutput("count1", 500)
            .AddOutput("count2", 750)
            .AddAgentContext("reader1", "active")
            .AddAgentContext("reader2", "standby")
            .AddTool("CsvParser", "v1.0")
            .AddTool("DataMerger", "v2.0")
            .AddMemory("cache1", "enabled")
            .AddMemory("cache2", "disabled")
            .AddCrewContext("mode", "batch")
            .AddCrewContext("timeout", 3600)
            .Build();

        // Verify all prefixed contexts are accessible
        Assert.Equal("data1.csv", context.Get<string>("input.file1"));
        Assert.Equal("data2.csv", context.Get<string>("input.file2"));
        Assert.Equal(500, context.Get<int>("output.count1"));
        Assert.Equal(750, context.Get<int>("output.count2"));
        Assert.Equal("active", context.Get<string>("agent.reader1"));
        Assert.Equal("standby", context.Get<string>("agent.reader2"));
        Assert.Equal("v1.0", context.Get<string>("tool.CsvParser"));
        Assert.Equal("v2.0", context.Get<string>("tool.DataMerger"));
        Assert.Equal("enabled", context.Get<string>("memory.cache1"));
        Assert.Equal("disabled", context.Get<string>("memory.cache2"));
        Assert.Equal("batch", context.Get<string>("crew.mode"));
        Assert.Equal(3600, context.Get<int>("crew.timeout"));
    }

    [Fact]
    public void ShouldContextInheritanceAndModification_WhenUsingComplexScenario()
    {
        // Base context from previous task
        var baseContext = TaskContext.CreateBuilder()
            .AddInput("baseData", "foundation")
            .AddPreviousResult("success")
            .AddCrewContext("environment", "production")
            .Build();

        // Extend context for current task
        var extendedContext = TaskContext.CreateBuilderFrom(baseContext)
            .AddInput("currentData", "processing")
            .AddOutput("status", "in-progress")
            .AddAgentContext("current-agent", "working")
            .Build();

        // Further modification for specific operation
        var finalContext = extendedContext
            .Set("operation_id", Guid.NewGuid().ToString())
            .Set("timestamp", DateTime.UtcNow)
            .Set("input.currentData", "updated-processing");

        // Assert inheritance and modifications
        Assert.Equal(8, finalContext.Count);
        Assert.Equal("foundation", finalContext.Get<string>("input.baseData")); // Inherited
        Assert.Equal("success", finalContext.Get<string>("previous_result")); // Inherited
        Assert.Equal("production", finalContext.Get<string>("crew.environment")); // Inherited
        Assert.Equal("updated-processing", finalContext.Get<string>("input.currentData")); // Modified
        Assert.Equal("in-progress", finalContext.Get<string>("output.status")); // Added
        Assert.Equal("working", finalContext.Get<string>("agent.current-agent")); // Added
        Assert.NotNull(finalContext.Get<string>("operation_id")); // Added
        // DateTime is a value type, it's never null - removed Assert.NotNull
    }

    #endregion
}
