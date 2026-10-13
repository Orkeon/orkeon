using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// CH-03 - the Information lines of the turn of a verbose agent (or of any agent while the run's crew is
/// verbose) go under the logger category <c>Orkeon.Verbose</c>; every other line keeps the orchestrator's.
/// </summary>
public sealed class VerboseAgentLogTests
{
    private const string OrchestratorCategory = "Orkeon.Application.Crew.ExecutionOrchestrator";

    private static readonly string[] TurnPrefixes = ["Tool call >>", "Tool result <<", "LLM response"];

    private static DomainAgent BuildAgent(bool verbose, SpyTool tool) =>
        new AgentBuilder().Role("Talker").Goal("Talk").MaxIterations(5).Verbose(verbose).WithTool(tool).Build();

    private static async System.Threading.Tasks.Task<List<CategoryLogLine>> RunTurnAsync(
        DomainAgent agent, string toolNameCalled = "worker")
    {
        var lines = new ConcurrentQueue<CategoryLogLine>();
        var provider = new ScriptedFullLlmProvider();
        provider.EnqueueOpenAiToolCall("call-1", toolNameCalled, "{\"input\":\"x\"}");
        provider.EnqueueText("the answer");
        using var chatClient = new LlmProviderToChatClientAdapter(provider);
        var orchestrator = new ExecutionOrchestrator(
            new RecordingCategoryLogger(OrchestratorCategory, lines), new LlmProviderAdapter(provider),
            chatClient, [new SpyTool("worker")], new FakeFileSystemService(),
            new RecordingCategoryLogger(ExecutionLogCategories.Verbose, lines));
        var task = DomainTask.Create(TaskDescription.From("Do it"), ExpectedOutput.From("An answer"));

        await orchestrator.ExecuteTaskCoreAsync(
            agent, task, new SimpleExecutionContext(CrewId.Create(), [], NullMemoryScope.Instance, []),
            TestContext.Current.CancellationToken);
        return [.. lines];
    }

    private static bool IsTurnLine(CategoryLogLine l) =>
        l.Level == LogLevel.Information && TurnPrefixes.Any(p => l.Message.StartsWith(p, StringComparison.Ordinal));

    private static void AssertTurnLinesUnder(List<CategoryLogLine> lines, string category, string other)
    {
        foreach (var prefix in TurnPrefixes)
        {
            Assert.Contains(lines, l => l.Category == category && l.Level == LogLevel.Information
                && l.Message.StartsWith(prefix, StringComparison.Ordinal));
        }

        Assert.DoesNotContain(lines, l => l.Category == other && IsTurnLine(l));
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public async System.Threading.Tasks.Task ShouldWriteTurnLinesUnderVerboseCategory_WhenAgentIsVerbose()
    {
        var lines = await RunTurnAsync(BuildAgent(true, new SpyTool("worker")));

        AssertTurnLinesUnder(lines, ExecutionLogCategories.Verbose, OrchestratorCategory);
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public async System.Threading.Tasks.Task ShouldWriteTurnLinesUnderOrchestrator_WhenAgentIsNotVerbose()
    {
        var lines = await RunTurnAsync(BuildAgent(false, new SpyTool("worker")));

        AssertTurnLinesUnder(lines, OrchestratorCategory, ExecutionLogCategories.Verbose);
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public async System.Threading.Tasks.Task ShouldMakeEveryAgentVerbose_WhenRunsCrewIsVerbose()
    {
        using var scope = CrewVerbosity.Begin(true);

        var lines = await RunTurnAsync(BuildAgent(false, new SpyTool("worker")));

        AssertTurnLinesUnder(lines, ExecutionLogCategories.Verbose, OrchestratorCategory);
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public async System.Threading.Tasks.Task ShouldKeepPromptsAtDebugUnderOrchestrator_WhenAgentIsVerbose()
    {
        var lines = await RunTurnAsync(BuildAgent(true, new SpyTool("worker")));

        Assert.Contains(lines, l => l.Category == ExecutionLogCategories.Verbose && IsTurnLine(l));
        Assert.Contains(lines, l => l.Category == OrchestratorCategory && l.Level == LogLevel.Debug
            && l.Message.Contains("prompt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, l => l.Category == ExecutionLogCategories.Verbose && l.Level == LogLevel.Debug);
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public async System.Threading.Tasks.Task ShouldKeepWarningCategory_WhenVerboseAgentWarns()
    {
        var lines = await RunTurnAsync(BuildAgent(true, new SpyTool("worker")), toolNameCalled: "ghost");

        Assert.Contains(lines, l => l.Category == OrchestratorCategory && l.Level == LogLevel.Warning);
        Assert.Contains(lines, l => l.Category == ExecutionLogCategories.Verbose && IsTurnLine(l));
        Assert.DoesNotContain(lines, l => l.Category == ExecutionLogCategories.Verbose && l.Level == LogLevel.Warning);
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void ShouldRestoreEnclosingVerbosity_WhenNestedRunEnds()
    {
        var plain = new AgentBuilder().Role("Plain").Goal("Talk").Build();
        var outer = CrewVerbosity.Begin(true);
        try
        {
            CrewVerbosity.Begin(false).Dispose();

            Assert.True(CrewVerbosity.IsVerbose(plain));
        }
        finally
        {
            outer.Dispose();
        }

        Assert.False(CrewVerbosity.IsVerbose(plain));
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public async System.Threading.Tasks.Task ShouldWriteTurnLinesUnderVerboseCategory_WhenHostHasNoChatClient()
    {
        var lines = new ConcurrentQueue<CategoryLogLine>();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(_ => new RecordingCategoryLoggerFactory(lines));
        services.AddSingleton<ILogger<ExecutionOrchestrator>>(new RecordingCategoryLogger(OrchestratorCategory, lines));
        services.AddSingleton(typeof(ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddSingleton<IBasicLlmProvider>(new ScriptedBasicLlmProvider());
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddOrkeonApplication();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var orchestrator = Assert.IsType<ExecutionOrchestrator>(scope.ServiceProvider.GetRequiredService<IExecutionOrchestrator>());
        var task = DomainTask.Create(TaskDescription.From("Do it"), ExpectedOutput.From("An answer"));

        await orchestrator.ExecuteTaskCoreAsync(
            BuildAgent(true, new SpyTool("worker")), task,
            new SimpleExecutionContext(CrewId.Create(), [], NullMemoryScope.Instance, []),
            TestContext.Current.CancellationToken);

        var all = lines.ToList();
        Assert.Contains(all, l => l.Category == ExecutionLogCategories.Verbose && l.Level == LogLevel.Information
            && l.Message.StartsWith("LLM response", StringComparison.Ordinal));
        Assert.DoesNotContain(all, l => l.Category == OrchestratorCategory && IsTurnLine(l));
    }
}
