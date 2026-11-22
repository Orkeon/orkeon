using System.Text.Json.Nodes;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Services.Security;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Execution;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Interop.AgentCli.Engine;
using Orkeon.Interop.AgentCli.Sessions;
using Orkeon.Interop.AgentCli.Tests.Doubles;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Interop.AgentCli.Tests.Loops;

/// <summary>
/// The two loops of Orkeon that call a model with tools - <c>act</c> of a script and the crew's chat loop -
/// run on the agent-cli provider: the tool call comes back as Anthropic <c>tool_use</c>, the permission gate
/// refuses it, and the refusal reaches the CLI's waiting <c>tools/call</c> as an error result.
/// </summary>
public sealed class AgentCliLoopTests : IAsyncDisposable
{
    private readonly FakeAgentCliLauncher _launcher = new();
    private readonly List<AgentCliProcessHost> _hosts = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
            await host.DisposeAsync();
    }

    private AgentCliLlmProvider Provider()
    {
        var options = new AgentCliOptions();
        var logger = new RecordingLogger();
        var host = new AgentCliProcessHost(_launcher, new ClaudeCodeEngine(options), options, logger);
        _hosts.Add(host);
        return new AgentCliLlmProvider(host, LlmConfig.Create("haiku"), logger);
    }

    /// <summary>
    /// Plays the CLI for one denied call: a turn ending on <c>file_write</c>, the CLI's <c>tools/call</c>, then
    /// the final answer once the bridge has answered. Returns the bridge's answer to the call.
    /// </summary>
    private async Task<JsonObject> PlayDeniedWriteAsync()
    {
        var process = await _launcher.NextAsync();
        await process.NextInputAsync();
        await process.EmitAsync(StreamLines.Init());
        await process.EmitAsync(StreamLines.ToolUse("m1", "toolu_1", "file_write", new JsonObject { ["path"] = "a.txt" }));
        await process.EmitAsync(StreamLines.Delta("tool_use"));
        await process.EmitAsync(StreamLines.Stop());
        await process.Connection.CallAsync(1, "file_write", "toolu_1", new JsonObject { ["path"] = "a.txt" });

        var answer = await process.Connection.NextAsync();
        await process.EmitAsync(StreamLines.Text("m2", "The write was refused."));
        await process.EmitAsync(StreamLines.Delta("end_turn"));
        await process.EmitAsync(StreamLines.Stop());
        await process.EmitAsync(StreamLines.Result("The write was refused."));
        return answer;
    }

    [Fact]
    public async Task ShouldSendDenialAsErrorResult_WhenActToolIsDenied()
    {
        await using var scope = AgentRunScope.Begin("a1", "Writer");
        using var engine = new Jint.Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var gate = new FakePermissionGate { Verdict = PermissionVerdict.Deny("no writes") };
        var tool = new FakeWriteTool();
        var facade = new JsLlmFacade(
            engine, Provider(), CancellationToken.None, new IBaseTool[] { tool },
            new JsLlmActGuards { PermissionGate = gate },
            new JsLlmObservability { ToolInvocation = new ToolInvocationPipeline(permissionGate: gate), AgentName = "writer" });

        // act is a JS async function: invoked as a script does, its promise settled by this test.
        var act = engine.Invoke(facade.act, "Write a.txt").UnwrapIfPromiseAsync(TestContext.Current.CancellationToken);
        var answer = await PlayDeniedWriteAsync();
        await act;

        Assert.Equal(0, tool.CallCount);
        Assert.Equal("file_write", Assert.Single(gate.Tools));
        Assert.True(answer["result"]!["isError"]!.GetValue<bool>());
        Assert.StartsWith("DENIED", answer["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Single(_launcher.Started);
    }

    [Fact]
    public async Task ShouldSendDenialAsErrorResult_WhenChatLoopToolIsDenied()
    {
        await using var scope = AgentRunScope.Begin("a1", "Writer");
        var gate = new FakePermissionGate { Verdict = PermissionVerdict.Deny("no writes") };
        var tool = new FakeWriteTool();
        var pipeline = new ToolInvocationPipeline(permissionGate: gate);
        var logger = NullLogger.Instance;
        using var client = new LlmProviderToChatClientAdapter(Provider());
        var loop = new ChatClientAgentLoop(
            logger,
            client,
            new LlmCallGate(logger, new StubBasicLlmProvider()),
            new ChatOptionsComposer(logger, [tool], new FakeFileSystemService(), pipeline),
            new ChatToolDispatcher(logger, pipeline));
        var agent = new AgentBuilder().Role("Writer").Goal("Write a file").MaxIterations(5).WithTool(tool).Build();
        var task = DomainTask.Create(TaskDescription.From("Write a.txt"), ExpectedOutput.From("A file"));

        var run = loop.ExecuteAsync(agent, task, "You are terse.", "Write a.txt", [], TestContext.Current.CancellationToken);
        var answer = await PlayDeniedWriteAsync();
        var result = await run;

        Assert.Equal(0, tool.CallCount);
        Assert.Equal("file_write", Assert.Single(gate.Tools));
        Assert.True(answer["result"]!["isError"]!.GetValue<bool>());
        Assert.Equal("Error: Blocked by the permission gate: no writes", answer["result"]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("The write was refused.", result.Output);
        Assert.Single(_launcher.Started);
    }
}
