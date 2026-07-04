using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Domain.Tests.ValueObjects;

public class ToolCallTests
{
    #region Constructor Tests

    [Fact]
    public void ShouldCreateInstance_WhenConstructingWithValidParameters()
    {
        // Arrange
        var toolName = "FileReader";
        var callId = "call-12345";
        var arguments = ToolArguments.CreateBuilder()
            .AddString(ParamPath, "/data/file.txt")
            .AddBool("validate", true)
            .Build();

        // Act
        var toolCall = new ToolCall(toolName, callId, arguments);

        // Assert
        Assert.Equal("FileReader", toolCall.ToolName);
        Assert.Equal("call-12345", toolCall.CallId);
        Assert.Same(arguments, toolCall.Arguments);
    }

    [Fact]
    public void ShouldCreateInstance_WhenConstructingWithEmptyArguments()
    {
        // Act
        var toolCall = new ToolCall("TestTool", "call-001", ToolArguments.Empty);

        // Assert
        Assert.Equal("TestTool", toolCall.ToolName);
        Assert.Equal("call-001", toolCall.CallId);
        Assert.Same(ToolArguments.Empty, toolCall.Arguments);
    }

    [Fact]
    public void ShouldCreateInstance_WhenConstructingWithNullToolName()
    {
        // Act
        var toolCall = new ToolCall(null!, "call-001", ToolArguments.Empty);

        // Assert
        Assert.Null(toolCall.ToolName);
        Assert.Equal("call-001", toolCall.CallId);
        Assert.Same(ToolArguments.Empty, toolCall.Arguments);
    }

    [Fact]
    public void ShouldCreateInstance_WhenConstructingWithNullCallId()
    {
        // Act
        var toolCall = new ToolCall("TestTool", null!, ToolArguments.Empty);

        // Assert
        Assert.Equal("TestTool", toolCall.ToolName);
        Assert.Null(toolCall.CallId);
        Assert.Same(ToolArguments.Empty, toolCall.Arguments);
    }

    [Fact]
    public void ShouldCreateInstance_WhenConstructingWithNullArguments()
    {
        // Act
        var toolCall = new ToolCall("TestTool", "call-001", null!);

        // Assert
        Assert.Equal("TestTool", toolCall.ToolName);
        Assert.Equal("call-001", toolCall.CallId);
        Assert.Null(toolCall.Arguments);
    }

    #endregion

    #region Create Factory Method Tests

    [Fact]
    public void ShouldCreateToolCall_WhenCreatingWithValidToolName()
    {
        // Arrange
        var arguments = ToolArguments.CreateBuilder()
            .AddString("input", "test data")
            .AddInt("timeout", 30)
            .Build();

        // Act
        var toolCall = ToolCall.Create("DataProcessor", arguments);

        // Assert
        Assert.Equal("DataProcessor", toolCall.ToolName);
        Assert.NotNull(toolCall.CallId);
        Assert.NotEmpty(toolCall.CallId);
        Assert.Same(arguments, toolCall.Arguments);
    }

    [Fact]
    public void ShouldUseEmptyArguments_WhenCreatingWithNullArguments()
    {
        // Act
        var toolCall = ToolCall.Create("SimpleTool", ToolArguments.Empty);

        // Assert
        Assert.Equal("SimpleTool", toolCall.ToolName);
        Assert.NotNull(toolCall.CallId);
        Assert.Same(ToolArguments.Empty, toolCall.Arguments);
    }

    [Fact]
    public void ShouldGenerateUniqueCallIds_WhenCreating()
    {
        // Act
        var toolCall1 = ToolCall.Create("Tool1", ToolArguments.Empty);
        var toolCall2 = ToolCall.Create("Tool2", ToolArguments.Empty);
        var toolCall3 = ToolCall.Create("Tool1", ToolArguments.Empty);

        // Assert
        Assert.NotEqual(toolCall1.CallId, toolCall2.CallId);
        Assert.NotEqual(toolCall1.CallId, toolCall3.CallId);
        Assert.NotEqual(toolCall2.CallId, toolCall3.CallId);
    }

