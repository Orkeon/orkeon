using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Domain.Tests.ValueObjects;

public class ToolArgumentsTests
{
    #region Empty Instance Tests

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var args = ToolArguments.Empty;

        // Assert
        Assert.NotNull(args);
        Assert.Equal(0, args.Count);
        Assert.Empty(args.Names);
        Assert.False(args.Contains("any"));
    }

    #endregion

    #region GetRequired Tests

    [Fact]
    public void ShouldReturnValue_WhenGettingRequiredWithExistingValue()
    {
        // Arrange
        var args = ToolArguments.CreateBuilder()
            .AddInt("count", 42)
            .Build();

        // Act
        var result = args.GetRequired<int>("count");

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenGettingRequiredWithNonExistentKey()
    {
        // Arrange
        var args = ToolArguments.Empty;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => args.GetRequired<int>("missing"));
        Assert.Contains("Required argument 'missing' not found", exception.Message);
    }

    [Fact]
    public void ShouldConvert_WhenGettingRequiredWithConvertibleValue()
    {
        // Arrange
        var args = ToolArguments.CreateBuilder()
            .AddDouble("rate", 3.14)
            .Build();

        // Act
        var result = args.GetRequired<double>("rate");

        // Assert
        Assert.Equal(3.14, result);
    }

    #endregion

    #region GetOptional Tests

    [Fact]
    public void ShouldReturnValue_WhenGettingOptionalWithExistingValue()
    {
        // Arrange
        var args = ToolArguments.CreateBuilder()
            .AddBool("enabled", true)
            .Build();

        // Act
        var result = args.GetOptional<bool>("enabled");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingOptionalWithNonExistentKey()
    {
        // Arrange
        var args = ToolArguments.Empty;

        // Act
        var result = args.GetOptional<int>("missing");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetRequiredString Tests

    [Fact]
    public void ShouldReturnValue_WhenGettingRequiredStringWithExistingString()
    {
        // Arrange
        var args = ToolArguments.CreateBuilder()
            .AddString("name", "test-value")
            .Build();

        // Act
        var result = args.GetRequiredString("name");

        // Assert
        Assert.Equal("test-value", result);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenGettingRequiredStringWithNonExistentKey()
    {
        // Arrange
        var args = ToolArguments.Empty;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => args.GetRequiredString("missing"));
        Assert.Contains("Required argument 'missing' not found", exception.Message);
    }

    #endregion

    #region GetOptionalString Tests

    [Fact]
    public void ShouldReturnValue_WhenGettingOptionalStringWithExistingString()
    {
        // Arrange
        var args = ToolArguments.CreateBuilder()
            .AddString("description", "optional description")
            .Build();

        // Act
        var result = args.GetOptionalString("description");

        // Assert
        Assert.Equal("optional description", result);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingOptionalStringWithNonExistentKey()
    {
        // Arrange
        var args = ToolArguments.Empty;

        // Act
        var result = args.GetOptionalString("missing");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetRequiredObject Tests

    [Fact]
    public void ShouldReturnValue_WhenGettingRequiredObjectWithExistingObject()
    {
        // Arrange
        var config = new { timeout = 30, retries = 3 };
        var args = ToolArguments.CreateBuilder()
            .AddObject("config", config)
            .Build();

        // Act
        var result = args.GetRequiredObject<object>("config");

        // Assert
        Assert.Same(config, result);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenGettingRequiredObjectWithNonExistentKey()
    {
        // Arrange
        var args = ToolArguments.Empty;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => args.GetRequiredObject<object>("missing"));
        Assert.Contains("Required argument 'missing' not found", exception.Message);
    }

    [Fact]
    public void ShouldReturnTypedValue_WhenGettingRequiredObjectWithSpecificType()
    {
        // Arrange
        var list = new List<string> { "item1", "item2" };
        var args = ToolArguments.CreateBuilder()
            .AddObject("items", list)
            .Build();

        // Act
        var result = args.GetRequiredObject<List<string>>("items");

        // Assert
        Assert.Same(list, result);
    }

    #endregion

    #region GetOptionalObject Tests

    [Fact]
    public void ShouldReturnValue_WhenGettingOptionalObjectWithExistingObject()
    {
        // Arrange
        var metadata = new { version = "1.0", author = "test" };
        var args = ToolArguments.CreateBuilder()
            .AddObject("metadata", metadata)
            .Build();

        // Act
        var result = args.GetOptionalObject<object>("metadata");

        // Assert
        Assert.Same(metadata, result);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingOptionalObjectWithNonExistentKey()
    {
        // Arrange
        var args = ToolArguments.Empty;

        // Act
        var result = args.GetOptionalObject<object>("missing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingOptionalObjectWithWrongType()
    {
        // Arrange
        var args = ToolArguments.CreateBuilder()
            .AddString("data", "string value")
            .Build();

        // Act
        var result = args.GetOptionalObject<List<string>>("data");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Contains Tests

    [Fact]
    public void ShouldReturnTrue_WhenUsingContainsWithExistingKey()
    {
        // Arrange
        var args = ToolArguments.CreateBuilder()
            .AddString("key", "value")
            .Build();

        // Act
        var result = args.Contains("key");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingContainsWithNonExistentKey()
    {
        // Arrange
        var args = ToolArguments.Empty;

        // Act
        var result = args.Contains("missing");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Names Property Tests

    [Fact]
    public void ShouldReturnEmpty_WhenUsingNamesWithEmptyArgs()
    {
        // Act
        var names = ToolArguments.Empty.Names.ToList();

        // Assert
        Assert.Empty(names);
    }

    [Fact]
    public void ShouldReturnAllNames_WhenUsingNamesWithMultipleArgs()
    {
        // Arrange
        var args = ToolArguments.CreateBuilder()
            .AddString("name", "value")
            .AddInt("count", 10)
            .AddBool("enabled", true)
            .Build();

        // Act
        var names = args.Names.ToList();

        // Assert
        Assert.Equal(3, names.Count);
        Assert.Contains("name", names);
        Assert.Contains("count", names);
        Assert.Contains("enabled", names);
    }

    #endregion

    #region Count Property Tests

    [Fact]
    public void ShouldReturnZero_WhenCountingWithEmptyArgs()
    {
        // Act
        var count = ToolArguments.Empty.Count;

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public void ShouldReturnCorrectCount_WhenCountingWithMultipleArgs()
    {
        // Arrange
        var args = ToolArguments.CreateBuilder()
            .AddString("arg1", "value1")
            .AddInt("arg2", 42)
            .AddBool("arg3", false)
            .AddDouble("arg4", 3.14)
            .Build();

        // Act
        var count = args.Count;

        // Assert
        Assert.Equal(4, count);
    }

    #endregion

    #region ToDictionary Tests

    [Fact]
    public void ShouldReturnEmptyDictionary_WhenUsingToDictionaryWithEmptyArgs()
    {
        // Act
        var dict = ToolArguments.Empty.ToDictionary();

        // Assert
        Assert.Empty(dict);
    }

    [Fact]
    public void ShouldReturnAllValues_WhenUsingToDictionaryWithMultipleArgs()
    {
        // Arrange
        var testObject = new { id = 1, name = "test" };
        var args = ToolArguments.CreateBuilder()
            .AddString("text", "sample")
            .AddInt("number", 123)
            .AddBool("flag", true)
            .AddDouble("rate", 2.5)
            .AddObject("data", testObject)
            .Build();

        // Act
        var dict = args.ToDictionary();

        // Assert
        Assert.Equal(5, dict.Count);
        Assert.Equal("sample", dict["text"]);
        Assert.Equal(123, dict["number"]);
        Assert.True((bool)dict["flag"]);
        Assert.Equal(2.5, dict["rate"]);
        Assert.Same(testObject, dict["data"]);
    }

    #endregion

    #region Builder Tests

    [Fact]
    public void ShouldReturnBuilder_WhenUsingCreateBuilder()
    {
        // Act
        var builder = ToolArguments.CreateBuilder();

        // Assert
        Assert.NotNull(builder);
    }

    [Fact]
    public void ShouldAddStringArgument_WhenUsingBuilderAddString()
    {
        // Act
        var args = ToolArguments.CreateBuilder()
            .AddString("message", "Hello World")
            .Build();

        // Assert
        Assert.Equal("Hello World", args.GetRequiredString("message"));
    }

    [Fact]
    public void ShouldAddIntArgument_WhenUsingBuilderAddInt()
    {
        // Act
        var args = ToolArguments.CreateBuilder()
            .AddInt("iterations", 1000)
            .Build();

        // Assert
        Assert.Equal(1000, args.GetRequired<int>("iterations"));
    }

    [Fact]
    public void ShouldAddBoolArgument_WhenUsingBuilderAddBool()
    {
        // Act
        var args = ToolArguments.CreateBuilder()
            .AddBool("verbose", false)
            .Build();

        // Assert
        Assert.False(args.GetRequired<bool>("verbose"));
    }

    [Fact]
    public void ShouldAddDoubleArgument_WhenUsingBuilderAddDouble()
    {
        // Act
        var args = ToolArguments.CreateBuilder()
            .AddDouble("threshold", 0.95)
            .Build();

        // Assert
        Assert.Equal(0.95, args.GetRequired<double>("threshold"));
    }

    [Fact]
    public void ShouldAddObjectArgument_WhenUsingBuilderAddObject()
    {
        // Arrange
        var settings = new { theme = "dark", fontSize = 14 };

        // Act
        var args = ToolArguments.CreateBuilder()
            .AddObject("settings", settings)
            .Build();

        // Assert
        Assert.Same(settings, args.GetRequiredObject<object>("settings"));
    }

    [Fact]
    public void ShouldAddAllArguments_WhenUsingBuilderChainedCalls()
    {
        // Arrange
        var config = new { maxRetries = 3, timeout = 30 };

        // Act
        var args = ToolArguments.CreateBuilder()
            .AddString("operation", "process")
            .AddInt("batchSize", 100)
            .AddBool("strictMode", true)
            .AddDouble("errorRate", 0.01)
            .AddObject("config", config)
            .Build();

        // Assert
        Assert.Equal("process", args.GetRequiredString("operation"));
        Assert.Equal(100, args.GetRequired<int>("batchSize"));
        Assert.True(args.GetRequired<bool>("strictMode"));
        Assert.Equal(0.01, args.GetRequired<double>("errorRate"));
        Assert.Same(config, args.GetRequiredObject<object>("config"));
        Assert.Equal(5, args.Count);
    }

    [Fact]
    public void ShouldKeepLastValue_WhenUsingBuilderOverwriteExistingKey()
    {
        // Act
        var args = ToolArguments.CreateBuilder()
            .AddString("key", "first")
            .AddString("key", "second")
            .Build();

        // Assert
        Assert.Equal("second", args.GetRequiredString("key"));
    }

    [Fact]
    public void ShouldReturnImmutableInstance_WhenUsingBuilderBuild()
    {
        // Arrange
        var builder = ToolArguments.CreateBuilder()
            .AddString("test", "value");

        // Act
        var args1 = builder.Build();
        var args2 = builder.Build();

        // Assert
        Assert.NotSame(args1, args2);
        Assert.Equal("value", args1.GetRequiredString("test"));
        Assert.Equal("value", args2.GetRequiredString("test"));
    }

    #endregion

    #region ToolArgumentValue Tests

    [Fact]
    public void ShouldCreateStringValue_WhenUsingToolArgumentValueFromString()
    {
        // Act
        var value = ToolArgumentValue.FromString("test string");

        // Assert
        Assert.Equal("test string", value.RawValue);
        Assert.Equal(typeof(string), value.ValueType);
    }

    [Fact]
    public void ShouldCreateIntValue_WhenUsingToolArgumentValueFromInt()
    {
        // Act
        var value = ToolArgumentValue.FromInt(42);

        // Assert
        Assert.Equal(42, value.RawValue);
        Assert.Equal(typeof(int), value.ValueType);
    }

    [Fact]
    public void ShouldCreateBoolValue_WhenUsingToolArgumentValueFromBool()
    {
        // Act
        var value = ToolArgumentValue.FromBool(true);

        // Assert
        Assert.True((bool)value.RawValue);
        Assert.Equal(typeof(bool), value.ValueType);
    }

    [Fact]
    public void ShouldCreateDoubleValue_WhenUsingToolArgumentValueFromDouble()
    {
        // Act
        var value = ToolArgumentValue.FromDouble(3.14);

        // Assert
        Assert.Equal(3.14, value.RawValue);
        Assert.Equal(typeof(double), value.ValueType);
    }

    [Fact]
    public void ShouldCreateObjectValue_WhenUsingToolArgumentValueFromObject()
    {
        // Arrange
        var obj = new { name = "test", id = 123 };

        // Act
        var value = ToolArgumentValue.FromObject(obj);

        // Assert
        Assert.Same(obj, value.RawValue);
        Assert.Equal(obj.GetType(), value.ValueType);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingToolArgumentValueGettingValueWithCorrectType()
    {
        // Arrange
        var value = ToolArgumentValue.FromInt(789);

        // Act
        var result = value.GetValue<int>();

        // Assert
        Assert.Equal(789, result);
    }

    [Fact]
    public void ShouldConvert_WhenUsingToolArgumentValueGettingValueWithConvertibleType()
    {
        // Arrange
        var value = ToolArgumentValue.FromInt(100);

        // Act
        var asDouble = value.GetValue<double>();
        var asLong = value.GetValue<long>();

        // Assert
        Assert.Equal(100.0, asDouble);
        Assert.Equal(100L, asLong);
    }

    [Fact]
    public void ShouldThrowInvalidCastException_WhenUsingToolArgumentValueGettingValueWithIncompatibleType()
    {
        // Arrange
        var value = ToolArgumentValue.FromString("not a number");

        // Act & Assert
        var exception = Assert.Throws<InvalidCastException>(() => value.GetValue<int>());
        Assert.Contains("Cannot convert value of type String to Int32", exception.Message);
    }

    [Fact]
    public void ShouldReturnString_WhenUsingToolArgumentValueGettingStringValueWithString()
    {
        // Arrange
        var value = ToolArgumentValue.FromString("original string");

        // Act
        var result = value.GetStringValue();

        // Assert
        Assert.Equal("original string", result);
    }

    [Fact]
    public void ShouldReturnToString_WhenUsingToolArgumentValueGettingStringValueWithNonString()
    {
        // Arrange
        var intValue = ToolArgumentValue.FromInt(456);
        var boolValue = ToolArgumentValue.FromBool(true);
        var doubleValue = ToolArgumentValue.FromDouble(2.71);

        // Act
        var intString = intValue.GetStringValue();
        var boolString = boolValue.GetStringValue();
        var doubleString = doubleValue.GetStringValue();

        // Assert
        Assert.Equal("456", intString);
        Assert.Equal("True", boolString);
        Assert.Equal("2.71", doubleString);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingToolArgumentValueGettingObjectValueWithCorrectType()
    {
        // Arrange
        var list = new List<int> { 1, 2, 3 };
        var value = ToolArgumentValue.FromObject(list);

        // Act
        var result = value.GetObjectValue<List<int>>();

        // Assert
        Assert.Same(list, result);
    }

    [Fact]
    public void ShouldThrowInvalidCastException_WhenUsingToolArgumentValueGettingObjectValueWithWrongType()
    {
        // Arrange
        var value = ToolArgumentValue.FromString("not a list");

        // Act & Assert
        var exception = Assert.Throws<InvalidCastException>(() => value.GetObjectValue<List<string>>());
        Assert.Contains("Cannot convert value of type String to List`1", exception.Message);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingToolArgumentValueUsingTryGetObjectValueWithCorrectType()
    {
        // Arrange
        var dict = new Dictionary<string, int> { { "key", 42 } };
        var value = ToolArgumentValue.FromObject(dict);

        // Act
        var result = value.TryGetObjectValue<Dictionary<string, int>>();

        // Assert
        Assert.Same(dict, result);
    }

    [Fact]
    public void ShouldReturnNull_WhenUsingToolArgumentValueUsingTryGetObjectValueWithWrongType()
    {
        // Arrange
        var value = ToolArgumentValue.FromString("not a dictionary");

        // Act
        var result = value.TryGetObjectValue<Dictionary<string, int>>();

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithEmptyStringArgument()
    {
        // Act
        var args = ToolArguments.CreateBuilder()
            .AddString("empty", "")
            .Build();

        // Assert
        Assert.Equal("", args.GetRequiredString("empty"));
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithNegativeNumbers()
    {
        // Act
        var args = ToolArguments.CreateBuilder()
            .AddInt("negInt", -100)
            .AddDouble("negDouble", -2.5)
            .Build();

        // Assert
        Assert.Equal(-100, args.GetRequired<int>("negInt"));
        Assert.Equal(-2.5, args.GetRequired<double>("negDouble"));
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithZeroValues()
    {
        // Act
        var args = ToolArguments.CreateBuilder()
            .AddInt("zero", 0)
            .AddDouble("zeroDouble", 0.0)
            .Build();

        // Assert
        Assert.Equal(0, args.GetRequired<int>("zero"));
        Assert.Equal(0.0, args.GetRequired<double>("zeroDouble"));
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithSpecialDoubleValues()
    {
        // Act
        var args = ToolArguments.CreateBuilder()
            .AddDouble("infinity", double.PositiveInfinity)
            .AddDouble("negInfinity", double.NegativeInfinity)
            .AddDouble("nan", double.NaN)
            .Build();

        // Assert
        Assert.Equal(double.PositiveInfinity, args.GetRequired<double>("infinity"));
        Assert.Equal(double.NegativeInfinity, args.GetRequired<double>("negInfinity"));
        Assert.True(double.IsNaN(args.GetRequired<double>("nan")));
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithMinMaxValues()
    {
        // Act
        var args = ToolArguments.CreateBuilder()
            .AddInt("maxInt", int.MaxValue)
            .AddInt("minInt", int.MinValue)
            .AddDouble("maxDouble", double.MaxValue)
            .AddDouble("minDouble", double.MinValue)
            .Build();

        // Assert
        Assert.Equal(int.MaxValue, args.GetRequired<int>("maxInt"));
        Assert.Equal(int.MinValue, args.GetRequired<int>("minInt"));
        Assert.Equal(double.MaxValue, args.GetRequired<double>("maxDouble"));
        Assert.Equal(double.MinValue, args.GetRequired<double>("minDouble"));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingEdgeCaseWithNullObjectValue()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            ToolArgumentValue.FromObject<string>(null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingEdgeCaseWithNullStringValue()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            ToolArgumentValue.FromString(null!));
        Assert.Equal("value", exception.ParamName);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldFileProcessingToolArguments_WhenUsingComplexScenario()
    {
        // Simulate arguments for a file processing tool
        var processingOptions = new
        {
            encoding = "UTF-8",
            bufferSize = 8192,
            parallel = true,
            filters = new[] { "*.txt", "*.csv" }
        };

        var args = ToolArguments.CreateBuilder()
            .AddString("inputPath", "/data/input")
            .AddString("outputPath", "/data/output")
            .AddString("operation", "transform")
            .AddInt("maxFiles", 1000)
            .AddInt("timeout", 300)
            .AddBool("recursive", true)
            .AddBool("overwrite", false)
            .AddDouble("errorThreshold", 0.1)
            .AddDouble("compressionRatio", 0.8)
            .AddObject("options", processingOptions)
            .Build();

        // Assert all arguments are accessible
        Assert.Equal("/data/input", args.GetRequiredString("inputPath"));
        Assert.Equal("/data/output", args.GetRequiredString("outputPath"));
        Assert.Equal("transform", args.GetRequiredString("operation"));
        Assert.Equal(1000, args.GetRequired<int>("maxFiles"));
        Assert.Equal(300, args.GetRequired<int>("timeout"));
        Assert.True(args.GetRequired<bool>("recursive"));
        Assert.False(args.GetRequired<bool>("overwrite"));
        Assert.Equal(0.1, args.GetRequired<double>("errorThreshold"));
        Assert.Equal(0.8, args.GetRequired<double>("compressionRatio"));
        Assert.Same(processingOptions, args.GetRequiredObject<object>("options"));

        // Verify optional access works
        Assert.Equal("/data/input", args.GetOptionalString("inputPath"));
        Assert.Equal(1000, args.GetOptional<int>("maxFiles"));
        Assert.Null(args.GetOptionalString("nonExistent"));

        // Verify contains and count
        Assert.True(args.Contains("inputPath"));
        Assert.False(args.Contains("missing"));
        Assert.Equal(10, args.Count);
    }

    [Fact]
    public void ShouldApiCallToolArguments_WhenUsingComplexScenario()
    {
        // Simulate arguments for an HTTP API call tool
        var headers = new Dictionary<string, string>
        {
            { "Authorization", "Bearer token123" },
            { "Content-Type", "application/json" },
            { "User-Agent", "Orkeon-Tool/1.0" }
        };

        var payload = new
        {
            query = "SELECT * FROM users",
            parameters = new { limit = 100, offset = 0 },
            options = new { format = "json", compressed = true }
        };

        var args = ToolArguments.CreateBuilder()
            .AddString(ParamUrl, "https://api.example.com/query")
            .AddString("method", "POST")
            .AddString("contentType", "application/json")
            .AddInt("timeoutSeconds", 30)
            .AddInt("maxRetries", 3)
            .AddBool("followRedirects", true)
            .AddBool("validateSsl", true)
            .AddDouble("retryDelayMultiplier", 1.5)
            .AddObject("headers", headers)
            .AddObject("body", payload)
            .Build();

        // Test required access
        Assert.Equal("https://api.example.com/query", args.GetRequiredString(ParamUrl));
        Assert.Equal("POST", args.GetRequiredString("method"));
        Assert.Equal(30, args.GetRequired<int>("timeoutSeconds"));
        Assert.True(args.GetRequired<bool>("followRedirects"));
        Assert.Equal(1.5, args.GetRequired<double>("retryDelayMultiplier"));

        // Test object access with specific types
        var retrievedHeaders = args.GetRequiredObject<Dictionary<string, string>>("headers");
        Assert.Same(headers, retrievedHeaders);
        Assert.Equal("Bearer token123", retrievedHeaders["Authorization"]);

        var retrievedPayload = args.GetRequiredObject<object>("body");
        Assert.Same(payload, retrievedPayload);

        // Test optional access
        Assert.Equal("application/json", args.GetOptionalString("contentType"));
        Assert.Null(args.GetOptionalString("proxy"));

        // Verify dictionary conversion for serialization
        var dict = args.ToDictionary();
        Assert.Equal(10, dict.Count);
        Assert.Equal("https://api.example.com/query", dict[ParamUrl]);
        Assert.Equal(30, dict["timeoutSeconds"]);
        Assert.True((bool)dict["followRedirects"]);
        Assert.Same(headers, dict["headers"]);
    }

    [Fact]
    public void ShouldDatabaseToolArguments_WhenUsingComplexScenario()
    {
        // Simulate arguments for a database operation tool
        var connectionSettings = new
        {
            server = "localhost",
            database = "orkeon",
            integratedSecurity = true,
            connectionTimeout = 30,
            commandTimeout = 120,
            pooling = true,
            maxPoolSize = 100
        };

        var args = ToolArguments.CreateBuilder()
            .AddString("operation", "ExecuteQuery")
            .AddString(ParamQuery, "SELECT id, name, email FROM users WHERE active = @active")
            .AddString("connectionString", "Server=localhost;Database=orkeon;Integrated Security=true;")
            .AddInt("expectedRows", 500)
            .AddBool("useTransaction", true)
            .AddBool("enableLogging", false)
            .AddDouble("queryTimeout", 60.0)
            .AddObject("settings", connectionSettings)
            .AddObject("parameters", new Dictionary<string, object> { { "@active", true } })
            .Build();

        // Test all argument types
        Assert.Equal("ExecuteQuery", args.GetRequiredString("operation"));
        Assert.Contains("SELECT id, name, email", args.GetRequiredString(ParamQuery));
        Assert.Equal(500, args.GetRequired<int>("expectedRows"));
        Assert.True(args.GetRequired<bool>("useTransaction"));
        Assert.False(args.GetRequired<bool>("enableLogging"));
        Assert.Equal(60.0, args.GetRequired<double>("queryTimeout"));

        // Test complex objects
        var settings = args.GetRequiredObject<object>("settings");
        Assert.Same(connectionSettings, settings);

        var parameters = args.GetRequiredObject<Dictionary<string, object>>("parameters");
        Assert.True((bool)parameters["@active"]);

        // Test optional access scenarios
        Assert.Equal("ExecuteQuery", args.GetOptionalString("operation"));
        Assert.Null(args.GetOptionalString("alternativeQuery"));
        Assert.Equal(500, args.GetOptional<int>("expectedRows"));
        Assert.Null(args.GetOptional<int>("maxRetries"));

        // Verify argument management
        Assert.Equal(9, args.Count);
        Assert.True(args.Contains(ParamQuery));
        Assert.False(args.Contains("nonExistent"));

        var names = args.Names.ToList();
        Assert.Contains("operation", names);
        Assert.Contains("settings", names);
        Assert.Contains("parameters", names);
    }

    #endregion
}
