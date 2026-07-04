using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Domain.Tests.Tools;

public class IBaseToolTests
{
    // Test implementation of IBaseTool
    private class TestTool : IBaseTool
    {
        private readonly Func<ToolCallRequest, CancellationToken, Task<ToolCallResponse>>? _callFunc;
        private readonly Func<string, CancellationToken, Task<ToolResult>>? _executeFunc;
        private readonly Func<string, bool>? _validateFunc;

        public string Name { get; set; }
        public string Description { get; set; }
        public ToolSchema Schema { get; set; }
        public int CallAsyncCount { get; private set; }
        public int ExecuteAsyncCount { get; private set; }
        public int ValidateInputCount { get; private set; }
        public ToolCallRequest? LastCallRequest { get; private set; }
        public string? LastExecuteInput { get; private set; }
        public string? LastValidateInput { get; private set; }

        public TestTool(
            string name = "TestTool",
            string description = "A test tool",
            ToolSchema? schema = null,
            Func<ToolCallRequest, CancellationToken, Task<ToolCallResponse>>? callFunc = null,
            Func<string, CancellationToken, Task<ToolResult>>? executeFunc = null,
            Func<string, bool>? validateFunc = null)
        {
            Name = name;
            Description = description;
            Schema = schema ?? CreateDefaultSchema();
            _callFunc = callFunc;
            _executeFunc = executeFunc;
            _validateFunc = validateFunc;
        }

        private static ToolSchema CreateDefaultSchema()
        {
            return new ToolSchema(
                "TestTool",
                "Default test tool schema",
                new Dictionary<string, ParameterSchema>
                {
                    ["input"] = new ParameterSchema("string", "Input parameter", true),
                    ["count"] = new ParameterSchema("integer", "Count parameter", false, 1)
                },
                new Dictionary<string, object?> { ["type"] = "string" });
        }

