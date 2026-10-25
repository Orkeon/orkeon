using Jint;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Security;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>RT-04: <c>act(prompt, {onToolCall})</c> is called before each tool call, after the permission gate.</summary>
public sealed class ActToolCallbacksTests
{
    private sealed record Run(
        FakeToolCallingLlmProvider Provider, FakeRecordingTool Tool, StubSequencePermissionGate Gate, StubToolApprovalChannel Channel, List<string> Log, string Output, int Seen, string Recorded)
    {
        public string ToolResultOfTurn2 => Provider.Turns[1][^1].Content;
    }

    private static string Counting(string answer)
        => "(call) => { globalThis.seen = (globalThis.seen ?? 0) + 1; return '" + answer + "'; }";

    private static async Task<Run> RunAsync(string onToolCallJs, Func<IReadOnlyDictionary<string, object?>, bool>? gateDenies = null,
        ToolApprovalDecision channelAnswer = ToolApprovalDecision.AllowOnce, string? onToolResultJs = null, bool registerGate = true, string calledTool = "file_read")
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var log = new List<string>();
        var tool = new FakeRecordingTool("file_read", log);
        var channel = new StubToolApprovalChannel { Decision = channelAnswer };
        var gate = new StubSequencePermissionGate(new ModePermissionGate(channel), log, gateDenies);
        var provider = new FakeToolCallingLlmProvider(calledTool, "{\"path\":\"/x\"}");
        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, registerGate ? new JsLlmActGuards { PermissionGate = gate } : new JsLlmActGuards());

        var options = await engine.EvaluateAsync("({ permissionMode: \"default\", onToolCall: " + onToolCallJs
            + (onToolResultJs is null ? "" : ", onToolResult: " + onToolResultJs) + " })", null, TestContext.Current.CancellationToken);
        var result = await facade.ActAsync(engine, "read it", options);
        var seen = (int)(await engine.EvaluateAsync("globalThis.seen ?? 0", null, TestContext.Current.CancellationToken)).AsNumber();
        var recorded = (await engine.EvaluateAsync("globalThis.rec === undefined ? '' : String(globalThis.rec)", null, TestContext.Current.CancellationToken)).AsString();
        return new Run(provider, tool, gate, channel, log, result.Get("output").AsString(), seen, recorded);
    }

    [Fact]
    [Trait("BR", "RT-04")]
    public async Task ShouldRunTool_WhenOnToolCallAllows()
    {
        var run = await RunAsync(Counting("allow"));

        Assert.Equal(1, run.Seen);
        Assert.Equal(["/x"], run.Tool.Inputs);
        Assert.Equal("final: " + run.ToolResultOfTurn2, run.Output);
        Assert.Equal("{\"ok\":true}", run.ToolResultOfTurn2);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async Task ShouldRefuseWithoutGateNorCallback_WhenToolIsNotOffered()
    {
        var run = await RunAsync(Counting("allow"), calledTool: "read");

        Assert.Equal(0, run.Seen);
        Assert.Empty(run.Gate.Paths);
        Assert.Empty(run.Tool.Inputs);
        Assert.Equal("ERROR: tool 'read' is not available to this agent.", run.ToolResultOfTurn2);
    }

    [Fact]
    [Trait("BR", "RT-04")]
    public async Task ShouldBlockWithReason_WhenOnToolCallDenies()
    {
        var run = await RunAsync("(call) => ({ deny: 'no' })");

        Assert.Empty(run.Tool.Inputs);
        Assert.Equal("DENIED: no", run.ToolResultOfTurn2);
    }

    [Fact]
    [Trait("BR", "RT-04")]
    public async Task ShouldRunRewrittenInputAfterRegate_WhenOnToolCallRewrites()
    {
        var run = await RunAsync("(call) => ({ input: { path: '/y' } })");

        Assert.Equal(["/x", "/y"], run.Gate.Paths);
        Assert.Equal(["/y"], run.Tool.Inputs);
    }

    [Fact]
    [Trait("BR", "RT-04")]
    public async Task ShouldDenyWithMessage_WhenOnToolCallThrows()
    {
        var run = await RunAsync("(call) => { throw new Error('boom'); }");

        Assert.Empty(run.Tool.Inputs);
        Assert.Equal("DENIED: boom", run.ToolResultOfTurn2);
    }

    [Fact]
    [Trait("BR", "RT-04")]
    public async Task ShouldRunGateBeforeCallback_WhenBothApply()
    {
        var allowed = await RunAsync(Counting("allow"));
        var denied = await RunAsync(Counting("allow"), gateDenies: _ => true);

        Assert.Equal(1, allowed.Seen);
        Assert.Equal(0, denied.Seen);
        Assert.Equal(["gate:/x", "tool:/x"], allowed.Log);
        Assert.Equal(["gate:/x"], denied.Log);
        Assert.Equal("DENIED: gate says no", denied.ToolResultOfTurn2);
    }

    [Theory]
    [Trait("BR", "RT-04")]
    [InlineData(ToolApprovalDecision.AllowOnce, 1, true)]
    [InlineData(ToolApprovalDecision.Deny, 0, false)]
    public async Task ShouldHandOffToApprovalChannel_WhenCallbackAnswersAsk(ToolApprovalDecision answer, int toolRuns, bool toolResultReachesModel)
    {
        var run = await RunAsync(Counting("ask"), channelAnswer: answer);

        var ask = Assert.Single(run.Channel.Asks);
        Assert.Equal("file_read", ask.ToolName);
        Assert.Equal(toolRuns, run.Tool.Inputs.Count);
        Assert.Equal(toolResultReachesModel, run.ToolResultOfTurn2 == "{\"ok\":true}");
        Assert.Equal(!toolResultReachesModel, run.ToolResultOfTurn2.StartsWith("DENIED:", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "RT-04")]
    public async Task ShouldPassResultToOnToolResult_WhenToolCompletes()
    {
        var run = await RunAsync(Counting("allow"),
            onToolResultJs: "(call, result) => { globalThis.rec = (globalThis.rec ?? []).concat([call.name + '|' + JSON.stringify(call.input) + '|' + JSON.stringify(result)]); }");

        Assert.Equal(["/x"], run.Tool.Inputs);
        Assert.Equal("{\"ok\":true}", run.ToolResultOfTurn2);
        Assert.Equal(1, run.Seen);
        Assert.Equal("file_read|{\"path\":\"/x\"}|{\"ok\":true}", run.Recorded);
    }

    [Fact]
    [Trait("BR", "RT-04")]
    public async Task ShouldReplaceContextLine_WhenOnToolResultReturnsContext()
    {
        var replaced = await RunAsync(Counting("allow"), onToolResultJs: "(call, result) => ({ context: 'summary' })");
        var untouched = await RunAsync(Counting("allow"), onToolResultJs: "(call, result) => { globalThis.rec = 'ran'; }");

        Assert.Equal("summary", replaced.ToolResultOfTurn2);
        Assert.Equal("{\"ok\":true}", untouched.ToolResultOfTurn2);
        Assert.Equal("ran", untouched.Recorded);
    }

    [Theory]
    [Trait("BR", "RT-04")]
    [InlineData("(call) => {}")]
    [InlineData("(call) => null")]
    [InlineData("(call) => ({ input: 'x' })")]
    [InlineData("(call) => ({ input: [1] })")]
    [InlineData("(call) => ({ input: null })")]
    public async Task ShouldDenyAndNotReject_WhenOnToolCallReturnsUnknownVerdict(string onToolCallJs)
    {
        var run = await RunAsync(onToolCallJs);

        Assert.Empty(run.Tool.Inputs);
        Assert.StartsWith("DENIED:", run.ToolResultOfTurn2, StringComparison.Ordinal);
        Assert.Contains("onToolCall", run.ToolResultOfTurn2, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-04")]
    public async Task ShouldDeny_WhenCallbackAsksAndNoPermissionGateRegistered()
    {
        var run = await RunAsync(Counting("ask"), registerGate: false);

        Assert.Empty(run.Tool.Inputs);
        Assert.StartsWith("DENIED:", run.ToolResultOfTurn2, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-04")]
    public async Task ShouldStayOffEngineThread_WhenCallbacksRunInLoop()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var probe = new FakeOverlapProbe();
        engine.SetValue("__probe", new Action<string>(probe.InCallback));
        var log = new List<string>();
        var tool = new FakeProbedTool("file_read", log, probe);
        var gate = new StubSequencePermissionGate(new ModePermissionGate(new StubToolApprovalChannel()), log, null);
        var provider = new FakeToolCallingLlmProvider("file_read", "{\"path\":\"/x\"}");
        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards { PermissionGate = gate });

        var options = await engine.EvaluateAsync(
            "({ permissionMode: 'default', onToolCall: (c) => { __probe('call'); return 'allow'; }, onToolResult: (c, r) => { __probe('result'); } })",
            null, TestContext.Current.CancellationToken);
        await facade.ActAsync(engine, "read it", options);

        Assert.Equal(["call", "tool", "result"], probe.Events);
        Assert.Equal(1, probe.MaxActive);
    }








}
