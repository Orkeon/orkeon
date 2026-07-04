using Microsoft.Extensions.AI;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Abstractions.Adapters;
using Orkeon.Tools.Abstractions.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Abstractions.Tests.Adapters;

public class BaseToolToAIFunctionAdapterTests
{
    private static MockBaseTool CreateMockTool(
        string name = "test_tool",
        string description = "A test tool",
        bool success = true,
        object? result = null)
    {
        var mock = new MockBaseTool();
        mock.Name = name;
        mock.Description = description;
        mock.Schema = new ToolSchema(
            name, description,
            new Dictionary<string, ParameterSchema>
            {
                ["input"] = new ParameterSchema("string", "The input value", true)
            });

        result ??= "tool result";
        mock.SetCallResult(new ToolCallResponse(success, result, success ? null : "error"));

        return mock;
    }

    [Fact]
    public void ShouldCreateFunctionWithCorrectName_WhenConvertingToAIFunction()
    {
        var mock = CreateMockTool(name: "my_tool");
        var aiFunction = BaseToolToAIFunctionAdapter.ToAIFunction(mock);

        Assert.Equal("my_tool", aiFunction.Name);
    }

    [Fact]
    public void ShouldCreateFunctionWithCorrectDescription_WhenConvertingToAIFunction()
    {
        var mock = CreateMockTool(description: "Does something useful");
        var aiFunction = BaseToolToAIFunctionAdapter.ToAIFunction(mock);

        Assert.Equal("Does something useful", aiFunction.Description);
    }

    [Fact]
    public async Task ShouldInvokeUnderlyingTool_WhenCallingAIFunction()
    {
        var mock = CreateMockTool(result: "hello world");
        var aiFunction = BaseToolToAIFunctionAdapter.ToAIFunction(mock);

        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["input"] = "test"
        });
        var result = await aiFunction.InvokeAsync(args, TestContext.Current.CancellationToken);

        Assert.Equal("hello world", result?.ToString());
        Assert.Equal(1, mock.CallAsyncCallCount);
        Assert.Equal("test_tool", mock.LastCallRequest!.ToolName);
    }

    [Fact]
    public async Task ShouldReturnErrorMessage_WhenToolFails()
    {
        var mock = CreateMockTool(success: false);
        mock.SetCallResult(new ToolCallResponse(false, null, "Something went wrong"));

        var aiFunction = BaseToolToAIFunctionAdapter.ToAIFunction(mock);
        var result = await aiFunction.InvokeAsync([], TestContext.Current.CancellationToken);

        Assert.Contains("Error:", result?.ToString());
        Assert.Contains("Something went wrong", result?.ToString());
    }

    [Fact]
    public async Task ShouldPassArguments_WhenInvokingAIFunction()
    {
        var mock = CreateMockTool();
        mock.SetCallResult(new ToolCallResponse(true, "ok", null));

        var aiFunction = BaseToolToAIFunctionAdapter.ToAIFunction(mock);
        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            [ParamPath] = "/tmp/file.txt",
            ["encoding"] = "utf-8"
        });
        await aiFunction.InvokeAsync(args, TestContext.Current.CancellationToken);

        Assert.NotNull(mock.LastCallRequest);
        Assert.Equal("test_tool", mock.LastCallRequest!.ToolName);
        Assert.Equal("/tmp/file.txt", mock.LastCallRequest.Parameters[ParamPath]);
        Assert.Equal("utf-8", mock.LastCallRequest.Parameters["encoding"]);
    }

    [Fact]
    public void ShouldConvertMultipleTools_WhenCallingToAITools()
    {
        var tools = new IBaseTool[]
        {
            CreateMockTool("tool1"),
            CreateMockTool("tool2"),
            CreateMockTool("tool3")
        };

        var aiTools = BaseToolToAIFunctionAdapter.ToAITools(tools);

        Assert.Equal(3, aiTools.Count);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenConvertingEmptyToolList()
    {
        var aiTools = BaseToolToAIFunctionAdapter.ToAITools(Array.Empty<IBaseTool>());
        Assert.Empty(aiTools);
    }

    [Fact]
    public async Task ShouldRespectCancellation_WhenTokenIsCancelled()
    {
        var mock = CreateMockTool();
        mock.SetCallFunc(async (_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return new ToolCallResponse(true, "ok", null);
        });

        var aiFunction = BaseToolToAIFunctionAdapter.ToAIFunction(mock);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var args = new AIFunctionArguments { Services = null };
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            aiFunction.InvokeAsync(args, cts.Token).AsTask());
    }
}
