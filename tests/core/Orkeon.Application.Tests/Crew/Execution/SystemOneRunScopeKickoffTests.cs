using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Compaction;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Crew.Skills;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Services.Security;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;
using CrewOutput = Orkeon.Application.Interfaces.Services.CrewOutput;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// #429, #434: each kickoff opens the run's <see cref="SystemOneRunScope"/>, through the host's own composition. A host
/// that runs two crews says System One's circuit warning once in each, and a <c>decision.mode: none</c> crew's agent
/// prunes nothing although the host prunes.
/// </summary>
public sealed class SystemOneRunScopeKickoffTests
{
    private const string ToolName = "read_page";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A host with no decision model: its circuit is off.</summary>
    private static ServiceProvider Host(ConcurrentQueue<CategoryLogLine> lines, FakeProcessStrategy? seam = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<ILoggerFactory>(_ => new RecordingCategoryLoggerFactory(lines));
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddOrkeonLlmProvider(_ => new ScriptedFullLlmProvider());
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        if (seam is not null)
            services.AddScoped<IProcessStrategyFactory>(_ => seam);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Saves a crew of one agent and <paramref name="tasks"/> tasks of it in a scope of <paramref name="host"/>, then
    /// runs it there.
    /// </summary>
    private static async System.Threading.Tasks.Task<CrewOutput> RunAsync(IServiceProvider host, DomainAgent agent, CrewDecisionMode mode, int tasks = 1)
    {
        await using var scope = host.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var builder = new CrewBuilder().Goal("Review the notes").Sequential().WithAgent(agent).DecisionMode(mode);
        await services.GetRequiredService<IAgentRepository>().AddAsync(agent, Ct);
        for (var i = 1; i <= tasks; i++)
        {
            var task = new CrewTaskBuilder().Description($"Review the notes, part {i}").ExpectedOutput("A review").AssignTo(agent).Build();
            builder.WithTask(task);
            await services.GetRequiredService<ITaskRepository>().AddAsync(task, Ct);
        }

        var crew = builder.Build();
        await services.GetRequiredService<ICrewRepository>().AddAsync(crew, Ct);
        return await services.GetRequiredService<ICrewOrchestrationService>()
            .KickoffAsync(crew.Id, new CrewInput("review", new Dictionary<string, object>()), Ct);
    }

    private static DomainAgent Reviewer() => new AgentBuilder()
        .Role("Reviewer")
        .Goal("Review the notes")
        .Skills(new AgentSkills { Auto = true })
        .Build();

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldWarnOncePerRun_WhenOneHostRunsTwoCrewsWithTheCircuitOff()
    {
        // Arrange
        var lines = new ConcurrentQueue<CategoryLogLine>();
        await using var host = Host(lines);
        var first = await RunAsync(host, Reviewer(), CrewDecisionMode.Unspecified, tasks: 3);

        // Act
        var second = await RunAsync(host, Reviewer(), CrewDecisionMode.Unspecified, tasks: 3);

        // Assert: six picks over two runs, one warning in each run.
        Assert.Equal(3, first.TaskOutputs.Count);
        Assert.Equal(3, second.TaskOutputs.Count);
        Assert.Equal(2, lines.Count(l => l.Level == LogLevel.Warning
            && l.Category.EndsWith(nameof(AgentSkillsResolver), StringComparison.Ordinal)
            && l.Message.StartsWith("Agent skills with auto take no pick", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public async System.Threading.Tasks.Task ShouldPruneNothing_WhenTheKickedOffCrewAsksSystemOneNothingAndTheHostPrunes()
    {
        // Arrange: in the run, six reads of 1,000 tokens each against a 10,000-token window, on a stage that prunes.
        var model = new FakeDecisionModel
        {
            Respond = request => new DecisionResponse
            {
                Model = "fake",
                Answers = request.Questions.Keys.ToImmutableDictionary(k => k, _ => (DecisionAnswer)new NoulAnswer { Noul = 0.1 }),
            },
        };
        var seam = new FakeProcessStrategy { During = ct => ReadSixPagesAsync(model, ct) };
        await using var host = Host(new ConcurrentQueue<CategoryLogLine>(), seam);

        // Act
        await RunAsync(host, new AgentBuilder().Role("Reader").Goal("Read the pages").Build(), CrewDecisionMode.None);

        // Assert
        Assert.Equal(1, seam.Calls);
        Assert.Empty(model.Requests);
    }

    /// <summary>An agent's native loop that reads six pages on a host whose pruning is on and asks <paramref name="model"/>.</summary>
    private static async System.Threading.Tasks.Task ReadSixPagesAsync(FakeDecisionModel model, CancellationToken cancellationToken)
    {
        var logger = new SpyExecutionLogger();
        var provider = new ScriptedFullLlmProvider();
        for (var turn = 1; turn <= 6; turn++)
            provider.EnqueueOpenAiToolCall($"call-{turn}", ToolName, $"{{\"page\":{turn}}}");
        provider.EnqueueText("done");
        var services = new ServiceCollection().AddSingleton<IDecisionModel>(model).BuildServiceProvider();
        var compaction = new ContextCompactionServices
        {
            Options = new ContextCompactionOptions { KeepRecentTurns = 2 },
            Windows = new FakeContextWindowResolver(new ContextWindow(10_000, ContextWindowSources.Table)),
            Logger = logger,
            Observer = new FakeCompactionObserver(),
            Stages = [new ToolResultPruningStage(services, new ToolResultPruningOptions { Enabled = true }, null, null, new RecordingLogger())],
        }.For(LlmProfiles.Default, baseConfig: null);
        var tool = new SpyTool(ToolName, result: new string('x', 4_000)) { Access = ToolAccess.Read };
        var loop = new NativeToolCallingAgentLoop(logger, provider,
            new FakeToolCallingStrategy(new OpenAiShapedToolCallParser()), [tool],
            new LlmCallGate(logger, new ScriptedBasicLlmProvider()), ToolInvocationPipeline.Unguarded,
            new NativeToolCallingAgentLoop.NativeToolCallingLoopOptions(Compaction: compaction));
        DomainAgent agent = new AgentBuilder().Role("Reader").Goal("Read the pages").MaxIterations(10).WithTool(tool).Build();
        var task = DomainTask.Create(TaskDescription.From("Read every page"), ExpectedOutput.From("A digest"));
        await loop.ExecuteAsync(
            new ExecutionInvocationContext(agent, task, "sys", "user", Context: null, ToolsUsed: [],
                Stopwatch: System.Diagnostics.Stopwatch.StartNew()),
            cancellationToken);
    }
}
