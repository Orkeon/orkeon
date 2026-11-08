using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Services.Security;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Security;

namespace Orkeon.Application.Tests.Services.Security;

/// <summary>FWK-06: the pipeline consults the permission gate for the calls of an agent, after the guardian.</summary>
public sealed class ToolInvocationPipelinePermissionGateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ToolInvocationCaller Agent = new("agent-1", "writer", "task-1", "crew-1");

    private static ToolInvocation Call(SpyTool tool, ToolInvocationCaller? caller = null) =>
        new(tool, new Dictionary<string, object?> { ["input"] = "x" }, caller ?? Agent);

    [Fact]
    public async System.Threading.Tasks.Task ADenyVerdict_BlocksTheCall_AndTheModelReadsTheReason()
    {
        var tool = new SpyTool("shell_command");
        var gate = new StubPermissionGate { Verdict = PermissionVerdict.Deny("not in plan mode") };
        var pipeline = new ToolInvocationPipeline(permissionGate: gate);

        var result = await pipeline.InvokeAsync(Call(tool, Agent with { PermissionMode = "plan" }), Ct);

        Assert.True(result.Blocked);
        Assert.Empty(tool.Calls);
        Assert.Contains("not in plan mode", result.ConversationText, StringComparison.Ordinal);
        Assert.StartsWith("Error: ", result.ConversationText, StringComparison.Ordinal);
        Assert.Equal(("shell_command", "plan"), Assert.Single(gate.Checks));
    }

    [Fact]
    public async System.Threading.Tasks.Task AnAllowVerdict_LetsTheCallRun()
    {
        var tool = new SpyTool("file_read");
        var pipeline = new ToolInvocationPipeline(permissionGate: new StubPermissionGate());

        var result = await pipeline.InvokeAsync(Call(tool), Ct);

        Assert.True(result.Success);
        Assert.Single(tool.Calls);
    }

    [Fact]
    public async System.Threading.Tasks.Task AnAskWithoutAChannel_IsARefusal()
    {
        var tool = new SpyTool("shell_command");
        var gate = new StubPermissionGate { Verdict = PermissionVerdict.Ask("needs approval") };
        var pipeline = new ToolInvocationPipeline(permissionGate: gate);

        var result = await pipeline.InvokeAsync(Call(tool), Ct);

        Assert.True(result.Blocked);
        Assert.Empty(tool.Calls);
        Assert.Contains("needs approval", result.ConversationText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ToolApprovalDecision.AllowOnce, true)]
    [InlineData(ToolApprovalDecision.AllowForSession, true)]
    [InlineData(ToolApprovalDecision.Deny, false)]
    public async System.Threading.Tasks.Task AnAsk_IsPutToTheChannel_WhenThereIsOne(ToolApprovalDecision decision, bool runs)
    {
        var tool = new SpyTool("shell_command");
        var gate = new StubPermissionGate { Verdict = PermissionVerdict.Ask("needs approval") };
        var channel = new StubToolApprovalChannel { Decision = decision };
        var pipeline = new ToolInvocationPipeline(permissionGate: gate, approvalChannel: channel);

        var result = await pipeline.InvokeAsync(Call(tool), Ct);

        Assert.Equal(runs, !result.Blocked);
        Assert.Equal(runs ? 1 : 0, tool.Calls.Count);
        var request = Assert.Single(channel.Requests);
        Assert.Equal("shell_command", request.ToolName);
        Assert.Equal("needs approval", request.Reason);
    }

    [Fact]
    public async System.Threading.Tasks.Task ACallerWithoutAMode_RunsUnderTheDefaultMode()
    {
        var gate = new StubPermissionGate();
        var pipeline = new ToolInvocationPipeline(permissionGate: gate, defaultMode: PermissionMode.AcceptEdits);

        await pipeline.InvokeAsync(Call(new SpyTool("file_write")), Ct);

        Assert.Equal("acceptEdits", Assert.Single(gate.Checks).Mode);
    }

    [Fact]
    public async System.Threading.Tasks.Task AnExemptCaller_IsNotPutToTheGate()
    {
        var tool = new SpyTool("shell_command");
        var gate = new StubPermissionGate { Verdict = PermissionVerdict.Deny("no") };
        var pipeline = new ToolInvocationPipeline(permissionGate: gate);

        var result = await pipeline.InvokeAsync(Call(tool, new ToolInvocationCaller("mcp", "mcp") { GateExempt = true }), Ct);

        Assert.True(result.Success);
        Assert.Empty(gate.Checks);
    }

    [Theory]
    [InlineData(ToolInvocationPipeline.DelegateWorkToolName)]
    [InlineData(ToolInvocationPipeline.AskQuestionToolName)]
    public async System.Threading.Tasks.Task ADelegationTool_IsNotPutToTheGate(string toolName)
    {
        var gate = new StubPermissionGate { Verdict = PermissionVerdict.Deny("no") };
        var pipeline = new ToolInvocationPipeline(permissionGate: gate);

        var result = await pipeline.InvokeAsync(Call(new SpyTool(toolName)), Ct);

        Assert.True(result.Success);
        Assert.Empty(gate.Checks);
    }

    [Theory]
    [InlineData("plan", "bypassPermissions", "plan")]
    [InlineData("dontAsk", "default", "dontAsk")]
    [InlineData("default", "bypassPermissions", "default")]
    [InlineData("bypassPermissions", "default", "default")]
    [InlineData("acceptEdits", "plan", "plan")]
    public async System.Threading.Tasks.Task ACoworker_RunsUnderTheStricterOfItsModeAndTheDelegators(string delegatorMode, string coworkerMode, string expected)
    {
        var gate = new StubPermissionGate();
        var pipeline = new ToolInvocationPipeline(permissionGate: gate);
        var coworker = new ToolInvocationCaller("agent-2", "editor") { PermissionMode = coworkerMode };
        var delegateTool = new ToolInvocationPipelineTests.CallbackTool(ToolInvocationPipeline.DelegateWorkToolName, async ct =>
        {
            await pipeline.InvokeAsync(Call(new SpyTool("shell_command"), coworker), ct);
        });

        await pipeline.InvokeAsync(new ToolInvocation(delegateTool, new Dictionary<string, object?>(), Agent with { PermissionMode = delegatorMode }), Ct);

        Assert.Equal(expected, Assert.Single(gate.Checks).Mode);
    }

    [Fact]
    public async System.Threading.Tasks.Task TheDelegatorsMode_DoesNotOutliveTheDelegation()
    {
        var gate = new StubPermissionGate();
        var pipeline = new ToolInvocationPipeline(permissionGate: gate);
        var delegateTool = new ToolInvocationPipelineTests.CallbackTool(ToolInvocationPipeline.DelegateWorkToolName, _ => System.Threading.Tasks.Task.CompletedTask);

        await pipeline.InvokeAsync(new ToolInvocation(delegateTool, new Dictionary<string, object?>(), Agent with { PermissionMode = "plan" }), Ct);
        await pipeline.InvokeAsync(Call(new SpyTool("shell_command"), Agent with { PermissionMode = "bypassPermissions" }), Ct);

        Assert.Equal("bypassPermissions", Assert.Single(gate.Checks).Mode);
    }

    [Fact]
    public async System.Threading.Tasks.Task AGuardianBlock_ComesBeforeTheGate()
    {
        var gate = new StubPermissionGate();
        var pipeline = new ToolInvocationPipeline(FakeGuardianPipeline.BlockingTool("shell"), permissionGate: gate);

        var result = await pipeline.InvokeAsync(Call(new SpyTool("shell")), Ct);

        Assert.True(result.Blocked);
        Assert.Empty(gate.Checks);
    }

    [Fact]
    public async System.Threading.Tasks.Task TheWaitScope_IsHeldAroundTheGateAndTheApproval()
    {
        var open = 0;
        bool? openInGate = null;
        bool? openInChannel = null;
        var gate = new StubPermissionGate { Verdict = PermissionVerdict.Ask("needs approval"), OnCheck = () => openInGate = open > 0 };
        var channel = new StubToolApprovalChannel { OnRequest = () => openInChannel = open > 0 };
        var pipeline = new ToolInvocationPipeline(permissionGate: gate, approvalChannel: channel);
        var caller = Agent with { WaitScope = () => { open++; return new Scope(() => open--); } };

        await pipeline.InvokeAsync(Call(new SpyTool("shell_command"), caller), Ct);

        Assert.True(openInGate);
        Assert.True(openInChannel);
        Assert.Equal(0, open);
    }

    [Fact]
    public async System.Threading.Tasks.Task TheWaitScope_IsNotHeldWhileTheToolRuns()
    {
        var open = 0;
        var tool = new SpyTool("file_read");
        var pipeline = new ToolInvocationPipeline(permissionGate: new StubPermissionGate());
        var caller = Agent with { WaitScope = () => { open++; return new Scope(() => open--); } };

        await pipeline.InvokeAsync(Call(tool, caller), Ct);

        Assert.Equal(0, open);
        Assert.Single(tool.Calls);
    }

    [Fact]
    public async System.Threading.Tasks.Task ACallThatRequiresApproval_IsAskedAsADefaultModeEdit()
    {
        var gate = new StubPermissionGate();
        var pipeline = new ToolInvocationPipeline(permissionGate: gate);

        await pipeline.InvokeAsync(Call(new SpyTool("file_read"), Agent with { PermissionMode = "bypassPermissions", RequireApproval = true }), Ct);

        Assert.Equal("default", Assert.Single(gate.Checks).Mode);
        Assert.Equal(Orkeon.Domain.Tools.ToolAccess.Edit, Assert.Single(gate.Accesses));
    }

    [Fact]
    public void APipeline_SaysWhetherItGates()
    {
        Assert.True(new ToolInvocationPipeline(permissionGate: new StubPermissionGate()).GatesPermissions);
        Assert.False(new ToolInvocationPipeline().GatesPermissions);
        Assert.False(ToolInvocationPipeline.Unguarded.GatesPermissions);
    }

    private sealed class Scope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