        public async System.Threading.Tasks.Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
        {
            CallAsyncCount++;
            LastCallRequest = request;

            if (_callFunc != null)
                return await _callFunc(request, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            await System.Threading.Tasks.Task.Delay(10, cancellationToken);

            return new ToolCallResponse(
                true,
                $"Processed {request.ToolName} with {request.Parameters.Count} parameters",
                null,
                new Dictionary<string, object?> { ["processed"] = true });
        }

        public async System.Threading.Tasks.Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        {
            ExecuteAsyncCount++;
            LastExecuteInput = input;

            if (_executeFunc != null)
                return await _executeFunc(input, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            await System.Threading.Tasks.Task.Delay(10, cancellationToken);

            return ToolResult.CreateSuccess($"Executed with input: {input}");
        }

        public bool ValidateInput(string input)
        {
            ValidateInputCount++;
            LastValidateInput = input;

            if (_validateFunc != null)
                return _validateFunc(input);

            return !string.IsNullOrWhiteSpace(input);
        }
    }

    [Fact]
    public void ShouldHaveRequiredProperties_WhenUsingIBaseTool()
    {
        // Arrange & Act
        var tool = new TestTool(
            name: "CalculatorTool",
            description: "Performs calculations");

        // Assert
        Assert.Equal("CalculatorTool", tool.Name);
        Assert.Equal("Performs calculations", tool.Description);
        Assert.NotNull(tool.Schema);
    }

    [Fact]
    public void ShouldContainCorrectInformation_WhenUsingIBaseToolUsingSchema()
    {
        // Arrange
        var schema = new ToolSchema(
            "MathTool",
            "Mathematical operations",
            new Dictionary<string, ParameterSchema>
            {
                ["operation"] = new ParameterSchema("string", "Operation to perform", true, null, ["add", "subtract", "multiply", "divide"]),
                ["a"] = new ParameterSchema("number", "First operand", true),
                ["b"] = new ParameterSchema("number", "Second operand", true)
            },
            new Dictionary<string, object?> { ["type"] = "number", ["description"] = "Result of the operation" });

        // Act
        var tool = new TestTool(schema: schema);

        // Assert
        Assert.Equal("MathTool", tool.Schema.Name);
        Assert.Equal("Mathematical operations", tool.Schema.Description);
        Assert.Equal(3, tool.Schema.Parameters.Count);
        Assert.True(tool.Schema.Parameters["operation"].Required);
        Assert.NotNull(tool.Schema.Parameters["operation"].Enum);
        Assert.Contains("add", tool.Schema.Parameters["operation"].Enum!);
        Assert.NotNull(tool.Schema.Returns);
        Assert.Equal("number", tool.Schema.Returns["type"]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteSuccessfully_WhenCallingAsyncWithValidRequest()
    {
        // Arrange
        var tool = new TestTool();
        var request = new ToolCallRequest(
            "TestTool",
            new Dictionary<string, object?>
            {
                ["input"] = "test data",
                ["count"] = 5
            },
            "execution context");

        // Act
        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.NotNull(response.Result);
        Assert.Null(response.Error);
        Assert.NotNull(response.Metadata);
        Assert.Equal(1, tool.CallAsyncCount);
        Assert.Equal(request, tool.LastCallRequest);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldUseCustomLogic_WhenCallingAsyncWithCustomFunction()
    {
        // Arrange
        var expectedResponse = new ToolCallResponse(
            true,
            new { calculated = 42 },
            null,
            new Dictionary<string, object?> { ["precision"] = "high" });

        var tool = new TestTool(
            callFunc: (request, ct) => System.Threading.Tasks.Task.FromResult(expectedResponse));

        var request = new ToolCallRequest(
            "Calculator",
            new Dictionary<string, object?> { ["operation"] = "compute" });

        // Act
        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedResponse, response);
        Assert.NotNull(response.Result);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnErrorResponse_WhenCallingAsyncWithError()
    {
        // Arrange
        var tool = new TestTool(
            callFunc: (request, ct) => System.Threading.Tasks.Task.FromResult(
                new ToolCallResponse(false, null, "Invalid parameters provided")));

        var request = new ToolCallRequest(
            "TestTool",
            []);

        // Act
        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.Null(response.Result);
        Assert.Equal("Invalid parameters provided", response.Error);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowOperationCanceledException_WhenCallingAsyncWithCancellation()
    {
        // Arrange
        var tool = new TestTool(
            callFunc: async (request, ct) =>
            {
                var delayTask = System.Threading.Tasks.Task.Delay(5000, ct);
                await delayTask; // This should throw when canceled
                return new ToolCallResponse(true, "result", null);
            });

        var request = new ToolCallRequest("TestTool", []);
        using var cts = new CancellationTokenSource();

        // Act - Start the call, then cancel it
        var callTask = tool.CallAsync(request, cts.Token);
        await System.Threading.Tasks.Task.Delay(10, TestContext.Current.CancellationToken); // Give the Task.Delay time to start
        await cts.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => callTask);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnSuccess_WhenExecutingAsyncWithValidInput()
    {
        // Arrange
        var tool = new TestTool();
        var input = "Process this input";

        // Act
        var result = await tool.ExecuteAsync(input, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Contains("Process this input", result.Output);
        Assert.Null(result.Error);
        Assert.Equal(1, tool.ExecuteAsyncCount);
        Assert.Equal(input, tool.LastExecuteInput);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldUseCustomLogic_WhenExecutingAsyncWithCustomFunction()
    {
        // Arrange
        var usage = new ToolUsageMetrics
        {
            TokensUsed = 100,
            ExecutionTimeMs = 250.5,
            ApiCalls = 2,
            Cost = 0.002
        };

        var tool = new TestTool(
            executeFunc: (input, ct) => System.Threading.Tasks.Task.FromResult(
                ToolResult.CreateSuccess($"Custom processing of: {input}", usage)));

        // Act
        var result = await tool.ExecuteAsync("test input", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Custom processing of: test input", result.Output);
        Assert.Equal(100, result.Usage!.TokensUsed);
        Assert.Equal(250.5, result.Usage.ExecutionTimeMs);
        Assert.Equal(2, result.Usage.ApiCalls);
        Assert.Equal(0.002, result.Usage.Cost);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnError_WhenExecutingAsyncWithError()
    {
        // Arrange
        var tool = new TestTool(
            executeFunc: (input, ct) => System.Threading.Tasks.Task.FromResult(
                ToolResult.CreateError("Failed to process input")));

        // Act
        var result = await tool.ExecuteAsync("bad input", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Equal("Failed to process input", result.Error);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowOperationCanceledException_WhenExecutingAsyncWithCancellation()
    {
        // Arrange
        var tool = new TestTool(
            executeFunc: async (input, ct) =>
            {
                var delayTask = System.Threading.Tasks.Task.Delay(5000, ct);
                await delayTask; // This should throw when canceled
                return ToolResult.CreateSuccess("Done");
            });

        using var cts = new CancellationTokenSource();

        // Act - Start the execution, then cancel it
        var executeTask = tool.ExecuteAsync("input", cts.Token);
        await System.Threading.Tasks.Task.Delay(10, TestContext.Current.CancellationToken); // Give the Task.Delay time to start
        await cts.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => executeTask);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingValidateInputWithValidInput()
    {
        // Arrange
        var tool = new TestTool();

        // Act
        var isValid = tool.ValidateInput("valid input");

        // Assert
        Assert.True(isValid);
        Assert.Equal(1, tool.ValidateInputCount);
        Assert.Equal("valid input", tool.LastValidateInput);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingValidateInputWithInvalidInput()
    {
        // Arrange
        var tool = new TestTool();

        // Act
        var isValidEmpty = tool.ValidateInput("");
        var isValidNull = tool.ValidateInput(null!);
        var isValidWhitespace = tool.ValidateInput("   ");

        // Assert
        Assert.False(isValidEmpty);
        Assert.False(isValidNull);
        Assert.False(isValidWhitespace);
        Assert.Equal(3, tool.ValidateInputCount);
    }

    [Fact]
    public void ShouldUseCustomLogic_WhenUsingValidateInputWithCustomValidator()
    {
        // Arrange
        var tool = new TestTool(
            validateFunc: input => input?.Length >= 5 && input.Length <= 100);

        // Act
        var tooShort = tool.ValidateInput("abc");
        var justRight = tool.ValidateInput("valid");
        var tooLong = tool.ValidateInput(new string('x', 101));

        // Assert
        Assert.False(tooShort);
        Assert.True(justRight);
        Assert.False(tooLong);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteAllMethods_WhenUsingIBaseToolWithCompleteWorkflow()
    {
        // Arrange
        var executionLog = new List<string>();

        var tool = new TestTool(
            name: "WorkflowTool",
            description: "Tool for complete workflow testing",
            callFunc: async (request, ct) =>
            {
                executionLog.Add($"CallAsync: {request.ToolName}");
                await System.Threading.Tasks.Task.Delay(10, ct);
                return new ToolCallResponse(true, "call result", null);
            },
            executeFunc: async (input, ct) =>
            {
                executionLog.Add($"ExecuteAsync: {input}");
                await System.Threading.Tasks.Task.Delay(10, ct);
                return ToolResult.CreateSuccess("execute result");
            },
            validateFunc: input =>
            {
                executionLog.Add($"ValidateInput: {input}");
                return true;
            });

        // Act
        // First validate the input
        var isValid = tool.ValidateInput("workflow input");

        if (isValid)
        {
            // Execute using legacy method
            var executeResult = await tool.ExecuteAsync("workflow input", TestContext.Current.CancellationToken);

            // Execute using new protocol
            var callRequest = new ToolCallRequest(
                tool.Name,
                new Dictionary<string, object?> { ["data"] = "workflow data" });
            var callResponse = await tool.CallAsync(callRequest, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(executeResult.Success);
            Assert.True(callResponse.Success);
            Assert.Equal(3, executionLog.Count);
            Assert.Contains("ValidateInput: workflow input", executionLog);
            Assert.Contains("ExecuteAsync: workflow input", executionLog);
            Assert.Contains("CallAsync: WorkflowTool", executionLog);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldOperateIndependently_WhenUsingIBaseToolWithMultipleTools()
    {
        // Arrange
        var calculatorTool = new TestTool(
            name: "Calculator",
            description: "Performs calculations",
            executeFunc: (input, ct) => System.Threading.Tasks.Task.FromResult(
                ToolResult.CreateSuccess($"Calculated: {input}")));

        var searchTool = new TestTool(
            name: "Search",
            description: "Searches for information",
            executeFunc: (input, ct) => System.Threading.Tasks.Task.FromResult(
                ToolResult.CreateSuccess($"Found: {input}")));

        var validatorTool = new TestTool(
            name: "Validator",
            description: "Validates data",
            validateFunc: input => input.StartsWith("valid"));

        // Act
        var calcResult = await calculatorTool.ExecuteAsync("2+2", TestContext.Current.CancellationToken);
        var searchResult = await searchTool.ExecuteAsync("weather", TestContext.Current.CancellationToken);
        var isValid1 = validatorTool.ValidateInput("valid data");
        var isValid2 = validatorTool.ValidateInput("invalid data");

        // Assert
        Assert.Equal("Calculated: 2+2", calcResult.Output);
        Assert.Equal("Found: weather", searchResult.Output);
        Assert.True(isValid1);
        Assert.False(isValid2);

        Assert.Equal(1, calculatorTool.ExecuteAsyncCount);
        Assert.Equal(1, searchTool.ExecuteAsyncCount);
        Assert.Equal(2, validatorTool.ValidateInputCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenUsingIBaseToolWithComplexParameters()
    {
        // Arrange
        var schema = new ToolSchema(
            "ComplexTool",
            "Tool with complex parameters",
            new Dictionary<string, ParameterSchema>
            {
                ["text"] = new ParameterSchema("string", "Text input", true),
                ["options"] = new ParameterSchema("object", "Configuration options", false,
                    new Dictionary<string, object?> { ["format"] = "json", ["validate"] = true }),
                ["tags"] = new ParameterSchema("array", "Tags", false, new List<string>()),
                ["priority"] = new ParameterSchema("number", "Priority level", false, 5,
                    [1, 2, 3, 4, 5])
            },
            new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["status"] = new Dictionary<string, object?> { ["type"] = "string" },
                    ["data"] = new Dictionary<string, object?> { ["type"] = "any" }
                }
            });

        var tool = new TestTool(
            schema: schema,
            callFunc: (request, ct) =>
            {
                // Verify complex parameters are passed correctly
                Assert.Contains("text", request.Parameters.Keys);
                Assert.Contains("options", request.Parameters.Keys);

                return System.Threading.Tasks.Task.FromResult(new ToolCallResponse(
                    true,
                    new Dictionary<string, object?>
                    {
                        ["status"] = "processed",
                        ["data"] = request.Parameters
                    },
                    null));
            });

        var request = new ToolCallRequest(
            "ComplexTool",
            new Dictionary<string, object?>
            {
                ["text"] = "Input text",
                ["options"] = new Dictionary<string, object?> { ["format"] = "markdown" },
                ["tags"] = new List<string> { "test", "complex" },
                ["priority"] = 3
            });

        // Act
        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.Success);
        Assert.NotNull(response.Result);
        var resultDict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(resultDict);
        Assert.Equal("processed", resultDict!["status"]);
    }

    [Fact]
    public void ShouldCreateCorrectInstances_WhenUsingToolResultUsingStaticMethods()
    {
        // Arrange & Act
        var successResult = ToolResult.CreateSuccess("Operation successful");
        var successWithUsage = ToolResult.CreateSuccess("With usage", new ToolUsageMetrics
        {
            TokensUsed = 50,
            ExecutionTimeMs = 100
        });
        var errorResult = ToolResult.CreateError("Operation failed");

        // Assert
        Assert.True(successResult.Success);
        Assert.Equal("Operation successful", successResult.Output);
        Assert.Null(successResult.Error);
        Assert.Null(successResult.Usage);

        Assert.True(successWithUsage.Success);
        Assert.NotNull(successWithUsage.Usage);
        Assert.Equal(50, successWithUsage.Usage.TokensUsed);

        Assert.False(errorResult.Success);
        Assert.Null(errorResult.Output);
        Assert.Equal("Operation failed", errorResult.Error);
    }
}
