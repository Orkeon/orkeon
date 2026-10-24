using Jint;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Security;
using Orkeon.Scripting.Bindings;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// A script tool (<c>toolBuilder()</c>, the <c>withAutonomousTool</c> path) run in act against the
/// real <see cref="ModePermissionGate"/> with a recording approval channel.
/// </summary>
public sealed class ReplAutonomousToolActTests
{
    private const string ReadExecute = "async () => await tools.fileRead({ path: '/workspace/a.txt' })";
    private const string WriteExecute = "async () => { await tools.fileWrite({ path: '/workspace/b.txt', content: 'x' }); return 'written'; }";

    private static readonly string[] Offered = ["probe_tool"];

    private sealed record Run(
        FakeToolCallingLlmProvider Provider, StubToolApprovalChannel Channel, FakeFileWriteTool Write, string Output);

    private static async Task<Run> RunAsync(string? access, string executeJs, string mode)
    {
        using var engine = new JsEngineFactory().Create();
        var fs = new FakeFileSystemService().AddMount("/workspace");
        fs.AddFile("/workspace/a.txt", "hello from a");
        var write = new FakeFileWriteTool();
        ToolsNamespaceBinding.Register(engine, new IBaseTool[] { new FakeFileReadTool(fs), write });

        var builder = new JsToolBuilder(engine).name("probe_tool").description("probe").execute(await engine.EvaluateAsync(executeJs, null, TestContext.Current.CancellationToken));
        if (access is not null) builder.access(access);
        var tool = builder.build();

        var channel = new StubToolApprovalChannel();
        var provider = new FakeToolCallingLlmProvider("probe_tool", "{}");
        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool },
            new JsLlmActGuards { PermissionGate = new ModePermissionGate(channel) });

        var options = await engine.EvaluateAsync("({ permissionMode: \"" + mode + "\" })", null, TestContext.Current.CancellationToken);
        var result = await facade.ActAsync(engine, "run the probe", options);
        return new Run(provider, channel, write, result.Get("output").AsString());
    }

    [Fact]
    [Trait("BR", "RT-18")]
    public async Task ShouldOfferReadTool_WhenAccessReadDeclared()
    {
        var run = await RunAsync("read", ReadExecute, "default");

        Assert.Equal(Offered, run.Provider.OfferedTools);
        Assert.Empty(run.Channel.Asks);
    }

    [Fact]
    [Trait("BR", "RT-18")]
    public async Task ShouldReturnFileReadValue_WhenExecuteCallsToolsFileRead()
    {
        var run = await RunAsync("read", ReadExecute, "default");

        Assert.Equal("\"hello from a\"", run.Provider.Turns[1][^1].Content);
        Assert.Equal("final: \"hello from a\"", run.Output);
    }

    [Fact]
    [Trait("BR", "RT-26")]
    public async Task ShouldDenyEditTool_WhenPermissionModeIsPlan()
    {
        var run = await RunAsync("edit", WriteExecute, "plan");

        Assert.Equal(
            "DENIED: tool 'probe_tool' is not permitted in plan mode (read-only).",
            run.Provider.Turns[1][^1].Content);
        Assert.Empty(run.Channel.Asks);
        Assert.Equal(0, run.Write.CallCount);
    }

    [Fact]
    [Trait("BR", "RT-26")]
    public async Task ShouldAskEditTool_WhenPermissionModeIsDefault()
    {
        var run = await RunAsync("edit", WriteExecute, "default");

        var ask = Assert.Single(run.Channel.Asks);
        Assert.Equal("probe_tool", ask.ToolName);
        Assert.Equal("default", ask.Mode);
        Assert.Equal("\"written\"", run.Provider.Turns[1][^1].Content);
    }

    [Fact]
    [Trait("BR", "RT-26")]
    public async Task ShouldTreatUndeclaredAccessAsWrite_WhenAccessOmitted()
    {
        var inPlan = await RunAsync(null, ReadExecute, "plan");
        var inDefault = await RunAsync(null, ReadExecute, "default");

        Assert.Equal(
            "DENIED: tool 'probe_tool' is not permitted in plan mode (read-only).",
            inPlan.Provider.Turns[1][^1].Content);
        Assert.Empty(inPlan.Channel.Asks);
        Assert.Equal("probe_tool", Assert.Single(inDefault.Channel.Asks).ToolName);
    }

    [Fact]
    [Trait("BR", "RT-18")]
    public async Task ShouldAskOnce_WhenExecuteCallsToolsFileWriteInDefaultMode()
    {
        var run = await RunAsync("edit", WriteExecute, "default");

        Assert.Equal("probe_tool", Assert.Single(run.Channel.Asks).ToolName);
        Assert.Equal(1, run.Write.CallCount);
    }

    private sealed class FakeFileReadTool : IBaseTool
    {
        private readonly FakeFileSystemService _fs;
        public FakeFileReadTool(FakeFileSystemService fs) => _fs = fs;

        public string Name => "file_read";
        public ToolAccess Access => ToolAccess.Read;
        public string Description => "read";
        public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());

        public async Task<ToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default)
        {
            var text = await _fs.TryReadAllTextAsync((string)request.Parameters["path"]!, cancellationToken);
            return new ToolCallResponse(true, text, null);
        }

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }

    private sealed class FakeFileWriteTool : IBaseTool
    {
        public string Name => "file_write";
        public ToolAccess Access => ToolAccess.Edit;
        public string Description => "write";
        public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());
        public int CallCount { get; private set; }

        public Task<ToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ToolCallResponse(true, "ok", null));
        }

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }
}
