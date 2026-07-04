using Orkeon.Domain.Tools.Protocol;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Abstractions.Tests.Base;

public sealed class ToolBaseTests : IDisposable
{
    private static readonly string[] s_statusValues = ["active", "inactive", "pending"];
    private static readonly int[] s_intArray123 = [1, 2, 3];

    private readonly TestTool _tool;
    private readonly TestLogger<TestTool> _logger;

    public ToolBaseTests()
    {
        _logger = new TestLogger<TestTool>();
        _tool = new TestTool(_logger);
    }

    public void Dispose() => _tool.Dispose();

    [Fact]
    public void ShouldUseNullLogger_WhenConstructedWithNullLogger()
    {
        // Arrange & Act
        using var tool = new TestTool(null);

        // Assert
        Assert.NotNull(tool);
        Assert.Equal("TestTool", tool.Name);
    }

    [Fact]
    public void ShouldReturnExpectedValues_WhenAccessingDefaultProperties()
    {
        // Assert
        Assert.Null(_tool.Parameters);
        Assert.False(_tool.RequiresHumanApproval);
        Assert.Equal("General", _tool.Category);
        Assert.Null(_tool.RateLimitPerMinute);
    }

    [Fact]
    public async Task ShouldExecuteSuccessfully_WhenCalledWithValidParameters()
    {
        // Arrange
        var request = new ToolCallRequest(
            ToolName: "TestTool",
            Parameters: new Dictionary<string, object?>
            {
                ["input"] = "test input"
            }
        );

        // Act
        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.Success);
        Assert.NotNull(response.Result);
        Assert.NotNull(response.Result);
        var resultDict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(resultDict);
        Assert.Contains("Processed: ", resultDict["message"]!.ToString()!);
    }

    [Fact]
    public async Task ShouldReturnValidationError_WhenRequiredParameterIsMissing()
    {
        // Arrange
        _tool.SetRequiredParameter("requiredParam");
        var request = new ToolCallRequest(
            ToolName: "TestTool",
            Parameters: new Dictionary<string, object?>
            {
                ["otherParam"] = "value"
            }
        );

        // Act
        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.Null(response.Result);
        Assert.Contains("Required parameter 'requiredParam' is missing", response.Error);
        Assert.True(response.Metadata?.ContainsKey("validation_error"));
    }

    [Fact]
    public async Task ShouldReturnValidationError_WhenParameterTypeIsInvalid()
    {
        // Arrange
        _tool.SetParameterTypeValidation("numberParam", "number");
        var request = new ToolCallRequest(
            ToolName: "TestTool",
            Parameters: new Dictionary<string, object?>
            {
                ["numberParam"] = "not a number"
            }
        );

        // Act
        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("Parameter 'numberParam' has invalid type", response.Error);
    }

    [Fact]
    public async Task ShouldReturnCancelledError_WhenOperationIsCancelled()
    {
        // Arrange
        var request = new ToolCallRequest(
            ToolName: "TestTool",
            Parameters: new Dictionary<string, object?>
            {
                ["input"] = "test"
            }
        );

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var response = await _tool.CallAsync(request, cts.Token);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("Operation cancelled", response.Error);
        Assert.True(response.Metadata?.ContainsKey("cancelled"));
    }

    [Fact]
    public async Task ShouldReturnError_WhenExecutionThrowsException()
    {
        // Arrange
        _tool.ShouldThrowOnExecute = true;
        var request = new ToolCallRequest(
            ToolName: "TestTool",
            Parameters: new Dictionary<string, object?>
            {
                ["input"] = "test"
            }
        );

        // Act
        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("Tool execution failed", response.Error);
        Assert.True(_logger.HasLoggedError("Error calling tool TestTool"));
    }

    [Fact]
    public async Task ShouldParseAndExecute_WhenInputIsValidJson()
    {
        // Arrange
        var input = JsonSerializer.Serialize(new
        {
            input = "test value",
            option = "enabled"
        });

        // Act
        var result = await _tool.ExecuteAsync(input, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Output);
    }

    [Fact]
    public async Task ShouldTreatAsInputParameter_WhenInputIsSimpleString()
    {
        // Arrange
        var input = "simple string input";

        // Act
        var result = await _tool.ExecuteAsync(input, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Output);
    }

    [Fact]
    public async Task ShouldTreatAsSimpleInput_WhenJsonIsInvalid()
    {
        // Arrange
        var input = "{ invalid json }";

        // Act
        var result = await _tool.ExecuteAsync(input, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Output);
    }

    [Fact]
    public async Task ShouldReturnError_WhenExecutionFails()
    {
        // Arrange
        _tool.ShouldThrowOnExecute = true;
        var input = "test input";

        // Act
        var result = await _tool.ExecuteAsync(input, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Tool execution failed", result.Error);
    }

    [Fact]
    public void ShouldReturnSuccess_WhenValidatingParametersWithNoSchema()
    {
        // Arrange
        using var tool = new MinimalTestTool();
        var parameters = new Dictionary<string, object?>
        {
            ["any"] = "value"
        };

        // Act
        var result = tool.TestValidateParameters(parameters);

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ShouldReturnSuccess_WhenEnumConstraintValueIsValid()
    {
        // Arrange
        _tool.SetParameterEnumConstraint("status", s_statusValues);
        var parameters = new Dictionary<string, object?>
        {
            ["status"] = "active"
        };

        // Act
        var result = _tool.TestValidateParameters(parameters);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldReturnError_WhenEnumConstraintValueIsInvalid()
    {
        // Arrange
        _tool.SetParameterEnumConstraint("status", s_statusValues);
        var parameters = new Dictionary<string, object?>
        {
            ["status"] = "unknown"
        };

        // Act
        var result = _tool.TestValidateParameters(parameters);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Parameter 'status' must be one of", result.Error);
    }

    [Fact]
    public void ShouldValidateCorrectly_WhenTypeIsString()
    {
        // Arrange & Act & Assert
        Assert.True(TestTool.TestIsValidType("test", "string"));
        Assert.False(TestTool.TestIsValidType(123, "string"));
        Assert.False(TestTool.TestIsValidType(true, "string"));
    }

    [Fact]
    public void ShouldValidateCorrectly_WhenTypeIsNumber()
    {
        // Arrange & Act & Assert
        Assert.True(TestTool.TestIsValidType(123, "number"));
        Assert.True(TestTool.TestIsValidType(123.45, "number"));
        Assert.True(TestTool.TestIsValidType("123", "number"));     // string-tolerant for text-parsed tool calls
        Assert.True(TestTool.TestIsValidType("3.14", "number"));    // string-tolerant for text-parsed tool calls
        Assert.False(TestTool.TestIsValidType("not-a-number", "number"));
        Assert.False(TestTool.TestIsValidType(true, "number"));
    }

    [Fact]
    public void ShouldValidateCorrectly_WhenTypeIsBoolean()
    {
        // Arrange & Act & Assert
        Assert.True(TestTool.TestIsValidType(true, "boolean"));
        Assert.True(TestTool.TestIsValidType(false, "boolean"));
        Assert.True(TestTool.TestIsValidType("true", "boolean"));   // string-tolerant for text-parsed tool calls
        Assert.True(TestTool.TestIsValidType("false", "boolean"));  // string-tolerant for text-parsed tool calls
        Assert.False(TestTool.TestIsValidType("maybe", "boolean"));
        Assert.False(TestTool.TestIsValidType(1, "boolean"));
    }

    [Fact]
    public void ShouldValidateCorrectly_WhenTypeIsObject()
    {
        // Arrange & Act & Assert
        Assert.True(TestTool.TestIsValidType(new Dictionary<string, object?>(), "object"));
        Assert.False(TestTool.TestIsValidType(new { prop = "value" }, "object")); // Anonymous types are not IDictionary<string, object?>
        Assert.False(TestTool.TestIsValidType("object", "object"));
        Assert.False(TestTool.TestIsValidType(123, "object"));
    }

    [Fact]
    public void ShouldValidateCorrectly_WhenTypeIsArray()
    {
        // Arrange & Act & Assert
        Assert.True(TestTool.TestIsValidType(s_intArray123, "array"));
        Assert.True(TestTool.TestIsValidType(new List<string> { "a", "b" }, "array"));
        Assert.False(TestTool.TestIsValidType("array", "array"));
        Assert.False(TestTool.TestIsValidType(123, "array"));
    }

    [Fact]
    public void ShouldReturnTrue_WhenTypeIsUnknown()
    {
        // Arrange & Act & Assert
        Assert.True(TestTool.TestIsValidType("any value", "unknown"));
        Assert.True(TestTool.TestIsValidType(123, "custom"));
    }
}

// Test implementation of ToolBase for testing
public class TestTool : ToolBase
{
    public override string Name => "TestTool";
    public override string Description => "A test tool for unit testing";

    private Dictionary<string, ParameterSchema> _parameters = [];
    public bool ShouldThrowOnExecute { get; set; }
    public bool ShouldCancelOperation { get; set; }

    public override ToolSchema Schema => new ToolSchema(
        Name: Name,
        Description: Description,
        Parameters: _parameters
    );

    public TestTool(ILogger<TestTool>? logger = null) : base(logger)
    {
    }

    public void SetRequiredParameter(string name)
    {
        _parameters[name] = new ParameterSchema(
            Type: "string",
            Description: $"Parameter {name}",
            Required: true
        );
    }

    public void SetParameterTypeValidation(string name, string type)
    {
        _parameters[name] = new ParameterSchema(
            Type: type,
            Description: $"Parameter {name}",
            Required: false
        );
    }

    public void SetParameterEnumConstraint(string name, string[] values)
    {
        _parameters[name] = new ParameterSchema(
            Type: "string",
            Description: $"Parameter {name}",
            Required: false,
            Enum: values.Cast<object>().ToList()
        );
    }

    protected override async Task<ToolCallResponse> ExecuteCoreAsync(ToolCallRequest request, CancellationToken cancellationToken)
    {
        if (ShouldCancelOperation)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (ShouldThrowOnExecute)
        {
            throw new InvalidOperationException("Test exception");
        }

        await System.Threading.Tasks.Task.Delay(1, cancellationToken);

        var input = request.Parameters.TryGetValue("input", out var inputValue)
            ? inputValue?.ToString() ?? "default"
            : "default";

        return new ToolCallResponse(
            Success: true,
            Result: new Dictionary<string, object?>
            {
                ["message"] = $"Processed: {input}",
                ["timestamp"] = DateTime.UtcNow
            },
            Error: null
        );
    }

    // Expose protected methods for testing
    public ValidationResult TestValidateParameters(Dictionary<string, object?> parameters)
    {
        return ValidateParameters(parameters);
    }

    public static bool TestIsValidType(object value, string type)
    {
        return IsValidType(value, type);
    }
}

// Minimal test tool with no schema
public class MinimalTestTool : ToolBase
{
    public override string Name => "MinimalTool";
    public override string Description => "Minimal test tool";
    public override ToolSchema Schema => null!;

    protected override Task<ToolCallResponse> ExecuteCoreAsync(ToolCallRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ToolCallResponse(true, null, null));
    }

    public ValidationResult TestValidateParameters(Dictionary<string, object?> parameters)
    {
        return ValidateParameters(parameters);
    }
}

// Test logger implementation
public class TestLogger<T> : ILogger<T>
{
    private readonly List<string> _errorLogs = [];
    private readonly List<string> _debugLogs = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);

        if (logLevel == LogLevel.Error)
            _errorLogs.Add(message);
        else if (logLevel == LogLevel.Debug)
            _debugLogs.Add(message);
    }

    public bool HasLoggedError(string containing) =>
        _errorLogs.Any(log => log.Contains(containing));

    public bool HasLoggedDebug(string containing) =>
        _debugLogs.Any(log => log.Contains(containing));
}