    [Fact]
    public void ShouldGenerateValidGuidCallId_WhenCreating()
    {
        // Act
        var toolCall = ToolCall.Create("TestTool", ToolArguments.Empty);

        // Assert
        Assert.True(Guid.TryParse(toolCall.CallId, out var guid));
        Assert.NotEqual(Guid.Empty, guid);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenCreatingWithEmptyToolName()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            ToolCall.Create("", ToolArguments.Empty));
        Assert.Equal("toolName", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenCreatingWithNullToolName()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            ToolCall.Create(null!, ToolArguments.Empty));
        Assert.Equal("toolName", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenCreatingWithWhitespaceToolName()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            ToolCall.Create("   ", ToolArguments.Empty));
        Assert.Equal("toolName", exception.ParamName);
    }

    #endregion

    #region Record Behavior Tests

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var arguments = ToolArguments.CreateBuilder()
            .AddString("param", "value")
            .Build();

        var toolCall1 = new ToolCall("TestTool", "call-123", arguments);
        var toolCall2 = new ToolCall("TestTool", "call-123", arguments);

        // Act & Assert
        Assert.Equal(toolCall1, toolCall2);
        Assert.True(toolCall1 == toolCall2);
        Assert.False(toolCall1 != toolCall2);
        Assert.Equal(toolCall1.GetHashCode(), toolCall2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange
        var arguments = ToolArguments.Empty;
        var toolCall1 = new ToolCall("Tool1", "call-123", arguments);
        var toolCall2 = new ToolCall("Tool2", "call-123", arguments);

        // Act & Assert
        Assert.NotEqual(toolCall1, toolCall2);
        Assert.False(toolCall1 == toolCall2);
        Assert.True(toolCall1 != toolCall2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentCallIds()
    {
        // Arrange
        var arguments = ToolArguments.Empty;
        var toolCall1 = new ToolCall("TestTool", "call-123", arguments);
        var toolCall2 = new ToolCall("TestTool", "call-456", arguments);

        // Act & Assert
        Assert.NotEqual(toolCall1, toolCall2);
        Assert.False(toolCall1 == toolCall2);
        Assert.True(toolCall1 != toolCall2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentArguments()
    {
        // Arrange
        var args1 = ToolArguments.CreateBuilder().AddString("key", "value1").Build();
        var args2 = ToolArguments.CreateBuilder().AddString("key", "value2").Build();

        var toolCall1 = new ToolCall("TestTool", "call-123", args1);
        var toolCall2 = new ToolCall("TestTool", "call-123", args2);

        // Act & Assert
        Assert.NotEqual(toolCall1, toolCall2);
        Assert.False(toolCall1 == toolCall2);
        Assert.True(toolCall1 != toolCall2);
    }

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingWith()
    {
        // Arrange
        var originalArgs = ToolArguments.CreateBuilder()
            .AddString("original", "value")
            .Build();

        var newArgs = ToolArguments.CreateBuilder()
            .AddString("modified", "new value")
            .Build();

        var original = new ToolCall("OriginalTool", "call-123", originalArgs);

        // Act
        var modifiedToolName = original with { ToolName = "ModifiedTool" };
        var modifiedCallId = original with { CallId = "call-456" };
        var modifiedArgs = original with { Arguments = newArgs };

        // Assert
        Assert.Equal("OriginalTool", original.ToolName);
        Assert.Equal("call-123", original.CallId);
        Assert.Same(originalArgs, original.Arguments);

        Assert.Equal("ModifiedTool", modifiedToolName.ToolName);
        Assert.Equal("call-123", modifiedToolName.CallId);
        Assert.Same(originalArgs, modifiedToolName.Arguments);

        Assert.Equal("OriginalTool", modifiedCallId.ToolName);
        Assert.Equal("call-456", modifiedCallId.CallId);
        Assert.Same(originalArgs, modifiedCallId.Arguments);

        Assert.Equal("OriginalTool", modifiedArgs.ToolName);
        Assert.Equal("call-123", modifiedArgs.CallId);
        Assert.Same(newArgs, modifiedArgs.Arguments);
    }

    [Fact]
    public void ShouldReturnFormattedString_WhenCallingToString()
    {
        // Arrange
        var arguments = ToolArguments.CreateBuilder()
            .AddString("input", "test data")
            .Build();

        var toolCall = new ToolCall("DataValidator", "call-789", arguments);

        // Act
        var result = toolCall.ToString();

        // Assert
        Assert.Contains("DataValidator", result);
        Assert.Contains("call-789", result);
        Assert.Contains("ToolCall", result);
    }

    [Fact]
    public void ShouldReturnAllValues_WhenUsingDeconstruct()
    {
        // Arrange
        var arguments = ToolArguments.CreateBuilder()
            .AddInt("count", 42)
            .Build();

        var toolCall = new ToolCall("Counter", "call-999", arguments);

        // Act
        var (toolName, callId, args) = toolCall;

        // Assert
        Assert.Equal("Counter", toolName);
        Assert.Equal("call-999", callId);
        Assert.Same(arguments, args);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseVeryLongToolName()
    {
        // Arrange
        var longName = new string('A', 1000);

        // Act
        var toolCall = ToolCall.Create(longName, ToolArguments.Empty);

        // Assert
        Assert.Equal(longName, toolCall.ToolName);
        Assert.Equal(1000, toolCall.ToolName.Length);
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseWithSpecialCharactersInToolName()
    {
        // Arrange
        var specialName = "Tool@123-FileReader_v2.0!";

        // Act
        var toolCall = ToolCall.Create(specialName, ToolArguments.Empty);

        // Assert
        Assert.Equal(specialName, toolCall.ToolName);
    }

    [Fact]
    public void ShouldBeStored_WhenUsingEdgeCaseUnicodeCharactersInToolName()
    {
        // Arrange
        var unicodeName = "文件读取器-ファイルリーダー-مقروء_الملف";

        // Act
        var toolCall = ToolCall.Create(unicodeName, ToolArguments.Empty);

        // Assert
        Assert.Equal(unicodeName, toolCall.ToolName);
    }

    [Fact]
    public void ShouldWork_WhenUsingEdgeCaseWithEmptyArgumentsSet()
    {
        // Act
        var toolCall = ToolCall.Create("EmptyArgsTool", ToolArguments.Empty);

        // Assert
        Assert.Equal("EmptyArgsTool", toolCall.ToolName);
        Assert.Same(ToolArguments.Empty, toolCall.Arguments);
        Assert.Equal(0, toolCall.Arguments.Count);
    }

    [Fact]
    public void ShouldWork_WhenUsingEdgeCaseWithLargeArgumentsSet()
    {
        // Arrange
        var builder = ToolArguments.CreateBuilder();
        for (int i = 0; i < 100; i++)
        {
            builder.AddString($"param{i}", $"value{i}");
        }
        var largeArgs = builder.Build();

        // Act
        var toolCall = ToolCall.Create("LargeArgsTool", largeArgs);

        // Assert
        Assert.Equal("LargeArgsTool", toolCall.ToolName);
        Assert.Same(largeArgs, toolCall.Arguments);
        Assert.Equal(100, toolCall.Arguments.Count);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldFileProcessingToolCall_WhenUsingComplexScenario()
    {
        // Arrange
        var processingOptions = new
        {
            encoding = "UTF-8",
            bufferSize = 8192,
            validateChecksum = true,
            compressionLevel = 6
        };

        var arguments = ToolArguments.CreateBuilder()
            .AddString("inputPath", "/data/input/large-dataset.csv")
            .AddString("outputPath", "/data/output/processed-data.json")
            .AddString("operation", "transform")
            .AddInt("batchSize", 1000)
            .AddInt("maxMemoryMB", 512)
            .AddBool("skipErrors", false)
            .AddBool("createBackup", true)
            .AddDouble("progressInterval", 5.0)
            .AddDouble("errorThreshold", 0.01)
            .AddObject("options", processingOptions)
            .Build();

        // Act
        var toolCall = ToolCall.Create("AdvancedFileProcessor", arguments);

        // Assert
        Assert.Equal("AdvancedFileProcessor", toolCall.ToolName);
        Assert.NotNull(toolCall.CallId);
        Assert.True(Guid.TryParse(toolCall.CallId, out _));
        Assert.Same(arguments, toolCall.Arguments);

        // Verify arguments are accessible
        Assert.Equal("/data/input/large-dataset.csv", toolCall.Arguments.GetRequiredString("inputPath"));
        Assert.Equal("transform", toolCall.Arguments.GetRequiredString("operation"));
        Assert.Equal(1000, toolCall.Arguments.GetRequired<int>("batchSize"));
        Assert.False(toolCall.Arguments.GetRequired<bool>("skipErrors"));
        Assert.Equal(0.01, toolCall.Arguments.GetRequired<double>("errorThreshold"));
        Assert.Same(processingOptions, toolCall.Arguments.GetRequiredObject<object>("options"));
    }

    [Fact]
    public void ShouldApiCallToolCall_WhenUsingComplexScenario()
    {
        // Arrange
        var requestHeaders = new Dictionary<string, string>
        {
            { "Authorization", "Bearer jwt-token-12345" },
            { "Content-Type", "application/json" },
            { "Accept", "application/json" },
            { "User-Agent", "Orkeon-Agent/1.0" }
        };

        var requestBody = new
        {
            query = new
            {
                filters = new { status = "active", category = "premium" },
                sort = new { field = "created_at", direction = "desc" },
                pagination = new { page = 1, size = 50 }
            },
            options = new
            {
                includeMetadata = true,
                format = "detailed"
            }
        };

        var arguments = ToolArguments.CreateBuilder()
            .AddString(ParamUrl, "https://api.example.com/v2/users/search")
            .AddString("method", "POST")
            .AddString("contentType", "application/json")
            .AddInt("timeoutSeconds", 30)
            .AddInt("maxRetries", 3)
            .AddBool("followRedirects", true)
            .AddBool("validateResponseSchema", true)
            .AddDouble("retryBackoffMultiplier", 1.5)
            .AddObject("headers", requestHeaders)
            .AddObject("body", requestBody)
            .Build();

        // Act
        var toolCall = ToolCall.Create("HttpApiClient", arguments);

        // Assert
        Assert.Equal("HttpApiClient", toolCall.ToolName);
        Assert.NotNull(toolCall.CallId);
        Assert.Same(arguments, toolCall.Arguments);

        // Verify complex arguments
        Assert.Equal("https://api.example.com/v2/users/search", toolCall.Arguments.GetRequiredString(ParamUrl));
        Assert.Equal("POST", toolCall.Arguments.GetRequiredString("method"));
        Assert.Equal(30, toolCall.Arguments.GetRequired<int>("timeoutSeconds"));
        Assert.True(toolCall.Arguments.GetRequired<bool>("followRedirects"));
        Assert.Same(requestHeaders, toolCall.Arguments.GetRequiredObject<Dictionary<string, string>>("headers"));
        Assert.Same(requestBody, toolCall.Arguments.GetRequiredObject<object>("body"));

        // Test optional access
        Assert.Equal("POST", toolCall.Arguments.GetOptionalString("method"));
        Assert.Null(toolCall.Arguments.GetOptionalString("proxy"));
    }

    [Fact]
    public void ShouldDatabaseQueryToolCall_WhenUsingComplexScenario()
    {
        // Arrange
        var queryParameters = new Dictionary<string, object>
        {
            { "@userId", 12345 },
            { "@startDate", new DateTime(2024, 1, 1) },
            { "@endDate", new DateTime(2024, 12, 31) },
            { "@includeDeleted", false },
            { "@maxResults", 1000 }
        };

        var connectionConfig = new
        {
            server = "db-server.example.com",
            database = "analytics",
            port = 5432,
            sslMode = "require",
            commandTimeout = 300,
            connectionTimeout = 30
        };

        var arguments = ToolArguments.CreateBuilder()
            .AddString(ParamQuery, @"
                SELECT u.id, u.name, u.email, COUNT(o.id) as order_count
                FROM users u
                LEFT JOIN orders o ON u.id = o.user_id
                WHERE u.created_at BETWEEN @startDate AND @endDate
                  AND u.active = true
                  AND (@includeDeleted = true OR u.deleted_at IS NULL)
                GROUP BY u.id, u.name, u.email
                ORDER BY order_count DESC
                LIMIT @maxResults")
            .AddString("connectionString", "Server=db-server.example.com;Database=analytics;Port=5432;")
            .AddString("operation", "ExecuteQuery")
            .AddInt("expectedRows", 500)
            .AddInt("queryTimeoutSeconds", 300)
            .AddBool("useTransaction", false)
            .AddBool("enableQueryLogging", true)
            .AddDouble("slowQueryThresholdSeconds", 10.0)
            .AddObject("parameters", queryParameters)
            .AddObject("connectionConfig", connectionConfig)
            .Build();

        // Act
        var toolCall = ToolCall.Create("DatabaseQueryExecutor", arguments);

        // Assert
        Assert.Equal("DatabaseQueryExecutor", toolCall.ToolName);
        Assert.NotNull(toolCall.CallId);
        Assert.Same(arguments, toolCall.Arguments);

        // Verify database-specific arguments
        Assert.Contains("SELECT u.id, u.name", toolCall.Arguments.GetRequiredString(ParamQuery));
        Assert.Equal("ExecuteQuery", toolCall.Arguments.GetRequiredString("operation"));
        Assert.Equal(500, toolCall.Arguments.GetRequired<int>("expectedRows"));
        Assert.False(toolCall.Arguments.GetRequired<bool>("useTransaction"));
        Assert.Equal(10.0, toolCall.Arguments.GetRequired<double>("slowQueryThresholdSeconds"));

        var parameters = toolCall.Arguments.GetRequiredObject<Dictionary<string, object>>("parameters");
        Assert.Equal(12345, parameters["@userId"]);
        Assert.False((bool)parameters["@includeDeleted"]);
        Assert.Equal(1000, parameters["@maxResults"]);

        var config = toolCall.Arguments.GetRequiredObject<object>("connectionConfig");
        Assert.Same(connectionConfig, config);
    }

    [Fact]
    public void ShouldToolCallEvolution_WhenUsingComplexScenario()
    {
        // Simulate evolving a tool call through modifications
        var initialArgs = ToolArguments.CreateBuilder()
            .AddString("mode", "development")
            .AddBool("debug", true)
            .Build();

        var originalCall = ToolCall.Create("DataProcessor", initialArgs);

        // Modify tool name for production
        var productionCall = originalCall with { ToolName = "ProductionDataProcessor" };

        // Add production arguments
        var productionArgs = ToolArguments.CreateBuilder()
            .AddString("mode", "production")
            .AddBool("debug", false)
            .AddInt("batchSize", 1000)
            .AddDouble("timeoutMinutes", 30.0)
            .Build();

        var finalCall = productionCall with { Arguments = productionArgs };

        // Generate new call ID for production run
        var deployedCall = finalCall with { CallId = Guid.NewGuid().ToString() };

        // Assert evolution
        Assert.Equal("DataProcessor", originalCall.ToolName);
        Assert.Equal("development", originalCall.Arguments.GetRequiredString("mode"));
        Assert.True(originalCall.Arguments.GetRequired<bool>("debug"));

        Assert.Equal("ProductionDataProcessor", productionCall.ToolName);
        Assert.Equal("development", productionCall.Arguments.GetRequiredString("mode")); // Still original args

        Assert.Equal("ProductionDataProcessor", finalCall.ToolName);
        Assert.Equal("production", finalCall.Arguments.GetRequiredString("mode"));
        Assert.False(finalCall.Arguments.GetRequired<bool>("debug"));
        Assert.Equal(1000, finalCall.Arguments.GetRequired<int>("batchSize"));

        Assert.Equal("ProductionDataProcessor", deployedCall.ToolName);
        Assert.Equal("production", deployedCall.Arguments.GetRequiredString("mode"));
        Assert.NotEqual(originalCall.CallId, deployedCall.CallId);
        Assert.NotEqual(finalCall.CallId, deployedCall.CallId);
    }

    #endregion
}
