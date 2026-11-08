using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Services.Security;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Application.Tests.Fixtures;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Security;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Security;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// FWK-06: the permission mode a YAML agent declares reaches the gate through the real path — the loader,
/// <c>CrewFactory</c>, the agent, <c>ChatToolDispatcher</c> and the tool-invocation pipeline.
/// </summary>
public sealed class YamlPermissionModeGateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Yaml(string modeLine) => $$"""
        name: gated
        goal: Answer
        process: sequential
        agents:
          writer:
            role: Writer
            goal: Write the file
        {{modeLine}}
            tools: [file_write]
        tasks:
          write:
            description: Write the file
            expected_output: A file
            agent: writer
        """;

    private static async System.Threading.Tasks.Task<DomainAgent> LoadAgentAsync(string yaml, SpyTool tool)
    {
        var loader = new YamlCrewDefinitionLoader(
            new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);
        var registry = new TestToolRegistry();
        await registry.RegisterToolAsync(tool);
        var agents = new TestAgentRepository();
        var factory = new CrewFactory(
            loader, registry, NullLogger<CrewFactory>.Instance, new TestCrewRepository(), agents, new TestTaskRepository());
        var crew = await factory.CreateFromConfigAsync(await loader.LoadFromStringAsync(yaml, Ct), Ct);
        return (await agents.GetByIdAsync(crew.Agents.Single(), Ct))!;
    }

    private static DomainTask NewTask() =>
        DomainTask.Create(TaskDescription.From("Write"), ExpectedOutput.From("Done"));

    private static async System.Threading.Tasks.Task<Orkeon.Domain.Tools.ToolUsage> CallAsync(
        DomainAgent agent, SpyTool tool, IToolInvocationPipeline pipeline)
    {
        var dispatcher = new ChatToolDispatcher(new SpyExecutionLogger(), pipeline);
        var toolsUsed = new List<Orkeon.Domain.Tools.ToolUsage>();
        var context = new ToolCallDispatchContext(
            new ChatResponse([new ChatMessage(ChatRole.Assistant, "turn")]), [], [tool], toolsUsed, agent, NewTask(), Iteration: 0);
        var call = new FunctionCallContent("call-1", "file_write", new Dictionary<string, object?> { ["input"] = "x" });

        await dispatcher.HandleNativeFunctionCallsAsync([call], context, elapsedMs: 1, Ct);

        return Assert.Single(toolsUsed);
    }

    [Theory]
    [InlineData("plan", PermissionMode.Plan)]
    [InlineData("dontAsk", PermissionMode.DontAsk)]
    public async System.Threading.Tasks.Task AToolCall_IsBlocked_WhenTheYamlAgentsModeRefusesIt(string written, PermissionMode expected)
    {
        var tool = new SpyTool("file_write");
        var agent = await LoadAgentAsync(Yaml($"    permissionMode: {written}"), tool);
        var pipeline = new ToolInvocationPipeline(permissionGate: new ModePermissionGate());

        var usage = await CallAsync(agent, tool, pipeline);

        Assert.Equal(expected, agent.PermissionMode);
        Assert.Empty(tool.Calls);
        Assert.False(usage.Success);
        Assert.Contains(written == "plan" ? "plan mode" : "dontAsk", usage.Error ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task AToolCall_Runs_WhenTheYamlAgentsModeBypassesTheGate()
    {
        var tool = new SpyTool("file_write");
        var agent = await LoadAgentAsync(Yaml("    permissionMode: bypassPermissions"), tool);
        var pipeline = new ToolInvocationPipeline(permissionGate: new ModePermissionGate());

        var usage = await CallAsync(agent, tool, pipeline);

        Assert.True(usage.Success);
        Assert.Single(tool.Calls);
    }

    [Fact]
    public async System.Threading.Tasks.Task AnAgentWithoutAMode_RunsUnderTheDefaultMode_WhichRefusesAWriteWithoutAChannel()
    {
        var tool = new SpyTool("file_write");
        var agent = await LoadAgentAsync(Yaml("    allowDelegation: false"), tool);
        var pipeline = new ToolInvocationPipeline(permissionGate: new ModePermissionGate());

        var usage = await CallAsync(agent, tool, pipeline);

        Assert.Null(agent.PermissionMode);
        Assert.Empty(tool.Calls);
        Assert.False(usage.Success);
    }

    [Fact]
    public void CallerOf_CarriesTheAgentsPermissionMode()
    {
        var agent = new AgentBuilder().Role("Writer").Goal("Write").PermissionMode(PermissionMode.AcceptEdits).Build();
        var plain = new AgentBuilder().Role("Reader").Goal("Read").Build();

        Assert.Equal("acceptEdits", ChatToolDispatcher.CallerOf(agent, NewTask()).PermissionMode);
        Assert.Null(ChatToolDispatcher.CallerOf(plain, NewTask()).PermissionMode);
    }

    [Fact]
    public async System.Threading.Tasks.Task AnUnknownModeAtThePipeline_FailsClosed()
    {
        var tool = new SpyTool("file_write");
        var pipeline = new ToolInvocationPipeline(permissionGate: new ModePermissionGate());
        var caller = new ToolInvocationCaller("agent-1", "writer") { PermissionMode = "yolo" };

        var result = await pipeline.InvokeAsync(
            new ToolInvocation(tool, new Dictionary<string, object?> { ["input"] = "x" }, caller), Ct);

        Assert.True(result.Blocked);
        Assert.Empty(tool.Calls);
    }

    [Theory]
    [InlineData("plan", true)]
    [InlineData("bypassPermissions", false)]
    public async System.Threading.Tasks.Task TheDefaultModeIsReadFromConfiguration(string defaultMode, bool blocked)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Orkeon:Security:PermissionGate:Enabled"] = "true",
            ["Orkeon:Security:PermissionGate:DefaultMode"] = defaultMode,
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOrkeonPermissionGate(configuration);
        services.AddOrkeonApplication();
        await using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IToolInvocationPipeline>();
        var tool = new SpyTool("file_write");

        var result = await pipeline.InvokeAsync(
            new ToolInvocation(tool, new Dictionary<string, object?> { ["input"] = "x" }, new ToolInvocationCaller("agent-1", "writer")), Ct);

        Assert.True(pipeline.GatesPermissions);
        Assert.Equal(blocked, result.Blocked);
        Assert.Equal(blocked ? 0 : 1, tool.Calls.Count);
    }

    [Fact]
    public void ADefaultModeThatNamesNoMode_RefusesTheRegistration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Orkeon:Security:PermissionGate:Enabled"] = "true",
            ["Orkeon:Security:PermissionGate:DefaultMode"] = "yolo",
        }).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddOrkeonPermissionGate(configuration));

        Assert.Contains("yolo", ex.Message, StringComparison.Ordinal);
    }
}
