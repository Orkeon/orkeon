using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.ValueObjects;

public class TypedParametersTests
{
    private static readonly string[] VolumeMounts =
    [
        "/app/data:/data:rw",
        "/app/logs:/logs:rw",
        "/etc/ssl:/ssl:ro"
    ];
    private static readonly string[] ValidationRules =
    [
        "required_fields_present",
        "timestamp_format_valid",
        "data_not_empty"
    ];

    private static readonly string[] RequiredSchemaFields = ["id", "timestamp", "data"];
    #region CommunicationParameters Tests

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingCommunicationParametersWithEmpty()
    {
        // Act
        var parameters = CommunicationParameters.Empty;

        // Assert
        Assert.NotNull(parameters);
        Assert.Equal(0, parameters.Count);
        Assert.Empty(parameters.Keys);
        Assert.False(parameters.Contains("any"));
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingCommunicationParametersGettingRequiredWithExistingKey()
    {
        // Arrange
        var parameters = CommunicationParameters.CreateBuilder()
            .AddTimeout(TimeoutQuick)
            .AddRetryCount(3)
            .Build();

        // Act
        var timeout = parameters.GetRequired<TimeSpan>("timeout");
        var retryCount = parameters.GetRequired<int>("retryCount");

        // Assert
        Assert.Equal(TimeoutQuick, timeout);
        Assert.Equal(3, retryCount);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingCommunicationParametersGettingRequiredWithNonExistentKey()
    {
        // Arrange
        var parameters = CommunicationParameters.Empty;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => parameters.GetRequired<string>("missing"));
        Assert.Contains("Required parameter 'missing' not found", exception.Message);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingCommunicationParametersGettingOptionalWithExistingKey()
    {
        // Arrange
        var parameters = CommunicationParameters.CreateBuilder()
            .AddEncryption(true)
            .Build();

        // Act
        var encryption = parameters.GetOptional<bool>("encrypted");

        // Assert
        Assert.True(encryption);
    }

    [Fact]
    public void ShouldReturnDefault_WhenUsingCommunicationParametersGettingOptionalWithNonExistentKey()
    {
        // Arrange
        var parameters = CommunicationParameters.Empty;

        // Act
        var missing = parameters.GetOptional<int>("missing");

        // Assert
        Assert.Equal(0, missing);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingCommunicationParametersGettingOptionalWithDefaultWithExistingKey()
    {
        // Arrange
        var parameters = CommunicationParameters.CreateBuilder()
            .AddRetryCount(5)
            .Build();

        // Act
        var retryCount = parameters.GetOptional("retryCount", 10);

        // Assert
        Assert.Equal(5, retryCount);
    }

    [Fact]
    public void ShouldReturnDefaultValue_WhenUsingCommunicationParametersGettingOptionalWithDefaultWithNonExistentKey()
    {
        // Arrange
        var parameters = CommunicationParameters.Empty;

        // Act
        var retryCount = parameters.GetOptional("retryCount", 10);

        // Assert
        Assert.Equal(10, retryCount);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithValue_WhenUsingCommunicationParametersWith()
    {
        // Arrange
        var original = CommunicationParameters.CreateBuilder()
            .AddTimeout(TimeSpan.FromSeconds(10))
            .Build();

        // Act
        var modified = original.With("newParam", "newValue");

        // Assert
        Assert.NotSame(original, modified);
        Assert.Equal(1, original.Count);
        Assert.Equal(2, modified.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), original.GetRequired<TimeSpan>("timeout"));
        Assert.Equal(TimeSpan.FromSeconds(10), modified.GetRequired<TimeSpan>("timeout"));
        Assert.Equal("newValue", modified.GetRequired<string>("newParam"));
    }

    [Fact]
    public void ShouldSetTimeoutParameter_WhenUsingCommunicationParametersUsingBuilderAddTimeout()
    {
        // Act
        var parameters = CommunicationParameters.CreateBuilder()
            .AddTimeout(TimeoutStandard)
            .Build();

        // Assert
        Assert.Equal(TimeoutStandard, parameters.GetRequired<TimeSpan>("timeout"));
    }

    [Fact]
    public void ShouldSetRetryCountParameter_WhenUsingCommunicationParametersUsingBuilderAddRetryCount()
    {
        // Act
        var parameters = CommunicationParameters.CreateBuilder()
            .AddRetryCount(7)
            .Build();

        // Assert
        Assert.Equal(7, parameters.GetRequired<int>("retryCount"));
    }

    [Fact]
    public void ShouldSetEncryptedParameter_WhenUsingCommunicationParametersUsingBuilderAddEncryption()
    {
        // Act
        var parameters = CommunicationParameters.CreateBuilder()
            .AddEncryption(false)
            .Build();

        // Assert
        Assert.False(parameters.GetRequired<bool>("encrypted"));
    }

    [Fact]
    public void ShouldSetCompressionParameter_WhenUsingCommunicationParametersUsingBuilderAddCompression()
    {
        // Act
        var parameters = CommunicationParameters.CreateBuilder()
            .AddCompression("gzip")
            .Build();

        // Assert
        Assert.Equal("gzip", parameters.GetRequired<string>("compression"));
    }

    [Fact]
    public void ShouldAddAllParameters_WhenUsingCommunicationParametersUsingBuilderChainedCalls()
    {
        // Act
        var parameters = CommunicationParameters.CreateBuilder()
            .AddTimeout(TimeSpan.FromSeconds(45))
            .AddRetryCount(2)
            .AddEncryption(true)
            .AddCompression("lz4")
            .Add("custom", "value")
            .Build();

        // Assert
        Assert.Equal(5, parameters.Count);
        Assert.Equal(TimeSpan.FromSeconds(45), parameters.GetRequired<TimeSpan>("timeout"));
        Assert.Equal(2, parameters.GetRequired<int>("retryCount"));
        Assert.True(parameters.GetRequired<bool>("encrypted"));
        Assert.Equal("lz4", parameters.GetRequired<string>("compression"));
        Assert.Equal("value", parameters.GetRequired<string>("custom"));
    }

    #endregion

    #region ExecutionContextParameters Tests

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingExecutionContextParametersWithEmpty()
    {
        // Act
        var parameters = ExecutionContextParameters.Empty;

        // Assert
        Assert.NotNull(parameters);
        Assert.Equal(0, parameters.Count);
        Assert.Empty(parameters.Keys);
    }

    [Fact]
    public void ShouldSetWorkingDirectoryParameter_WhenUsingExecutionContextParametersUsingBuilderAddWorkingDirectory()
    {
        // Act
        var parameters = ExecutionContextParameters.CreateBuilder()
            .AddWorkingDirectory("/app/workspace")
            .Build();

        // Assert
        Assert.Equal("/app/workspace", parameters.GetRequired<string>("workingDirectory"));
    }

    [Fact]
    public void ShouldSetEnvironmentParameter_WhenUsingExecutionContextParametersUsingBuilderAddEnvironmentVariable()
    {
        // Act
        var parameters = ExecutionContextParameters.CreateBuilder()
            .AddEnvironmentVariable("PATH", "/usr/bin:/bin")
            .AddEnvironmentVariable("HOME", "/home/user")
            .Build();

        // Assert
        Assert.Equal("/usr/bin:/bin", parameters.GetRequired<string>("env_PATH"));
        Assert.Equal("/home/user", parameters.GetRequired<string>("env_HOME"));
    }

    [Fact]
    public void ShouldSetMaxMemoryParameter_WhenUsingExecutionContextParametersUsingBuilderAddMaxMemory()
    {
        // Act
        var parameters = ExecutionContextParameters.CreateBuilder()
            .AddMaxMemory(1073741824L) // 1GB
            .Build();

        // Assert
        Assert.Equal(1073741824L, parameters.GetRequired<long>("maxMemory"));
    }

    [Fact]
    public void ShouldSetMaxCpuParameter_WhenUsingExecutionContextParametersUsingBuilderAddMaxCpu()
    {
        // Act
        var parameters = ExecutionContextParameters.CreateBuilder()
            .AddMaxCpu(85.5)
            .Build();

        // Assert
        Assert.Equal(85.5, parameters.GetRequired<double>("maxCpu"));
    }

    [Fact]
    public void ShouldAddAllParameters_WhenUsingExecutionContextParametersUsingBuilderChainedCalls()
    {
        // Act
        var parameters = ExecutionContextParameters.CreateBuilder()
            .AddWorkingDirectory("/tmp/execution")
            .AddEnvironmentVariable("DEBUG", "true")
            .AddEnvironmentVariable("LOG_LEVEL", "info")
            .AddMaxMemory(2147483648L) // 2GB
            .AddMaxCpu(75.0)
            .Add("custom_setting", "enabled")
            .Build();

        // Assert
        Assert.Equal(6, parameters.Count);
        Assert.Equal("/tmp/execution", parameters.GetRequired<string>("workingDirectory"));
        Assert.Equal("true", parameters.GetRequired<string>("env_DEBUG"));
        Assert.Equal("info", parameters.GetRequired<string>("env_LOG_LEVEL"));
        Assert.Equal(2147483648L, parameters.GetRequired<long>("maxMemory"));
        Assert.Equal(75.0, parameters.GetRequired<double>("maxCpu"));
        Assert.Equal("enabled", parameters.GetRequired<string>("custom_setting"));
    }

    [Fact]
    public void ShouldCreateNewInstanceWithValue_WhenUsingExecutionContextParametersWith()
    {
        // Arrange
        var original = ExecutionContextParameters.CreateBuilder()
            .AddWorkingDirectory("/original")
            .Build();

        // Act
        var modified = original.With("workingDirectory", "/modified");

        // Assert
        Assert.Equal("/original", original.GetRequired<string>("workingDirectory"));
        Assert.Equal("/modified", modified.GetRequired<string>("workingDirectory"));
    }

    #endregion

    #region ExecutionStepParameters Tests

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingExecutionStepParametersWithEmpty()
    {
        // Act
        var parameters = ExecutionStepParameters.Empty;

        // Assert
        Assert.NotNull(parameters);
        Assert.Equal(0, parameters.Count);
        Assert.Empty(parameters.Keys);
    }

    [Fact]
    public void ShouldSetInputParameter_WhenUsingExecutionStepParametersUsingBuilderAddInput()
    {
        // Act
        var parameters = ExecutionStepParameters.CreateBuilder()
            .AddInput("data", "input data")
            .AddInput("config", new { setting = "value" })
            .Build();

        // Assert
        Assert.Equal("input data", parameters.GetRequired<string>("input_data"));
        Assert.NotNull(parameters.GetRequired<object>("input_config"));
    }

    [Fact]
    public void ShouldSetOutputParameter_WhenUsingExecutionStepParametersUsingBuilderAddOutput()
    {
        // Act
        var parameters = ExecutionStepParameters.CreateBuilder()
            .AddOutput("result", "success")
            .AddOutput("count", 42)
            .Build();

        // Assert
        Assert.Equal("success", parameters.GetRequired<string>("output_result"));
        Assert.Equal(42, parameters.GetRequired<int>("output_count"));
    }

    [Fact]
    public void ShouldSetConditionParameter_WhenUsingExecutionStepParametersUsingBuilderAddCondition()
    {
        // Act
        var parameters = ExecutionStepParameters.CreateBuilder()
            .AddCondition("status == 'ready'")
            .Build();

        // Assert
        Assert.Equal("status == 'ready'", parameters.GetRequired<string>("condition"));
    }

    [Fact]
    public void ShouldSetPriorityParameter_WhenUsingExecutionStepParametersUsingBuilderAddPriority()
    {
        // Act
        var parameters = ExecutionStepParameters.CreateBuilder()
            .AddPriority(10)
            .Build();

        // Assert
        Assert.Equal(10, parameters.GetRequired<int>("priority"));
    }

    [Fact]
    public void ShouldAddAllParameters_WhenUsingExecutionStepParametersUsingBuilderChainedCalls()
    {
        // Arrange
        var inputData = new Dictionary<string, object> { { "key", "value" } };

        // Act
        var parameters = ExecutionStepParameters.CreateBuilder()
            .AddInput("sourceData", inputData)
            .AddInput("format", "json")
            .AddOutput("processedData", "transformed")
            .AddOutput("status", "completed")
            .AddCondition("input_format == 'json'")
            .AddPriority(5)
            .Add("custom_flag", true)
            .Build();

        // Assert
        Assert.Equal(7, parameters.Count);
        Assert.Same(inputData, parameters.GetRequired<Dictionary<string, object>>("input_sourceData"));
        Assert.Equal("json", parameters.GetRequired<string>("input_format"));
        Assert.Equal("transformed", parameters.GetRequired<string>("output_processedData"));
        Assert.Equal("completed", parameters.GetRequired<string>("output_status"));
        Assert.Equal("input_format == 'json'", parameters.GetRequired<string>("condition"));
        Assert.Equal(5, parameters.GetRequired<int>("priority"));
        Assert.True(parameters.GetRequired<bool>("custom_flag"));
    }

    [Fact]
    public void ShouldCreateNewInstanceWithValue_WhenUsingExecutionStepParametersWith()
    {
        // Arrange
        var original = ExecutionStepParameters.CreateBuilder()
            .AddPriority(1)
            .Build();

        // Act
        var modified = original.With("priority", 10);

        // Assert
        Assert.Equal(1, original.GetRequired<int>("priority"));
        Assert.Equal(10, modified.GetRequired<int>("priority"));
    }

    #endregion

    #region Base Class Behavior Tests (via concrete implementations)

    [Fact]
    public void ShouldReturnTrue_WhenUsingTypedParametersUsingContainsWithExistingKey()
    {
        // Arrange
        var parameters = CommunicationParameters.CreateBuilder()
            .AddTimeout(TimeSpan.FromSeconds(15))
            .Build();

        // Act
        var result = parameters.Contains("timeout");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingTypedParametersUsingContainsWithNonExistentKey()
    {
        // Arrange
        var parameters = CommunicationParameters.Empty;

        // Act
        var result = parameters.Contains("missing");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldReturnAllParameterKeys_WhenUsingTypedParametersUsingKeys()
    {
        // Arrange
        var parameters = ExecutionContextParameters.CreateBuilder()
            .AddWorkingDirectory("/test")
            .AddMaxMemory(1024L)
            .AddEnvironmentVariable("TEST", "value")
            .Build();

        // Act
        var keys = parameters.Keys.ToList();

        // Assert
        Assert.Equal(3, keys.Count);
        Assert.Contains("workingDirectory", keys);
        Assert.Contains("maxMemory", keys);
        Assert.Contains("env_TEST", keys);
    }

    [Fact]
    public void ShouldReturnNumberOfParameters_WhenUsingTypedParametersCounting()
    {
        // Arrange
        var parameters = ExecutionStepParameters.CreateBuilder()
            .AddInput("data1", "value1")
            .AddInput("data2", "value2")
            .AddOutput("result", "success")
            .AddPriority(3)
            .Build();

        // Act
        var count = parameters.Count;

        // Assert
        Assert.Equal(4, count);
    }

    [Fact]
    public void ShouldConvert_WhenUsingTypedParametersGettingRequiredWithTypeConversion()
    {
        // Arrange
        var parameters = CommunicationParameters.CreateBuilder()
            .AddRetryCount(42)
            .Build();

        // Act
        var asString = parameters.GetRequired<string>("retryCount");
        var asDouble = parameters.GetRequired<double>("retryCount");
        var asLong = parameters.GetRequired<long>("retryCount");

        // Assert
        Assert.Equal("42", asString);
        Assert.Equal(42.0, asDouble);
        Assert.Equal(42L, asLong);
    }

    [Fact]
    public void ShouldConvert_WhenUsingTypedParametersGettingOptionalWithTypeConversion()
    {
        // Arrange
        var parameters = ExecutionContextParameters.CreateBuilder()
            .AddMaxCpu(75.5)
            .Build();

        // Act
        var asString = parameters.GetOptional<string>("maxCpu");
        var asInt = parameters.GetOptional<int>("maxCpu");

        // Assert
        Assert.Equal("75.5", asString);
        Assert.Equal(75, asInt); // Should truncate
    }

    #endregion

    #region ParameterValue Tests

    [Fact]
    public void ShouldCreate_WhenUsingParameterValueFromWithValidValue()
    {
        // Act
        var stringValue = ParameterValue.From("test");
        var intValue = ParameterValue.From(123);
        var boolValue = ParameterValue.From(true);
        var objectValue = ParameterValue.From(new { id = 1 });

        // Assert
        Assert.NotNull(stringValue);
        Assert.NotNull(intValue);
        Assert.NotNull(boolValue);
        Assert.NotNull(objectValue);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingParameterValueFromWithNull()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => ParameterValue.From(null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingParameterValueGettingValueWithCorrectType()
    {
        // Arrange
        var value = ParameterValue.From("parameter value");

        // Act
        var result = value.GetValue<string>();

        // Assert
        Assert.Equal("parameter value", result);
    }

    [Fact]
    public void ShouldConvert_WhenUsingParameterValueGettingValueWithConvertibleType()
    {
        // Arrange
        var value = ParameterValue.From(789);

        // Act
        var asString = value.GetValue<string>();
        var asDouble = value.GetValue<double>();
        var asLong = value.GetValue<long>();

        // Assert
        Assert.Equal("789", asString);
        Assert.Equal(789.0, asDouble);
        Assert.Equal(789L, asLong);
    }

    [Fact]
    public void ShouldThrowInvalidCastException_WhenUsingParameterValueGettingValueWithIncompatibleType()
    {
        // Arrange
        var value = ParameterValue.From("not a number");

        // Act & Assert
        var exception = Assert.Throws<InvalidCastException>(() => value.GetValue<int>());
        Assert.Contains("Cannot convert parameter value of type String to Int32", exception.Message);
    }

    [Fact]
    public void ShouldReturnOriginalValue_WhenUsingParameterValueUsingRawValue()
    {
        // Arrange
        var original = new { name = "Parameter", id = 456 };
        var value = ParameterValue.From(original);

        // Act
        var raw = value.RawValue;

        // Assert
        Assert.Same(original, raw);
    }

    [Fact]
    public void ShouldReturnCorrectType_WhenUsingParameterValueUsingValueType()
    {
        // Arrange
        var stringValue = ParameterValue.From("test");
        var intValue = ParameterValue.From(42);
        var timeSpanValue = ParameterValue.From(TimeoutExtended);

        // Act & Assert
        Assert.Equal(typeof(string), stringValue.ValueType);
        Assert.Equal(typeof(int), intValue.ValueType);
        Assert.Equal(typeof(TimeSpan), timeSpanValue.ValueType);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithEmptyStringParameters()
    {
        // Act
        var commParams = CommunicationParameters.CreateBuilder()
            .AddCompression("")
            .Build();

        var execParams = ExecutionContextParameters.CreateBuilder()
            .AddWorkingDirectory("")
            .Build();

        var stepParams = ExecutionStepParameters.CreateBuilder()
            .AddCondition("")
            .Build();

        // Assert
        Assert.Equal("", commParams.GetRequired<string>("compression"));
        Assert.Equal("", execParams.GetRequired<string>("workingDirectory"));
        Assert.Equal("", stepParams.GetRequired<string>("condition"));
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithNegativeValues()
    {
        // Act
        var commParams = CommunicationParameters.CreateBuilder()
            .AddRetryCount(-1)
            .Build();

        var execParams = ExecutionContextParameters.CreateBuilder()
            .AddMaxMemory(-100L)
            .AddMaxCpu(-50.5)
            .Build();

        var stepParams = ExecutionStepParameters.CreateBuilder()
            .AddPriority(-5)
            .Build();

        // Assert
        Assert.Equal(-1, commParams.GetRequired<int>("retryCount"));
        Assert.Equal(-100L, execParams.GetRequired<long>("maxMemory"));
        Assert.Equal(-50.5, execParams.GetRequired<double>("maxCpu"));
        Assert.Equal(-5, stepParams.GetRequired<int>("priority"));
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithZeroValues()
    {
        // Act
        var commParams = CommunicationParameters.CreateBuilder()
            .AddTimeout(TimeSpan.Zero)
            .AddRetryCount(0)
            .Build();

        var execParams = ExecutionContextParameters.CreateBuilder()
            .AddMaxMemory(0L)
            .AddMaxCpu(0.0)
            .Build();

        // Assert
        Assert.Equal(TimeSpan.Zero, commParams.GetRequired<TimeSpan>("timeout"));
        Assert.Equal(0, commParams.GetRequired<int>("retryCount"));
        Assert.Equal(0L, execParams.GetRequired<long>("maxMemory"));
        Assert.Equal(0.0, execParams.GetRequired<double>("maxCpu"));
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithMaxValues()
    {
        // Act
        var commParams = CommunicationParameters.CreateBuilder()
            .AddRetryCount(int.MaxValue)
            .AddTimeout(TimeSpan.MaxValue)
            .Build();

        var execParams = ExecutionContextParameters.CreateBuilder()
            .AddMaxMemory(long.MaxValue)
            .AddMaxCpu(double.MaxValue)
            .Build();

        // Assert
        Assert.Equal(int.MaxValue, commParams.GetRequired<int>("retryCount"));
        Assert.Equal(TimeSpan.MaxValue, commParams.GetRequired<TimeSpan>("timeout"));
        Assert.Equal(long.MaxValue, execParams.GetRequired<long>("maxMemory"));
        Assert.Equal(double.MaxValue, execParams.GetRequired<double>("maxCpu"));
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithSpecialDoubleValues()
    {
        // Act
        var parameters = ExecutionContextParameters.CreateBuilder()
            .AddMaxCpu(double.PositiveInfinity)
            .Add("neg_infinity", double.NegativeInfinity)
            .Add("nan", double.NaN)
            .Build();

        // Assert
        Assert.Equal(double.PositiveInfinity, parameters.GetRequired<double>("maxCpu"));
        Assert.Equal(double.NegativeInfinity, parameters.GetRequired<double>("neg_infinity"));
        Assert.True(double.IsNaN(parameters.GetRequired<double>("nan")));
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldCommunicationParametersForDistributedSystem_WhenUsingComplexScenario()
    {
        // Simulate communication parameters for a distributed system
        var parameters = CommunicationParameters.CreateBuilder()
            .AddTimeout(TimeoutQuick)
            .AddRetryCount(5)
            .AddEncryption(true)
            .AddCompression("lz4")
            .Add("maxMessageSize", 1048576) // 1MB
            .Add("keepAliveInterval", TimeSpan.FromMinutes(2))
            .Add("heartbeatTimeout", TimeSpan.FromSeconds(10))
            .Add("connectionPoolSize", 20)
            .Add("sslCertificatePath", "/etc/ssl/certs/service.pem")
            .Add("authenticationMethod", "JWT")
            .Build();

        // Assert all communication settings
        Assert.Equal(TimeoutQuick, parameters.GetRequired<TimeSpan>("timeout"));
        Assert.Equal(5, parameters.GetRequired<int>("retryCount"));
        Assert.True(parameters.GetRequired<bool>("encrypted"));
        Assert.Equal("lz4", parameters.GetRequired<string>("compression"));
        Assert.Equal(1048576, parameters.GetRequired<int>("maxMessageSize"));
        Assert.Equal(TimeSpan.FromMinutes(2), parameters.GetRequired<TimeSpan>("keepAliveInterval"));
        Assert.Equal(TimeSpan.FromSeconds(10), parameters.GetRequired<TimeSpan>("heartbeatTimeout"));
        Assert.Equal(20, parameters.GetRequired<int>("connectionPoolSize"));
        Assert.Equal("/etc/ssl/certs/service.pem", parameters.GetRequired<string>("sslCertificatePath"));
        Assert.Equal("JWT", parameters.GetRequired<string>("authenticationMethod"));

        Assert.Equal(10, parameters.Count);
        Assert.True(parameters.Contains("encrypted"));
        Assert.False(parameters.Contains("nonExistent"));
    }

    [Fact]
    public void ShouldExecutionContextParametersForContainerizedApp_WhenUsingComplexScenario()
    {
        // Simulate execution context for a containerized application
        var parameters = ExecutionContextParameters.CreateBuilder()
            .AddWorkingDirectory("/app/workspace")
            .AddEnvironmentVariable("NODE_ENV", "production")
            .AddEnvironmentVariable("DATABASE_URL", "postgresql://user:pass@db:5432/app")
            .AddEnvironmentVariable("REDIS_URL", "redis://redis:6379/0")
            .AddEnvironmentVariable("LOG_LEVEL", "info")
            .AddEnvironmentVariable("API_PORT", "3000")
            .AddMaxMemory(2147483648L) // 2GB
            .AddMaxCpu(80.0)
            .Add("containerName", "app-worker-01")
            .Add("imageName", "myapp:v2.1.0")
            .Add("networkMode", "bridge")
            .Add("volumeMounts", VolumeMounts)
            .Add("resourceLimits", new Dictionary<string, object>
            {
                { "memory", "2Gi" },
                { "cpu", "800m" },
                { "storage", "10Gi" }
            })
            .Build();

        // Assert execution context settings
        Assert.Equal("/app/workspace", parameters.GetRequired<string>("workingDirectory"));
        Assert.Equal("production", parameters.GetRequired<string>("env_NODE_ENV"));
        Assert.Contains("postgresql://", parameters.GetRequired<string>("env_DATABASE_URL"));
        Assert.Equal("redis://redis:6379/0", parameters.GetRequired<string>("env_REDIS_URL"));
        Assert.Equal("info", parameters.GetRequired<string>("env_LOG_LEVEL"));
        Assert.Equal("3000", parameters.GetRequired<string>("env_API_PORT"));
        Assert.Equal(2147483648L, parameters.GetRequired<long>("maxMemory"));
        Assert.Equal(80.0, parameters.GetRequired<double>("maxCpu"));
        Assert.Equal("app-worker-01", parameters.GetRequired<string>("containerName"));
        Assert.Equal("myapp:v2.1.0", parameters.GetRequired<string>("imageName"));

        var volumeMounts = parameters.GetRequired<string[]>("volumeMounts");
        Assert.Equal(3, volumeMounts.Length);
        Assert.Contains("/app/data:/data:rw", volumeMounts);

        var resourceLimits = parameters.GetRequired<Dictionary<string, object>>("resourceLimits");
        Assert.Equal("2Gi", resourceLimits["memory"]);
        Assert.Equal("800m", resourceLimits["cpu"]);

        Assert.Equal(13, parameters.Count);
    }

    [Fact]
    public void ShouldExecutionStepParametersForDataPipeline_WhenUsingComplexScenario()
    {
        // Simulate execution step parameters for a data processing pipeline
        var inputSchema = new
        {
            type = "object",
            properties = new
            {
                id = new { type = "string" },
                timestamp = new { type = "string", format = "date-time" },
                data = new { type = "object" }
            },
            required = RequiredSchemaFields
        };

        var outputSchema = new
        {
            type = "object",
            properties = new
            {
                processedId = new { type = "string" },
                result = new { type = "object" },
                metadata = new { type = "object" }
            }
        };

        var parameters = ExecutionStepParameters.CreateBuilder()
            .AddInput("sourceData", "/data/input/batch-2024-01-15.json")
            .AddInput("schema", inputSchema)
            .AddInput("validationRules", ValidationRules)
            .AddOutput("processedData", "/data/output/processed-2024-01-15.json")
            .AddOutput("validationReport", "/data/reports/validation-2024-01-15.json")
            .AddOutput("schema", outputSchema)
            .AddCondition("input_validation_passed && !skip_processing")
            .AddPriority(7)
            .Add("batchId", "batch-20240115-001")
            .Add("processingMode", "strict")
            .Add("errorHandling", "fail_fast")
            .Add("maxRecords", 100000)
            .Add("timeout", TimeSpan.FromMinutes(30))
            .Add("parallelism", 4)
            .Add("checkpointInterval", 1000)
            .Build();

        // Assert data pipeline step settings
        Assert.Equal("/data/input/batch-2024-01-15.json", parameters.GetRequired<string>("input_sourceData"));
        Assert.Same(inputSchema, parameters.GetRequired<object>("input_schema"));

        var validationRules = parameters.GetRequired<string[]>("input_validationRules");
        Assert.Equal(3, validationRules.Length);
        Assert.Contains("required_fields_present", validationRules);

        Assert.Equal("/data/output/processed-2024-01-15.json", parameters.GetRequired<string>("output_processedData"));
        Assert.Same(outputSchema, parameters.GetRequired<object>("output_schema"));

        Assert.Contains("input_validation_passed", parameters.GetRequired<string>("condition"));
        Assert.Equal(7, parameters.GetRequired<int>("priority"));
        Assert.Equal("batch-20240115-001", parameters.GetRequired<string>("batchId"));
        Assert.Equal("strict", parameters.GetRequired<string>("processingMode"));
        Assert.Equal("fail_fast", parameters.GetRequired<string>("errorHandling"));
        Assert.Equal(100000, parameters.GetRequired<int>("maxRecords"));
        Assert.Equal(TimeSpan.FromMinutes(30), parameters.GetRequired<TimeSpan>("timeout"));
        Assert.Equal(4, parameters.GetRequired<int>("parallelism"));
        Assert.Equal(1000, parameters.GetRequired<int>("checkpointInterval"));

        Assert.Equal(15, parameters.Count);
    }

    [Fact]
    public void ShouldParametersEvolutionAndInheritance_WhenUsingComplexScenario()
    {
        // Start with base communication parameters
        var baseCommunication = CommunicationParameters.CreateBuilder()
            .AddTimeout(TimeSpan.FromSeconds(15))
            .AddRetryCount(3)
            .AddEncryption(false)
            .Build();

        // Evolve to secure communication
        var secureCommunication = baseCommunication
            .With("encrypted", true)
            .With("compression", "gzip")
            .With("sslVersion", "TLS1.3");

        // Create execution context based on communication settings
        var executionContext = ExecutionContextParameters.CreateBuilder()
            .AddWorkingDirectory("/secure/workspace")
            .AddMaxMemory(1073741824L) // 1GB
            .AddMaxCpu(50.0)
            .Add("communicationTimeout", secureCommunication.GetRequired<TimeSpan>("timeout"))
            .Add("securityEnabled", secureCommunication.GetRequired<bool>("encrypted"))
            .Add("compressionAlgorithm", secureCommunication.GetRequired<string>("compression"))
            .Build();

        // Create execution step with inherited context
        var executionStep = ExecutionStepParameters.CreateBuilder()
            .AddInput("workingDir", executionContext.GetRequired<string>("workingDirectory"))
            .AddInput("memoryLimit", executionContext.GetRequired<long>("maxMemory"))
            .AddOutput("result", "step_completed")
            .AddCondition("security_enabled == true")
            .AddPriority(5)
            .Add("inheritedTimeout", executionContext.GetRequired<TimeSpan>("communicationTimeout"))
            .Add("securityContext", new
            {
                encrypted = executionContext.GetRequired<bool>("securityEnabled"),
                algorithm = executionContext.GetRequired<string>("compressionAlgorithm")
            })
            .Build();

        // Assert parameter evolution and inheritance
        Assert.Equal(TimeSpan.FromSeconds(15), baseCommunication.GetRequired<TimeSpan>("timeout"));
        Assert.False(baseCommunication.GetRequired<bool>("encrypted"));

        Assert.True(secureCommunication.GetRequired<bool>("encrypted"));
        Assert.Equal("gzip", secureCommunication.GetRequired<string>("compression"));
        Assert.Equal("TLS1.3", secureCommunication.GetRequired<string>("sslVersion"));

        Assert.Equal("/secure/workspace", executionContext.GetRequired<string>("workingDirectory"));
        Assert.Equal(TimeSpan.FromSeconds(15), executionContext.GetRequired<TimeSpan>("communicationTimeout"));
        Assert.True(executionContext.GetRequired<bool>("securityEnabled"));

        Assert.Equal("/secure/workspace", executionStep.GetRequired<string>("input_workingDir"));
        Assert.Equal(1073741824L, executionStep.GetRequired<long>("input_memoryLimit"));
        Assert.Equal("step_completed", executionStep.GetRequired<string>("output_result"));
        Assert.Equal(TimeSpan.FromSeconds(15), executionStep.GetRequired<TimeSpan>("inheritedTimeout"));

        var securityContext = executionStep.GetRequired<object>("securityContext");
        Assert.NotNull(securityContext);

        // Verify all parameters have correct counts
        Assert.Equal(5, secureCommunication.Count);
        Assert.Equal(6, executionContext.Count);
        Assert.Equal(7, executionStep.Count);
    }

    #endregion
}
