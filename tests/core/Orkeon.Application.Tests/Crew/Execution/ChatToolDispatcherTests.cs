using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using Microsoft.Extensions.AI;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Tools;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// SONAR-14 T2: pins the IChatClient tool dispatch — native FunctionCallContent execution
/// (success, missing tool with paired call id, tool fault) and the [TOOL_CALL] text
/// fallback (execution, missing tool, fault, conversation feedback).
/// </summary>
public class ChatToolDispatcherTests
{
    private static DomainAgent BuildAgent() =>
        new AgentBuilder().Role("Dispatcher Agent").Goal("Dispatch tools").Build();

    private static DomainTask BuildTask() =>
        DomainTask.Create(TaskDescription.From("Dispatch"), ExpectedOutput.From("Done"));

    private static ToolCallDispatchContext BuildContext(
        List<IBaseTool> availableTools,
        out List<ChatMessage> messages,
        out List<ToolUsage> toolsUsed)
    {
        messages = [];
        toolsUsed = [];
        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, "assistant turn")]);
        return new ToolCallDispatchContext(
            response, messages, availableTools, toolsUsed, BuildAgent(), BuildTask(), Iteration: 0);
    }

    // ── Native FunctionCallContent dispatch ───────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task UnwrapsACallEnvelope_ThatASmallModelPutInsideTheArguments()
    {
        // llama3.2:1b on the README quickstart: the arguments carry the whole envelope.
        var tool = new SpyTool("file_write", result: "written");
        var dispatcher = new ChatToolDispatcher(new SpyExecutionLogger());
        var ctx = BuildContext([tool], out _, out var toolsUsed);
        using var payload = System.Text.Json.JsonDocument.Parse(
            """{"path":"/output/hello.md","content":"# Hello","append":"False"}""");
        var call = new FunctionCallContent("call-1b", "file_write", new Dictionary<string, object?>
        {
            ["type"] = "function",
            ["function"] = "file_write",
            ["parameters"] = payload.RootElement.Clone(),
        });

        await dispatcher.HandleNativeFunctionCallsAsync([call], ctx, elapsedMs: 5, TestContext.Current.CancellationToken);

        var parameters = Assert.Single(tool.Calls).Parameters;
        Assert.Equal("/output/hello.md", parameters["path"]?.ToString());
        Assert.Equal("# Hello", parameters["content"]?.ToString());
        Assert.False(parameters.ContainsKey("type"));
        Assert.True(Assert.Single(toolsUsed).Success);
    }

    [Fact]
    public void LeavesArgumentsAlone_WhenAKeyIsNotPartOfAnEnvelope()
    {
        var arguments = new Dictionary<string, object?> { ["type"] = "function", ["parameters"] = "x", ["path"] = "/a" };

        Assert.Same(arguments, ChatToolDispatcher.UnwrapCallEnvelope(arguments));
    }

    [Fact]
    public void UnwrapsAJsonEncodedPayload_AndKeepsAScalarOne()
    {
        var encoded = new Dictionary<string, object?> { ["function"] = "t", ["arguments"] = """{"path":"/a"}""" };
        var scalar = new Dictionary<string, object?> { ["function"] = "t", ["arguments"] = "not json" };

        Assert.Equal("/a", ChatToolDispatcher.UnwrapCallEnvelope(encoded)["path"]?.ToString());
        Assert.Same(scalar, ChatToolDispatcher.UnwrapCallEnvelope(scalar));
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecutesANativeCall_AndPairsTheResultWithItsCallId()
    {
        var tool = new SpyTool("native_tool", result: "native result");
        var dispatcher = new ChatToolDispatcher(new SpyExecutionLogger());
        var ctx = BuildContext([tool], out var messages, out var toolsUsed);
        var call = new FunctionCallContent("call-42", "native_tool",
            new Dictionary<string, object?> { ["input"] = "x" });

        await dispatcher.HandleNativeFunctionCallsAsync([call], ctx, elapsedMs: 5, TestContext.Current.CancellationToken);

        // Assistant turn first, then the tool result carrying the same call id.
        Assert.Equal("assistant turn", messages[0].Text);
        var functionResult = Assert.IsType<FunctionResultContent>(Assert.Single(messages[1].Contents));
        Assert.Equal("call-42", functionResult.CallId);
        Assert.Equal("native result", functionResult.Result?.ToString());

        var usage = Assert.Single(toolsUsed);
        Assert.True(usage.Success);
        Assert.Equal("native_tool", usage.ToolName);
        Assert.Equal("x", Assert.Single(tool.Calls).Parameters["input"]);
    }

    [Fact]
    public async System.Threading.Tasks.Task AnswersAMissingTool_WithAPairedErrorResult()
    {
        var dispatcher = new ChatToolDispatcher(new SpyExecutionLogger());
        var ctx = BuildContext([], out var messages, out var toolsUsed);
        var call = new FunctionCallContent("call-x", "ghost_tool", arguments: null);

        await dispatcher.HandleNativeFunctionCallsAsync([call], ctx, elapsedMs: 1, TestContext.Current.CancellationToken);

        var functionResult = Assert.IsType<FunctionResultContent>(Assert.Single(messages[1].Contents));
        Assert.Equal("call-x", functionResult.CallId);
        Assert.Contains("Tool 'ghost_tool' not found", functionResult.Result?.ToString(), StringComparison.Ordinal);
        Assert.Empty(toolsUsed);
    }

    [Fact]
    public async System.Threading.Tasks.Task RecordsAFailure_WhenTheNativeToolThrows()
    {
        var tool = new SpyTool("bomb", exceptionToThrow: new InvalidOperationException("native boom"));
        var dispatcher = new ChatToolDispatcher(new SpyExecutionLogger());
        var ctx = BuildContext([tool], out var messages, out var toolsUsed);
        var call = new FunctionCallContent("call-b", "bomb", arguments: null);

        await dispatcher.HandleNativeFunctionCallsAsync([call], ctx, elapsedMs: 1, TestContext.Current.CancellationToken);

        var functionResult = Assert.IsType<FunctionResultContent>(Assert.Single(messages[1].Contents));
        Assert.Equal("Error: native boom", functionResult.Result?.ToString());
        var usage = Assert.Single(toolsUsed);
        Assert.False(usage.Success);
    }

    [Fact]
    public void BuildMissingToolMessage_CarriesTheCallId()
    {
        var message = ChatToolDispatcher.BuildMissingToolMessage("id-9", "gone");

        Assert.Equal(ChatRole.Tool, message.Role);
        var content = Assert.IsType<FunctionResultContent>(Assert.Single(message.Contents));
        Assert.Equal("id-9", content.CallId);
        Assert.Contains("gone", content.Result?.ToString(), StringComparison.Ordinal);
    }

    // ── [TOOL_CALL] text fallback dispatch ────────────────────────────────

    private static List<ParsedToolCall> ParseSingleTextCall(string toolName) =>
        ToolCallTextParser.ParseToolCallBlocks(
            $$$"""[TOOL_CALL]{tool => "{{{toolName}}}", args => {--path "f.txt"}}[/TOOL_CALL]""");

    [Fact]
    public async System.Threading.Tasks.Task ExecutesATextFallbackCall_AndFeedsTheResultBack()
    {
        var tool = new SpyTool("text_tool", result: "text result");
        var dispatcher = new ChatToolDispatcher(new SpyExecutionLogger());
        var ctx = BuildContext([tool], out var messages, out var toolsUsed);

        await dispatcher.HandleTextFallbackToolCallsAsync(
            ParseSingleTextCall("text_tool"), ctx, TestContext.Current.CancellationToken);

        // Assistant turn, then a user message carrying the tool results section.
        Assert.Equal(ChatRole.User, messages[1].Role);
        Assert.Contains("[Tool text_tool result]: text result", messages[1].Text, StringComparison.Ordinal);
        Assert.Contains("Continue working on the task", messages[1].Text, StringComparison.Ordinal);

        var usage = Assert.Single(toolsUsed);
        Assert.True(usage.Success);
        Assert.Equal("f.txt", Assert.Single(tool.Calls).Parameters["path"]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ReportsAMissingTool_InTheTextFallbackResults()
    {
        var dispatcher = new ChatToolDispatcher(new SpyExecutionLogger());
        var ctx = BuildContext([], out var messages, out var toolsUsed);

        await dispatcher.HandleTextFallbackToolCallsAsync(
            ParseSingleTextCall("ghost_tool"), ctx, TestContext.Current.CancellationToken);

        Assert.Contains("[Tool 'ghost_tool' not found]", messages[1].Text, StringComparison.Ordinal);
        Assert.Empty(toolsUsed);
    }

    [Fact]
    public async System.Threading.Tasks.Task RecordsAFailure_WhenTheTextFallbackToolThrows()
    {
        var tool = new SpyTool("text_bomb", exceptionToThrow: new InvalidOperationException("text boom"));
        var dispatcher = new ChatToolDispatcher(new SpyExecutionLogger());
        var ctx = BuildContext([tool], out var messages, out var toolsUsed);

        await dispatcher.HandleTextFallbackToolCallsAsync(
            ParseSingleTextCall("text_bomb"), ctx, TestContext.Current.CancellationToken);

        Assert.Contains("[Tool text_bomb error]: text boom", messages[1].Text, StringComparison.Ordinal);
        var usage = Assert.Single(toolsUsed);
        Assert.False(usage.Success);
    }

    [Fact]
    public async System.Threading.Tasks.Task FormatsAFailedToolResponse_AsAnErrorLine()
    {
        var tool = new SpyTool("failing_tool", result: "denied", succeed: false);
        var dispatcher = new ChatToolDispatcher(new SpyExecutionLogger());
        var ctx = BuildContext([tool], out var messages, out var toolsUsed);

        await dispatcher.HandleTextFallbackToolCallsAsync(
            ParseSingleTextCall("failing_tool"), ctx, TestContext.Current.CancellationToken);

        Assert.Contains("[Tool failing_tool result]: Error: denied", messages[1].Text, StringComparison.Ordinal);
        // A failed ToolCallResponse is still a recorded (successful-dispatch) usage.
        Assert.Single(toolsUsed);
    }
}
