using Orkeon.Domain.Memory.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

public class ToolUsageMetadataTests
{
    private static readonly string[] TwoValidationErrors = ["validation error 1", "validation error 2"];
    private static readonly string[] ThreeErrors = ["Invalid input format", "Missing required field", "Value out of range"];
    private static readonly string[] TimeoutError = ["timeout error"];
    private static readonly string[] AbArray = ["a", "b"];
    private static readonly string[] s_timeoutError = ["Timeout on first attempt"];
    private static readonly string[] s_timeoutAndRetryErrors = ["Timeout on first attempt", "Recovered on retry"];

    #region Empty Instance Tests

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var metadata = ToolUsageMetadata.Empty;

        // Assert
        Assert.NotNull(metadata);
        Assert.Empty(metadata.Keys);
        Assert.Null(metadata.Get<string>("any"));
        Assert.False(metadata.ContainsKey("any"));
    }

    #endregion

    #region Get Method Tests

    [Fact]
    public void ShouldReturnValue_WhenGettingWithExistingKey()
    {
        // Arrange
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddInput("test input data")
            .AddRetryCount(3)
            .Build();

        // Act
        var input = metadata.Get<string>("input");
        var retryCount = metadata.Get<int>("retry_count");

        // Assert
        Assert.Equal("test input data", input);
        Assert.Equal(3, retryCount);
    }

    [Fact]
    public void ShouldReturnDefault_WhenGettingWithNonExistentKey()
    {
        // Arrange
        var metadata = ToolUsageMetadata.Empty;

        // Act
        var stringValue = metadata.Get<string>("missing");
        var intValue = metadata.Get<int>("missing");
        var boolValue = metadata.Get<bool>("missing");

        // Assert
        Assert.Null(stringValue);
        Assert.Equal(0, intValue);
        Assert.False(boolValue);
    }

    [Fact]
    public void ShouldConvert_WhenGettingWithTypeConversion()
    {
        // Arrange
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddRetryCount(42)
            .Build();

        // Act
        var asString = metadata.Get<string>("retry_count");
        var asDouble = metadata.Get<double>("retry_count");
        var asLong = metadata.Get<long>("retry_count");

        // Assert
        Assert.Equal("42", asString);
        Assert.Equal(42.0, asDouble);
        Assert.Equal(42L, asLong);
    }

    #endregion

    #region GetRequired Method Tests

    [Fact]
    public void ShouldReturnValue_WhenGettingRequiredWithExistingKey()
    {
        // Arrange
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddOutput("processing complete")
            .AddCacheHit(true)
            .Build();

        // Act
        var output = metadata.GetRequired<string>("output");
        var cacheHit = metadata.GetRequired<bool>("cache_hit");

        // Assert
        Assert.Equal("processing complete", output);
        Assert.True(cacheHit);
    }

    [Fact]
    public void ShouldThrowKeyNotFoundException_WhenGettingRequiredWithNonExistentKey()
    {
        // Arrange
        var metadata = ToolUsageMetadata.Empty;

        // Act & Assert
        var exception = Assert.Throws<KeyNotFoundException>(() => metadata.GetRequired<string>("missing"));
        Assert.Contains("Required metadata key 'missing' not found", exception.Message);
    }

    [Fact]
    public void ShouldConvert_WhenGettingRequiredWithTypeConversion()
    {
        // Arrange
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddRetryCount(100)
            .Build();

        // Act
        var asDouble = metadata.GetRequired<double>("retry_count");

        // Assert
        Assert.Equal(100.0, asDouble);
    }

    #endregion

    #region ContainsKey Method Tests

    [Fact]
    public void ShouldReturnTrue_WhenUsingContainsKeyWithExistingKey()
    {
        // Arrange
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddExecutionContext("background-task")
            .Build();

        // Act
        var result = metadata.ContainsKey("execution_context");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingContainsKeyWithNonExistentKey()
    {
        // Arrange
        var metadata = ToolUsageMetadata.Empty;

        // Act
        var result = metadata.ContainsKey("missing");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Keys Property Tests

    [Fact]
    public void ShouldReturnEmpty_WhenUsingKeysWithEmptyMetadata()
    {
        // Act
        var keys = ToolUsageMetadata.Empty.Keys.ToList();

        // Assert
        Assert.Empty(keys);
    }

    [Fact]
    public void ShouldReturnAllKeys_WhenUsingKeysWithMultipleMetadata()
    {
        // Arrange
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddInput("data")
            .AddOutput("result")
            .AddRetryCount(2)
            .AddCacheHit(false)
            .Build();

        // Act
        var keys = metadata.Keys.ToList();

        // Assert
        Assert.Equal(4, keys.Count);
        Assert.Contains("input", keys);
        Assert.Contains("output", keys);
        Assert.Contains("retry_count", keys);
        Assert.Contains("cache_hit", keys);
    }

    #endregion

    #region ToDictionary Method Tests

    [Fact]
    public void ShouldReturnEmptyDictionary_WhenUsingToDictionaryWithEmptyMetadata()
    {
        // Act
        var dict = ToolUsageMetadata.Empty.ToDictionary();

        // Assert
        Assert.Empty(dict);
    }

    [Fact]
    public void ShouldReturnAllValues_WhenUsingToDictionaryWithMultipleMetadata()
    {
        // Arrange
        var resources = new Dictionary<string, double> { { "memory", 128.5 }, { "cpu", 45.2 } };
        var errors = TwoValidationErrors;

        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddInput("test data")
            .AddOutput("processed result")
            .AddRetryCount(1)
            .AddCacheHit(true)
            .AddResourcesUsed(resources)
            .AddValidationErrors(errors)
            .Build();

        // Act
        var dict = metadata.ToDictionary();

        // Assert
        Assert.Equal(6, dict.Count);
        Assert.Equal("test data", dict["input"]);
        Assert.Equal("processed result", dict["output"]);
        Assert.Equal(1, dict["retry_count"]);
        Assert.True((bool)dict["cache_hit"]);
        Assert.Same(resources, dict["resources_used"]);
        Assert.Same(errors, dict["validation_errors"]);
    }

    #endregion

    #region FromDictionary Method Tests

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithNull()
    {
        // Act
        var metadata = ToolUsageMetadata.FromDictionary(null);

        // Assert
        Assert.Same(ToolUsageMetadata.Empty, metadata);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithEmptyDictionary()
    {
        // Act
        var metadata = ToolUsageMetadata.FromDictionary(new Dictionary<string, object>());

        // Assert
        Assert.Same(ToolUsageMetadata.Empty, metadata);
    }

    [Fact]
    public void ShouldCreateMetadata_WhenUsingFromDictionaryWithValues()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "input", "source data" },
            { "output", "transformed data" },
            { "retry_count", 2 },
            { "cache_hit", false },
            { "custom_key", "custom_value" }
        };

        // Act
        var metadata = ToolUsageMetadata.FromDictionary(dict);

        // Assert
        Assert.Equal(5, metadata.Keys.Count());
        Assert.Equal("source data", metadata.Get<string>("input"));
        Assert.Equal("transformed data", metadata.Get<string>("output"));
        Assert.Equal(2, metadata.Get<int>("retry_count"));
        Assert.False(metadata.Get<bool>("cache_hit"));
        Assert.Equal("custom_value", metadata.Get<string>("custom_key"));
    }

    [Fact]
    public void ShouldPreserveValues_WhenUsingRoundTripToDictionaryFromDictionary()
    {
        // Arrange
        var originalMetrics = new Dictionary<string, double> { { "latency", 50.5 }, { "throughput", 1000.0 } };
        var original = ToolUsageMetadata.CreateBuilder()
            .AddInput("original input")
            .AddRetryCount(3)
            .AddCacheHit(true)
            .AddPerformanceMetrics(originalMetrics)
            .Build();

        // Act
        var dict = original.ToDictionary();
        var restored = ToolUsageMetadata.FromDictionary(dict);

        // Assert
        Assert.Equal(original.Keys.Count(), restored.Keys.Count());
        Assert.Equal("original input", restored.Get<string>("input"));
        Assert.Equal(3, restored.Get<int>("retry_count"));
        Assert.True(restored.Get<bool>("cache_hit"));
        Assert.Same(originalMetrics, restored.Get<Dictionary<string, double>>("performance_metrics"));
    }

    #endregion

    #region Builder Tests

    [Fact]
    public void ShouldReturnBuilder_WhenUsingCreateBuilder()
    {
        // Act
        var builder = ToolUsageMetadata.CreateBuilder();

        // Assert
        Assert.NotNull(builder);
    }

    [Fact]
    public void ShouldSetInputKey_WhenUsingBuilderAddInput()
    {
        // Act
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddInput("user query data")
            .Build();

        // Assert
        Assert.Equal("user query data", metadata.Get<string>("input"));
    }

    [Fact]
    public void ShouldSetOutputKey_WhenUsingBuilderAddOutput()
    {
        // Act
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddOutput("analysis results")
            .Build();

        // Assert
        Assert.Equal("analysis results", metadata.Get<string>("output"));
    }

    [Fact]
    public void ShouldSetRetryCountKey_WhenUsingBuilderAddRetryCount()
    {
        // Act
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddRetryCount(5)
            .Build();

        // Assert
        Assert.Equal(5, metadata.Get<int>("retry_count"));
    }

    [Fact]
    public void ShouldSetCacheHitKey_WhenUsingBuilderAddCacheHit()
    {
        // Act
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddCacheHit(true)
            .Build();

        // Assert
        Assert.True(metadata.Get<bool>("cache_hit"));
    }

    [Fact]
    public void ShouldSetResourcesUsedKey_WhenUsingBuilderAddResourcesUsed()
    {
        // Arrange
        var resources = new Dictionary<string, double>
        {
            { "memory_mb", 256.0 },
            { "cpu_percent", 35.5 },
            { "disk_io_mb", 12.8 }
        };

        // Act
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddResourcesUsed(resources)
            .Build();

        // Assert
        Assert.Same(resources, metadata.Get<Dictionary<string, double>>("resources_used"));
    }

    [Fact]
    public void ShouldSetExecutionContextKey_WhenUsingBuilderAddExecutionContext()
    {
        // Act
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddExecutionContext("batch-processing")
            .Build();

        // Assert
        Assert.Equal("batch-processing", metadata.Get<string>("execution_context"));
    }

    [Fact]
    public void ShouldSetValidationErrorsKey_WhenUsingBuilderAddValidationErrors()
    {
        // Arrange
        var errors = ThreeErrors;

        // Act
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddValidationErrors(errors)
            .Build();

        // Assert
        Assert.Same(errors, metadata.Get<string[]>("validation_errors"));
    }

    [Fact]
    public void ShouldSetPerformanceMetricsKey_WhenUsingBuilderAddPerformanceMetrics()
    {
        // Arrange
        var metrics = new Dictionary<string, double>
        {
            { "response_time_ms", 125.5 },
            { "requests_per_second", 850.0 },
            { "error_rate_percent", 0.5 }
        };

        // Act
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddPerformanceMetrics(metrics)
            .Build();

        // Assert
        Assert.Same(metrics, metadata.Get<Dictionary<string, double>>("performance_metrics"));
    }

    [Fact]
    public void ShouldSetCustomKey_WhenUsingBuilderAdd()
    {
        // Arrange
        var customData = new { version = "1.0", timestamp = DateTime.UtcNow };

        // Act
        var metadata = ToolUsageMetadata.CreateBuilder()
            .Add("custom_metadata", customData)
            .Add("string_value", "test")
            .Add("numeric_value", 42)
            .Build();

        // Assert
        Assert.Same(customData, metadata.Get<object>("custom_metadata"));
        Assert.Equal("test", metadata.Get<string>("string_value"));
        Assert.Equal(42, metadata.Get<int>("numeric_value"));
    }

    [Fact]
    public void ShouldAddAllMetadata_WhenUsingBuilderChainedCalls()
    {
        // Arrange
        var resources = new Dictionary<string, double> { { "memory", 512.0 } };
        var errors = TimeoutError;
        var metrics = new Dictionary<string, double> { { "latency", 25.0 } };

        // Act
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddInput("complex input data")
            .AddOutput("transformed output")
            .AddRetryCount(2)
            .AddCacheHit(false)
            .AddResourcesUsed(resources)
            .AddExecutionContext("production")
            .AddValidationErrors(errors)
            .AddPerformanceMetrics(metrics)
            .Add("custom", "value")
            .Build();

        // Assert
        Assert.Equal(9, metadata.Keys.Count());
        Assert.Equal("complex input data", metadata.Get<string>("input"));
        Assert.Equal("transformed output", metadata.Get<string>("output"));
        Assert.Equal(2, metadata.Get<int>("retry_count"));
        Assert.False(metadata.Get<bool>("cache_hit"));
        Assert.Same(resources, metadata.Get<Dictionary<string, double>>("resources_used"));
        Assert.Equal("production", metadata.Get<string>("execution_context"));
        Assert.Same(errors, metadata.Get<string[]>("validation_errors"));
        Assert.Same(metrics, metadata.Get<Dictionary<string, double>>("performance_metrics"));
        Assert.Equal("value", metadata.Get<string>("custom"));
    }

    [Fact]
    public void ShouldKeepLastValue_WhenUsingBuilderOverwriteExistingKey()
    {
        // Act
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddInput("first input")
            .AddInput("second input")
            .Build();

        // Assert
        Assert.Equal("second input", metadata.Get<string>("input"));
    }

    [Fact]
    public void ShouldReturnImmutableInstance_WhenUsingBuilderBuild()
    {
        // Arrange
        var builder = ToolUsageMetadata.CreateBuilder()
            .AddInput("test");

        // Act
        var metadata1 = builder.Build();
        var metadata2 = builder.Build();

        // Assert
        Assert.NotSame(metadata1, metadata2);
        Assert.Equal("test", metadata1.Get<string>("input"));
        Assert.Equal("test", metadata2.Get<string>("input"));
    }

    #endregion

    #region ToolUsageMetadataValue Tests

    [Fact]
    public void ShouldCreate_WhenUsingToolUsageMetadataValueFromWithValidValue()
    {
        // Act
        var stringValue = ToolUsageMetadataValue.From("test");
        var intValue = ToolUsageMetadataValue.From(42);
        var boolValue = ToolUsageMetadataValue.From(true);
        var objectValue = ToolUsageMetadataValue.From(new { id = 1 });

        // Assert
        Assert.NotNull(stringValue);
        Assert.NotNull(intValue);
        Assert.NotNull(boolValue);
        Assert.NotNull(objectValue);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToolUsageMetadataValueFromWithNull()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => ToolUsageMetadataValue.From(null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingToolUsageMetadataValueGettingValueWithCorrectType()
    {
        // Arrange
        var value = ToolUsageMetadataValue.From("metadata value");

        // Act
        var result = value.GetValue<string>();

        // Assert
        Assert.Equal("metadata value", result);
    }

    [Fact]
    public void ShouldConvert_WhenUsingToolUsageMetadataValueGettingValueWithConvertibleType()
    {
        // Arrange
        var value = ToolUsageMetadataValue.From(123);

        // Act
        var asString = value.GetValue<string>();
        var asDouble = value.GetValue<double>();
        var asLong = value.GetValue<long>();

        // Assert
        Assert.Equal("123", asString);
        Assert.Equal(123.0, asDouble);
        Assert.Equal(123L, asLong);
    }

    [Fact]
    public void ShouldThrowInvalidCastException_WhenUsingToolUsageMetadataValueGettingValueWithIncompatibleType()
    {
        // Arrange
        var value = ToolUsageMetadataValue.From("not a number");

        // Act & Assert
        var exception = Assert.Throws<InvalidCastException>(() => value.GetValue<int>());
        Assert.Contains("Cannot convert tool usage metadata value of type String to Int32", exception.Message);
    }

    [Fact]
    public void ShouldReturnOriginalValue_WhenUsingToolUsageMetadataValueUsingRawValue()
    {
        // Arrange
        var original = new { name = "Metadata", count = 456 };
        var value = ToolUsageMetadataValue.From(original);

        // Act
        var raw = value.RawValue;

        // Assert
        Assert.Same(original, raw);
    }

    [Fact]
    public void ShouldReturnCorrectType_WhenUsingToolUsageMetadataValueUsingValueType()
    {
        // Arrange
        var stringValue = ToolUsageMetadataValue.From("test");
        var intValue = ToolUsageMetadataValue.From(42);
        var arrayValue = ToolUsageMetadataValue.From(AbArray);

        // Act & Assert
        Assert.Equal(typeof(string), stringValue.ValueType);
        Assert.Equal(typeof(int), intValue.ValueType);
        Assert.Equal(typeof(string[]), arrayValue.ValueType);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithEmptyStringValues()
    {
        // Act
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddInput("")
            .AddOutput("")
            .AddExecutionContext("")
            .Build();

        // Assert
        Assert.Equal("", metadata.Get<string>("input"));
        Assert.Equal("", metadata.Get<string>("output"));
        Assert.Equal("", metadata.Get<string>("execution_context"));
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithNegativeRetryCount()
    {
        // Act
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddRetryCount(-1)
            .Build();

        // Assert
        Assert.Equal(-1, metadata.Get<int>("retry_count"));
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithZeroRetryCount()
    {
        // Act
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddRetryCount(0)
            .Build();

        // Assert
        Assert.Equal(0, metadata.Get<int>("retry_count"));
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithEmptyCollections()
    {
        // Arrange
        var emptyResources = new Dictionary<string, double>();
        var emptyErrors = Array.Empty<string>();
        var emptyMetrics = new Dictionary<string, double>();

        // Act
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddResourcesUsed(emptyResources)
            .AddValidationErrors(emptyErrors)
            .AddPerformanceMetrics(emptyMetrics)
            .Build();

        // Assert
        Assert.Same(emptyResources, metadata.Get<Dictionary<string, double>>("resources_used"));
        Assert.Same(emptyErrors, metadata.Get<string[]>("validation_errors"));
        Assert.Same(emptyMetrics, metadata.Get<Dictionary<string, double>>("performance_metrics"));
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithSpecialDoubleValues()
    {
        // Arrange
        var specialMetrics = new Dictionary<string, double>
        {
            { "infinity", double.PositiveInfinity },
            { "neg_infinity", double.NegativeInfinity },
            { "nan", double.NaN },
            { "max", double.MaxValue },
            { "min", double.MinValue }
        };

        // Act
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddPerformanceMetrics(specialMetrics)
            .Build();

        // Assert
        var metrics = metadata.Get<Dictionary<string, double>>("performance_metrics");
        Assert.Equal(double.PositiveInfinity, metrics!["infinity"]);
        Assert.Equal(double.NegativeInfinity, metrics["neg_infinity"]);
        Assert.True(double.IsNaN(metrics["nan"]));
        Assert.Equal(double.MaxValue, metrics["max"]);
        Assert.Equal(double.MinValue, metrics["min"]);
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseVeryLargeRetryCount()
    {
        // Act
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddRetryCount(int.MaxValue)
            .Build();

        // Assert
        Assert.Equal(int.MaxValue, metadata.Get<int>("retry_count"));
    }

    #endregion

    #region ToolUsageMetadata Equality Tests

    [Fact]
    public void ShouldBeEqual_WhenComparingTwoMetadataWithSameValues()
    {
        // Arrange
        var metadata1 = ToolUsageMetadata.CreateBuilder()
            .AddInput("test input")
            .AddOutput("test output")
            .AddRetryCount(2)
            .Build();

        var metadata2 = ToolUsageMetadata.CreateBuilder()
            .AddInput("test input")
            .AddOutput("test output")
            .AddRetryCount(2)
            .Build();

        // Act & Assert
        Assert.Equal(metadata1, metadata2);
        Assert.True(metadata1.Equals(metadata2));
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingTwoMetadataWithDifferentValues()
    {
        // Arrange
        var metadata1 = ToolUsageMetadata.CreateBuilder()
            .AddInput("input1")
            .Build();

        var metadata2 = ToolUsageMetadata.CreateBuilder()
            .AddInput("input2")
            .Build();

        // Act & Assert
        Assert.NotEqual(metadata1, metadata2);
        Assert.False(metadata1.Equals(metadata2));
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingMetadataWithDifferentCounts()
    {
        // Arrange
        var metadata1 = ToolUsageMetadata.CreateBuilder()
            .AddInput("test")
            .Build();

        var metadata2 = ToolUsageMetadata.CreateBuilder()
            .AddInput("test")
            .AddOutput("extra")
            .Build();

        // Act & Assert
        Assert.NotEqual(metadata1, metadata2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingMetadataWithNull()
    {
        // Arrange
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddInput("test")
            .Build();

        // Act & Assert
        Assert.False(metadata.Equals(null));
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingMetadataWithSameReference()
    {
        // Arrange
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddInput("test")
            .Build();

        // Act & Assert
        Assert.True(metadata.Equals(metadata));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingMetadataWithDifferentObjectType()
    {
        // Arrange
        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddInput("test")
            .Build();

        // Act & Assert
        Assert.False(metadata.Equals("not ToolUsageMetadata"));
    }

    [Fact]
    public void ShouldHaveSameHashCode_WhenTwoMetadataAreEqual()
    {
        // Arrange
        var metadata1 = ToolUsageMetadata.CreateBuilder()
            .AddInput("test")
            .AddRetryCount(0)
            .Build();

        var metadata2 = ToolUsageMetadata.CreateBuilder()
            .AddInput("test")
            .AddRetryCount(0)
            .Build();

        // Act & Assert
        Assert.Equal(metadata1.GetHashCode(), metadata2.GetHashCode());
    }

    [Fact]
    public void ShouldHaveDifferentHashCode_WhenTwoMetadataAreDifferent()
    {
        // Arrange
        var metadata1 = ToolUsageMetadata.CreateBuilder()
            .AddInput("test1")
            .Build();

        var metadata2 = ToolUsageMetadata.CreateBuilder()
            .AddInput("test2")
            .Build();

        // Act & Assert
        Assert.NotEqual(metadata1.GetHashCode(), metadata2.GetHashCode());
    }

    [Fact]
    public void ShouldBeEqual_WhenComparingTwoEmptyMetadata()
    {
        // Arrange
        var metadata1 = ToolUsageMetadata.Empty;
        var metadata2 = ToolUsageMetadata.Empty;

        // Act & Assert
        Assert.Equal(metadata1, metadata2);
        Assert.Equal(metadata1.GetHashCode(), metadata2.GetHashCode());
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldDataProcessingToolMetadata_WhenUsingComplexScenario()
    {
        // Simulate metadata for a data processing tool execution
        var resourceUsage = new Dictionary<string, double>
        {
            { "memory_peak_mb", 1024.5 },
            { "cpu_average_percent", 75.2 },
            { "disk_io_mb", 256.8 },
            { "network_io_mb", 128.4 }
        };

        var performanceMetrics = new Dictionary<string, double>
        {
            { "processing_time_seconds", 45.7 },
            { "records_per_second", 2500.0 },
            { "error_rate_percent", 0.02 },
            { "throughput_mbps", 15.8 }
        };

        var validationErrors = new[]
        {
            "Row 1523: Invalid date format",
            "Row 2847: Missing required field 'email'",
            "Row 3901: Value exceeds maximum length"
        };

        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddInput("dataset-2024-q1.csv (50MB, 100,000 records)")
            .AddOutput("processed-data.json (35MB, 99,997 valid records)")
            .AddRetryCount(1)
            .AddCacheHit(false)
            .AddResourcesUsed(resourceUsage)
            .AddExecutionContext("batch-processing-worker-03")
            .AddValidationErrors(validationErrors)
            .AddPerformanceMetrics(performanceMetrics)
            .Add("tool_version", "2.1.0")
            .Add("start_time", new DateTime(2024, 1, 15, 10, 30, 0))
            .Add("end_time", new DateTime(2024, 1, 15, 10, 30, 45))
            .Build();

        // Assert comprehensive metadata
        Assert.Contains("dataset-2024-q1.csv", metadata.GetRequired<string>("input"));
        Assert.Contains("processed-data.json", metadata.GetRequired<string>("output"));
        Assert.Equal(1, metadata.GetRequired<int>("retry_count"));
        Assert.False(metadata.GetRequired<bool>("cache_hit"));
        Assert.Equal("batch-processing-worker-03", metadata.GetRequired<string>("execution_context"));
        Assert.Equal("2.1.0", metadata.GetRequired<string>("tool_version"));

        // Verify resource usage
        var resources = metadata.GetRequired<Dictionary<string, double>>("resources_used");
        Assert.Equal(1024.5, resources["memory_peak_mb"]);
        Assert.Equal(75.2, resources["cpu_average_percent"]);

        // Verify performance metrics
        var metrics = metadata.GetRequired<Dictionary<string, double>>("performance_metrics");
        Assert.Equal(45.7, metrics["processing_time_seconds"]);
        Assert.Equal(2500.0, metrics["records_per_second"]);

        // Verify validation errors
        var errors = metadata.GetRequired<string[]>("validation_errors");
        Assert.Equal(3, errors.Length);
        Assert.Contains("Row 1523: Invalid date format", errors);

        // Verify total metadata count
        Assert.Equal(11, metadata.Keys.Count());
    }

    [Fact]
    public void ShouldApiCallToolMetadata_WhenUsingComplexScenario()
    {
        // Simulate metadata for an HTTP API call tool
        var resourceUsage = new Dictionary<string, double>
        {
            { "network_latency_ms", 25.3 },
            { "bandwidth_usage_kb", 156.7 },
            { "connection_pool_size", 10.0 }
        };

        var performanceMetrics = new Dictionary<string, double>
        {
            { "request_duration_ms", 150.5 },
            { "response_size_bytes", 8192.0 },
            { "dns_lookup_ms", 5.2 },
            { "ssl_handshake_ms", 12.8 }
        };

        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddInput("GET /api/v2/users?page=1&limit=100")
            .AddOutput("200 OK: {users: [...], total: 1500, page: 1}")
            .AddRetryCount(0)
            .AddCacheHit(true)
            .AddResourcesUsed(resourceUsage)
            .AddExecutionContext("api-client-pool-worker")
            .AddValidationErrors([])
            .AddPerformanceMetrics(performanceMetrics)
            .Add("http_status_code", 200)
            .Add("response_headers", new Dictionary<string, string>
            {
                { "Content-Type", "application/json" },
                { "Cache-Control", "max-age=3600" },
                { "X-RateLimit-Remaining", "950" }
            })
            .Add("request_id", "req-12345-abcde")
            .Build();

        // Assert API-specific metadata
        Assert.Contains("GET /api/v2/users", metadata.GetRequired<string>("input"));
        Assert.Contains("200 OK", metadata.GetRequired<string>("output"));
        Assert.Equal(0, metadata.GetRequired<int>("retry_count"));
        Assert.True(metadata.GetRequired<bool>("cache_hit"));
        Assert.Equal(200, metadata.GetRequired<int>("http_status_code"));
        Assert.Equal("req-12345-abcde", metadata.GetRequired<string>("request_id"));

        // Verify network performance metrics
        var metrics = metadata.GetRequired<Dictionary<string, double>>("performance_metrics");
        Assert.Equal(150.5, metrics["request_duration_ms"]);
        Assert.Equal(8192.0, metrics["response_size_bytes"]);

        // Verify resource usage
        var resources = metadata.GetRequired<Dictionary<string, double>>("resources_used");
        Assert.Equal(25.3, resources["network_latency_ms"]);

        // Verify empty validation errors
        var errors = metadata.GetRequired<string[]>("validation_errors");
        Assert.Empty(errors);

        // Verify response headers
        var headers = metadata.GetRequired<Dictionary<string, string>>("response_headers");
        Assert.Equal("application/json", headers["Content-Type"]);
        Assert.Equal("950", headers["X-RateLimit-Remaining"]);
    }

    [Fact]
    public void ShouldDatabaseToolMetadata_WhenUsingComplexScenario()
    {
        // Simulate metadata for a database operation tool
        var resourceUsage = new Dictionary<string, double>
        {
            { "connection_pool_active", 5.0 },
            { "memory_usage_mb", 64.2 },
            { "temp_space_mb", 12.8 },
            { "buffer_pool_hit_ratio", 0.98 }
        };

        var performanceMetrics = new Dictionary<string, double>
        {
            { "query_execution_ms", 245.7 },
            { "rows_examined", 50000.0 },
            { "rows_returned", 1250.0 },
            { "index_usage", 3.0 }
        };

        var validationErrors = new[]
        {
            "Query timeout threshold exceeded (>200ms)",
            "Non-optimal index usage detected"
        };

        var metadata = ToolUsageMetadata.CreateBuilder()
            .AddInput("SELECT u.*, p.name as profile_name FROM users u JOIN profiles p ON u.id = p.user_id WHERE u.active = true ORDER BY u.created_at DESC LIMIT 1000")
            .AddOutput("Query completed: 1,250 rows returned in 245.7ms")
            .AddRetryCount(1)
            .AddCacheHit(false)
            .AddResourcesUsed(resourceUsage)
            .AddExecutionContext("db-reader-replica-2")
            .AddValidationErrors(validationErrors)
            .AddPerformanceMetrics(performanceMetrics)
            .Add("database_name", "production_users")
            .Add("transaction_id", "txn-789-xyz")
            .Add("connection_id", "conn-456")
            .Add("query_plan_hash", "qp-12345abcde")
            .Build();

        // Assert database-specific metadata
        Assert.Contains("SELECT u.*, p.name", metadata.GetRequired<string>("input"));
        Assert.Contains("1,250 rows returned", metadata.GetRequired<string>("output"));
        Assert.Equal(1, metadata.GetRequired<int>("retry_count"));
        Assert.False(metadata.GetRequired<bool>("cache_hit"));
        Assert.Equal("production_users", metadata.GetRequired<string>("database_name"));
        Assert.Equal("txn-789-xyz", metadata.GetRequired<string>("transaction_id"));

        // Verify database performance metrics
        var metrics = metadata.GetRequired<Dictionary<string, double>>("performance_metrics");
        Assert.Equal(245.7, metrics["query_execution_ms"]);
        Assert.Equal(50000.0, metrics["rows_examined"]);
        Assert.Equal(1250.0, metrics["rows_returned"]);

        // Verify database resource usage
        var resources = metadata.GetRequired<Dictionary<string, double>>("resources_used");
        Assert.Equal(0.98, resources["buffer_pool_hit_ratio"]);
        Assert.Equal(64.2, resources["memory_usage_mb"]);

        // Verify performance warnings
        var errors = metadata.GetRequired<string[]>("validation_errors");
        Assert.Equal(2, errors.Length);
        Assert.Contains("Query timeout threshold exceeded", errors[0]);
        Assert.Contains("Non-optimal index usage detected", errors[1]);
    }

    [Fact]
    public void ShouldMetadataEvolution_WhenUsingComplexScenario()
    {
        // Start with basic metadata
        var initial = ToolUsageMetadata.CreateBuilder()
            .AddInput("initial data")
            .AddRetryCount(0)
            .AddCacheHit(false)
            .Build();

        // Add performance data after execution
        var withPerformance = ToolUsageMetadata.CreateBuilder()
            .AddInput("initial data")
            .AddOutput("processed successfully")
            .AddRetryCount(0)
            .AddCacheHit(false)
            .AddPerformanceMetrics(new Dictionary<string, double>
            {
                { "execution_time_ms", 125.5 },
                { "memory_peak_mb", 256.0 }
            })
            .Build();

        // Add error information after retry
        var withRetry = ToolUsageMetadata.CreateBuilder()
            .AddInput("initial data")
            .AddOutput("processed with warnings")
            .AddRetryCount(1)
            .AddCacheHit(false)
            .AddValidationErrors(s_timeoutError)
            .AddPerformanceMetrics(new Dictionary<string, double>
            {
                { "execution_time_ms", 250.8 },
                { "memory_peak_mb", 256.0 },
                { "retry_delay_ms", 1000.0 }
            })
            .Build();

        // Final metadata with all information
        var final = ToolUsageMetadata.CreateBuilder()
            .AddInput("initial data")
            .AddOutput("processing completed successfully")
            .AddRetryCount(1)
            .AddCacheHit(false)
            .AddResourcesUsed(new Dictionary<string, double>
            {
                { "total_memory_mb", 256.0 },
                { "cpu_time_ms", 180.5 }
            })
            .AddExecutionContext("production-worker-05")
            .AddValidationErrors(s_timeoutAndRetryErrors)
            .AddPerformanceMetrics(new Dictionary<string, double>
            {
                { "total_execution_time_ms", 250.8 },
                { "successful_completion_time_ms", 125.3 }
            })
            .Add("completion_status", "success_with_retry")
            .Build();

        // Assert evolution of metadata
        Assert.Equal("initial data", initial.GetRequired<string>("input"));
        Assert.Equal(0, initial.GetRequired<int>("retry_count"));
        Assert.False(initial.ContainsKey("output"));

        Assert.Equal("processed successfully", withPerformance.GetRequired<string>("output"));
        Assert.True(withPerformance.ContainsKey("performance_metrics"));
        Assert.False(withPerformance.ContainsKey("validation_errors"));

        Assert.Equal(1, withRetry.GetRequired<int>("retry_count"));
        Assert.Single(withRetry.GetRequired<string[]>("validation_errors"));

        Assert.Equal("processing completed successfully", final.GetRequired<string>("output"));
        Assert.Equal("success_with_retry", final.GetRequired<string>("completion_status"));
        Assert.Equal(2, final.GetRequired<string[]>("validation_errors").Length);
        Assert.True(final.ContainsKey("resources_used"));
        Assert.Equal(9, final.Keys.Count());
    }

    #endregion
}
