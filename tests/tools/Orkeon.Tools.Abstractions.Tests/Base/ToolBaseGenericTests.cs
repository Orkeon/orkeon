using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Abstractions.Tests.Base;

#region Test helpers

public record ToolTestRequest
{
    public string Query { get; init; } = "";
    public int MaxResults { get; init; } = 10;
    public bool Verbose { get; init; }
}

public record ToolTestResponse
{
    public string Answer { get; init; } = "";
    public int ResultCount { get; init; }
}

/// <summary>
/// Concrete test tool inheriting from ToolBase&lt;TReq, TRes&gt;.
/// </summary>
public class TestGenericTool : ToolBase<ToolTestRequest, ToolTestResponse>
{
    public string? ValidationError { get; set; }
    public ToolTestRequest? LastRequest { get; private set; }

    public TestGenericTool(ILogger? logger = null) : base(logger) { }

    public override string Name => "test_generic_tool";
    public override string Description => "A test tool for unit testing ToolBase<TReq, TRes>";

    public override ToolSchema Schema => new(
        Name: "test_generic_tool",
        Description: "A test tool",
        Parameters: new Dictionary<string, ParameterSchema>
        {
            [ParamQuery] = new ParameterSchema("string", "The search query", Required: true),
            ["max_results"] = new ParameterSchema("integer", "Maximum results", Required: false, Default: 10),
            ["verbose"] = new ParameterSchema("boolean", "Verbose output", Required: false, Default: false)
        },
        Returns: new Dictionary<string, object?>
        {
            ["answer"] = new { type = "string", description = "The answer" },
            ["result_count"] = new { type = "integer", description = "Number of results" }
        }
    );

    protected override Task<ToolTestResponse> ExecuteTypedAsync(ToolTestRequest request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(new ToolTestResponse
        {
            Answer = $"Found: {request.Query}",
            ResultCount = request.MaxResults
        });
    }

    protected override string? ValidateTypedRequest(ToolTestRequest request)
        => ValidationError;
}

/// <summary>
/// Tool with output filtering (returns schema limits output keys).
/// </summary>
public class FilteredOutputTool : ToolBase<ToolTestRequest, ToolTestResponse>
{
    public FilteredOutputTool(ILogger? logger = null) : base(logger) { }

    public override string Name => "filtered_tool";
    public override string Description => "Tool with output filtering";

    public override ToolSchema Schema => new(
        Name: "filtered_tool",
        Description: "Tool with filtered output",
        Parameters: new Dictionary<string, ParameterSchema>
        {
            [ParamQuery] = new ParameterSchema("string", "Query", Required: true)
        },
        Returns: new Dictionary<string, object?>
        {
            ["answer"] = new { type = "string" }
            // result_count is NOT in returns, so it should be filtered out
        }
    );

    protected override Task<ToolTestResponse> ExecuteTypedAsync(ToolTestRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ToolTestResponse
        {
            Answer = $"Result: {request.Query}",
            ResultCount = 99
        });
    }
}

#endregion

public sealed class ToolBaseGenericTests : IDisposable
{
    private readonly TestGenericTool _tool;

    public ToolBaseGenericTests()
    {
        _tool = new TestGenericTool(NullLogger.Instance);
    }

    public void Dispose() => _tool.Dispose();

    [Fact]
    public async Task ShouldReturnSuccessResponse_WhenExecutingWithValidDict()
    {
        var request = new ToolCallRequest(
            ToolName: "test_generic_tool",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "hello world",
                ["max_results"] = 5,
                ["verbose"] = true
            });

        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);
        Assert.NotNull(_tool.LastRequest);
        Assert.Equal("hello world", _tool.LastRequest!.Query);
        Assert.Equal(5, _tool.LastRequest.MaxResults);
        Assert.True(_tool.LastRequest.Verbose);
    }

    [Fact]
    public async Task ShouldUseDefaults_WhenExecutingWithMinimalParams()
    {
        var request = new ToolCallRequest(
            ToolName: "test_generic_tool",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "test"
            });

        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        Assert.NotNull(_tool.LastRequest);
        Assert.Equal("test", _tool.LastRequest!.Query);
        // Default values from YAML schema should be injected
        Assert.Equal(10, _tool.LastRequest.MaxResults);
    }

    [Fact]
    public async Task ShouldReturnFailure_WhenValidationErrorOccurs()
    {
        _tool.ValidationError = "Query cannot be empty";

        var request = new ToolCallRequest(
            ToolName: "test_generic_tool",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = ""
            });

        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Contains("Query cannot be empty", response.Error);
    }

    [Fact]
    public async Task ShouldContainCorrectData_WhenExecutionSucceeds()
    {
        var request = new ToolCallRequest(
            ToolName: "test_generic_tool",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "cats",
                ["max_results"] = 3
            });

        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        // Result should be a dictionary with answer and result_count
        var resultDict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(resultDict);
        Assert.Equal("Found: cats", resultDict!["answer"]?.ToString());
    }

    [Fact]
    public async Task ShouldIncludeOnlyDeclaredReturns_WhenOutputFilteringIsActive()
    {
        using var tool = new FilteredOutputTool(NullLogger.Instance);
        var request = new ToolCallRequest(
            ToolName: "filtered_tool",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "test"
            });

        var response = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        var resultDict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(resultDict);
        Assert.True(resultDict!.ContainsKey("answer"));
        Assert.False(resultDict.ContainsKey("result_count")); // Filtered out
    }

    [Fact]
    public async Task ShouldInjectYamlDefaults_WhenOptionalParamsAreMissing()
    {
        // Schema declares max_results with Default: 10 and verbose with Default: false
        var request = new ToolCallRequest(
            ToolName: "test_generic_tool",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "test"
                // max_results and verbose are missing - should get defaults from schema
            });

        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        Assert.NotNull(_tool.LastRequest);
        Assert.Equal(10, _tool.LastRequest!.MaxResults);
        Assert.False(_tool.LastRequest.Verbose);
    }

    [Fact]
    public async Task ShouldReturnSuccessResult_WhenCalledWithValidParams()
    {
        var request = new ToolCallRequest(
            ToolName: "test_generic_tool",
            Parameters: new Dictionary<string, object?>
            {
                [ParamQuery] = "search term",
                ["max_results"] = 7
            });

        var response = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        var resultDict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(resultDict);
        Assert.Equal("Found: search term", resultDict!["answer"]?.ToString());
    }

    [Fact]
    public void ShouldReturnCorrectValues_WhenAccessingToolProperties()
    {
        Assert.Equal("test_generic_tool", _tool.Name);
        Assert.Equal("A test tool for unit testing ToolBase<TReq, TRes>", _tool.Description);
        Assert.NotNull(_tool.Schema);
        Assert.Equal(3, _tool.Schema.Parameters.Count);
    }
}
