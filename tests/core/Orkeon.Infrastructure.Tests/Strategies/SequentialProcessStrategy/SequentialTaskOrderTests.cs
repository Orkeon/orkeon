using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Crew;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using CrewExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Tests.Strategies;

/// <summary>
/// STUDIO-12 C2 — without a plan, the sequential strategy runs the crew's tasks in a stable
/// topological order on their declared dependencies, whichever layout the crew was written
/// in. The multi-file layout lists the tasks in the ordinal order of their file names, so
/// <c>aaa-second.yaml</c> (which depends on <c>zzz-first</c>) used to run first, silently.
/// </summary>
public sealed class SequentialTaskOrderTests : IDisposable
{
    private readonly MockAgentExecutionService _executionService = new();
    private readonly MockMemoryScope _memoryScope = new();
    private readonly List<string> _executed = [];
    private readonly RecordingLogger<SequentialProcessStrategy> _logger = new();

    public SequentialTaskOrderTests()
    {
        _executionService.SetExecuteFunc((agent, task, _, _) =>
        {
            _executed.Add(task.Description.Value);
            return new TaskResult(true, $"done: {task.Description.Value}", null, [], TimeSpan.FromMilliseconds(1));
        });
    }

    public void Dispose()
    {
        _memoryScope.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── the sheet's deterministic repro, in both layouts ──────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_dependent_task_runs_second_in_both_layouts(bool multiFile)
    {
        // Both layouts DECLARE aaa-second before zzz-first: the file names in the multi-file
        // layout, the mapping order in the single-file one. Only the dependency can put
        // zzz-first ahead.
        var fs = new FakeFileSystemService();
        string path;
        if (multiFile)
        {
            path = "/crews/repro";
            fs.AddFile($"{path}/config.yaml", "name: repro\ngoal: Order repro\nprocess: sequential");
            fs.AddFile($"{path}/agents/worker.yaml", "role: Worker\ngoal: Work");
            fs.AddFile($"{path}/tasks/aaa-second.yaml",
                "description: STEP TWO - depends on zzz-first\nexpectedOutput: two\nagent: worker\ndependencies: [zzz-first]");
            fs.AddFile($"{path}/tasks/zzz-first.yaml",
                "description: STEP ONE - must run first\nexpectedOutput: one\nagent: worker\ndependencies: []");
        }
        else
        {
            path = "/crews/repro.yaml";
            fs.AddFile(path,
                "name: repro\ngoal: Order repro\nprocess: sequential\n" +
                "agents:\n  worker:\n    role: Worker\n    goal: Work\n" +
                "tasks:\n" +
                "  aaa-second:\n    description: STEP TWO - depends on zzz-first\n    expectedOutput: two\n    agent: worker\n    dependencies: [zzz-first]\n" +
                "  zzz-first:\n    description: STEP ONE - must run first\n    expectedOutput: one\n    agent: worker\n    dependencies: []\n");
        }

        var (crew, strategy) = await BuildFromYamlAsync(fs, path, multiFile);

        var output = await strategy.ExecuteSequentialAsync(
            crew, CrewExecutionPlan.Create(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(output.Success);
        Assert.Equal(["STEP ONE - must run first", "STEP TWO - depends on zzz-first"], _executed);
    }

    // ── the sort's contract, at the strategy level ────────────────────────────────────

    [Fact]
    public async Task The_declared_order_is_kept_when_no_task_depends_on_another()
    {
        var agent = NewAgent();
        var tasks = new[] { NewTask("C"), NewTask("A"), NewTask("B") };
        var (crew, strategy) = Build(agent, tasks);

        await strategy.ExecuteSequentialAsync(
            crew, CrewExecutionPlan.Create(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["C", "A", "B"], _executed);
    }

    [Fact]
    public async Task A_dependency_on_a_task_the_crew_does_not_carry_is_ignored()
    {
        var agent = NewAgent();
        var a = NewTask("A");
        var b = NewTask("B");
        a.AddDependency(TaskId.Create());
        var (crew, strategy) = Build(agent, [a, b]);

        await strategy.ExecuteSequentialAsync(
            crew, CrewExecutionPlan.Create(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["A", "B"], _executed);
        Assert.DoesNotContain(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("circular", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_cycle_keeps_the_declared_order_and_warns_instead_of_throwing()
    {
        var agent = NewAgent();
        var a = NewTask("A");
        var b = NewTask("B");
        a.AddDependency(b.Id);
        b.AddDependency(a.Id);
        var (crew, strategy) = Build(agent, [a, b]);

        var output = await strategy.ExecuteSequentialAsync(
            crew, CrewExecutionPlan.Create(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(output.Success);
        Assert.Equal(["A", "B"], _executed);
        var warning = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("circular", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(a.Id.ToString(), warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plan_that_carries_tasks_decides_the_order_as_before()
    {
        var agent = NewAgent();
        var a = NewTask("A");
        var b = NewTask("B");
        var (crew, strategy) = Build(agent, [a, b]);
        var plan = CrewExecutionPlan.Create([b.Id, a.Id]);

        await strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["B", "A"], _executed);
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────

    private static DomainAgent NewAgent() =>
        new AgentBuilder().Role("Worker").Goal("Work").Backstory("Works").Build();

    private static CrewTask NewTask(string name) =>
        new CrewTaskBuilder().Description(name).ExpectedOutput(name).Build();

    private (DomainCrew Crew, SequentialProcessStrategy Strategy) Build(DomainAgent agent, IReadOnlyList<CrewTask> tasks)
    {
        var unitOfWork = new NullUnitOfWork();
        var taskRepository = new InMemoryTaskRepository(unitOfWork);
        var agentRepository = new InMemoryAgentRepository(unitOfWork);
        foreach (var task in tasks)
            taskRepository.AddAsync(task).GetAwaiter().GetResult();
        agentRepository.AddAsync(agent).GetAwaiter().GetResult();

        var builder = new CrewBuilder().Goal("Order").Sequential().WithAgent(agent);
        foreach (var task in tasks)
            builder.WithTask(task);

        return (builder.Build(), Strategy(taskRepository, agentRepository));
    }

    private async Task<(DomainCrew Crew, SequentialProcessStrategy Strategy)> BuildFromYamlAsync(
        FakeFileSystemService fs, string path, bool directory)
    {
        var loader = new YamlCrewDefinitionLoader(new YamlDotNetSerializer(), fs, NullLogger<YamlCrewDefinitionLoader>.Instance);
        var config = directory
            ? await loader.LoadFromDirectoryAsync(path, TestContext.Current.CancellationToken)
            : await loader.LoadFromFileAsync(path, TestContext.Current.CancellationToken);

        var unitOfWork = new NullUnitOfWork();
        var taskRepository = new InMemoryTaskRepository(unitOfWork);
        var agentRepository = new InMemoryAgentRepository(unitOfWork);
        var factory = new CrewFactory(
            loader,
            new MockToolRegistry(),
            NullLogger<CrewFactory>.Instance,
            new InMemoryCrewRepository(unitOfWork),
            agentRepository,
            taskRepository);

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);
        return (crew, Strategy(taskRepository, agentRepository));
    }

    private SequentialProcessStrategy Strategy(ITaskRepository taskRepository, IAgentRepository agentRepository)
    {
        var delegation = new AgentDelegationToolsProvider(
            new MockAgentCommunicationService(), _executionService, NullLogger<AgentDelegationToolsProvider>.Instance);

        return new SequentialProcessStrategy(
            new CrewStrategyDependencies(taskRepository, agentRepository, _executionService, _memoryScope),
            delegation,
            _logger);
    }

    /// <summary>Keeps every log line, so a warning can be asserted on and its absence too.</summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose() { }
        }
    }
}
