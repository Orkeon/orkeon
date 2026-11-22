using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Crew;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>A crew loaded from YAML carries its decision tasks and conditions, and runs them.</summary>
public sealed class DecisionTaskFactoryTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly MockMemoryScope _memoryScope = new();

    private const string CrewYaml = """
name: support
goal: Handle tickets
process: sequential
agents:
  writer: { role: Writer, goal: Write the answer }
tasks:
  intake:
    description: Read the ticket
    expectedOutput: A summary
    agent: writer
  triage:
    description: Classify the change.
    expectedOutput: A classification.
    decision:
      questions:
        category:
          choice:
            bug: a defect
            billing: an invoice question
      min_confidence: 0.6
      fallback:
        category: bug
  refund:
    description: Handle the refund
    expectedOutput: A refund
    agent: writer
    when:
      triage.category: [billing]
""";

    private const string DecisionOnlyYaml = """
name: routing
goal: Route tickets
process: sequential
agents:
  idle: { role: Idle, goal: Wait }
tasks:
  triage:
    description: Classify the change.
    expectedOutput: A classification.
    decision:
      questions:
        category:
          choice:
            bug: a defect
            billing: an invoice question
      fallback:
        category: bug
  notify:
    description: Notify the owner.
    expectedOutput: A notice.
    decision:
      questions:
        ack:
          choice:
            yes: sent
            no: not sent
      fallback:
        ack: "no"
    when:
      triage.category: [billing]
""";

    public void Dispose()
    {
        _memoryScope.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record Built(
        DomainCrew Crew, InMemoryTaskRepository Tasks, InMemoryAgentRepository Agents, NullUnitOfWork UnitOfWork);

    private static async System.Threading.Tasks.Task<Built> BuildAsync(string yaml)
    {
        var loader = new YamlCrewDefinitionLoader(
            new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);
        var unitOfWork = new NullUnitOfWork();
        var agents = new InMemoryAgentRepository(unitOfWork);
        var tasks = new InMemoryTaskRepository(unitOfWork);
        var factory = new CrewFactory(
            loader, new MockToolRegistry(), NullLogger<CrewFactory>.Instance,
            new InMemoryCrewRepository(unitOfWork), agents, tasks);
        var crew = await factory.CreateFromConfigAsync(await loader.LoadFromStringAsync(yaml, Ct), Ct);
        return new Built(crew, tasks, agents, unitOfWork);
    }

    private static async System.Threading.Tasks.Task<List<CrewTask>> TasksOfAsync(Built built)
    {
        var list = new List<CrewTask>();
        foreach (var id in built.Crew.Tasks)
            list.Add((await built.Tasks.GetByIdAsync(id, Ct))!);
        return list;
    }

    [Fact]
    [Trait("BR", "UC-01")]
    [Trait("BR", "UC-02")]
    public async System.Threading.Tasks.Task ShouldCopyKeyDecisionAndCondition_WhenFactoryCreatesTasksFromYaml()
    {
        var tasks = await TasksOfAsync(await BuildAsync(CrewYaml));

        var triage = tasks.Single(t => t.Key == "triage");
        Assert.True(triage.IsDecision);
        Assert.Null(triage.AssignedAgent);
        var decision = Assert.IsType<TaskDecision>(triage.Decision);
        Assert.IsType<ChoiceQuestionSpec>(decision.Question("category"));
        Assert.Equal(0.6, decision.ConfidenceFloor);
        Assert.Equal("bug", decision.Fallback["category"]);

        var refund = tasks.Single(t => t.Key == "refund");
        var condition = Assert.IsType<TaskCondition>(refund.Condition);
        Assert.Equal("triage", condition.DecisionTask);
        var expected = TaskCondition.Create("triage", [ConditionClause.OneOf(decision, "category", ["billing"])]);
        Assert.Equal(expected, condition);
    }

    [Fact]
    [Trait("BR", "UC-02")]
    public async System.Threading.Tasks.Task ShouldResolveConditionToDecisionTaskId_WhenFactoryCreatesTasks()
    {
        var tasks = await TasksOfAsync(await BuildAsync(CrewYaml));

        var triage = tasks.Single(t => t.Key == "triage");
        var refund = tasks.Single(t => t.Key == "refund");
        Assert.NotNull(refund.Condition);
        Assert.Contains(triage.Id, refund.Dependencies);
    }

    /// <summary><paramref name="yaml"/> without the expected output of its decision task, which the validator does not ask for.</summary>
    private static string WithoutDecisionExpectedOutput(string yaml)
    {
        var lines = yaml.Split('\n');
        var kept = lines.Where(line => line.Trim() != "expectedOutput: A classification.").ToList();
        Assert.Equal(lines.Length - 1, kept.Count);
        return string.Join('\n', kept);
    }

    [Fact]
    [Trait("BR", "CH-476")]
    public async System.Threading.Tasks.Task ShouldCreateTheDecisionTask_WhenItDeclaresNoExpectedOutput()
    {
        var tasks = await TasksOfAsync(await BuildAsync(WithoutDecisionExpectedOutput(CrewYaml)));

        var triage = tasks.Single(t => t.Key == "triage");
        Assert.True(triage.IsDecision);
        Assert.Equal("decision", triage.ExpectedOutput.Value);
        Assert.Equal("A summary", tasks.Single(t => t.Key == "intake").ExpectedOutput.Value);
        Assert.Contains(triage.Id, tasks.Single(t => t.Key == "refund").Dependencies);
    }

    [Theory]
    [InlineData("billing", true, 2, true)]
    [InlineData("bug", false, 1, true)]
    [InlineData("billing", true, 2, false)]
    [InlineData("bug", false, 1, false)]
    [Trait("BR", "UC-01")]
    [Trait("BR", "UC-02")]
    [Trait("BR", "UC-04")]
    [Trait("BR", "CH-476")]
    public async System.Threading.Tasks.Task ShouldRunYamlLoadedCrew_WhenItsOnlyAgentIsUsedByNoTask(
        string category, bool notifyRuns, int expectedRequests, bool expectedOutputDeclared)
    {
        var built = await BuildAsync(expectedOutputDeclared ? DecisionOnlyYaml : WithoutDecisionExpectedOutput(DecisionOnlyYaml));
        var model = new FakeDecisionModel
        {
            Respond = request => new DecisionResponse
            {
                Model = "fake",
                Answers = request.Questions.Keys.ToImmutableDictionary(
                    name => name,
                    name => (DecisionAnswer)new ChoiceAnswer
                    {
                        Choice = name == "category" ? category : "yes",
                        Confidence = 0.9,
                        Probabilities = ImmutableDictionary<string, double>.Empty
                            .Add(name == "category" ? category : "yes", 0.9),
                    }),
            },
        };
        var executionService = new MockAgentExecutionService();
        var delegation = new AgentDelegationToolsProvider(
            new MockAgentCommunicationService(), executionService, NullLogger<AgentDelegationToolsProvider>.Instance);
        var strategy = new SequentialProcessStrategy(
            new CrewStrategyDependencies(built.Tasks, built.Agents, executionService, _memoryScope)
            {
                DecisionRunner = new DecisionTaskRunner(model, DecisionCircuit.On, NullLogger<DecisionTaskRunner>.Instance),
            },
            delegation,
            NullLogger<SequentialProcessStrategy>.Instance);

        var output = await strategy.ExecuteSequentialAsync(built.Crew, cancellationToken: Ct);

        Assert.True(output.Success, output.Error);
        var tasks = await TasksOfAsync(built);
        Assert.Equal(TaskStatus.Completed, tasks.Single(t => t.Key == "triage").Status);
        Assert.Equal(notifyRuns ? TaskStatus.Completed : TaskStatus.Cancelled, tasks.Single(t => t.Key == "notify").Status);
        Assert.Equal(expectedRequests, model.Requests.Count);
        Assert.Equal(0, executionService.ExecuteTaskCallCount);
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldRefuseTheConfiguration_WhenItsRelayNamesAnAgentTheCrewDoesNotHave()
    {
        // Arrange: a configuration handed straight to the factory, its relay renamed after the loader checked it.
        var yaml = CrewYaml
            .Replace("process: sequential\n", "process: sequential\ndecision:\n  mode: optional\n", StringComparison.Ordinal)
            .Replace("      fallback:\n        category: bug\n", "      without:\n        agent: writer\n", StringComparison.Ordinal);
        var loader = new YamlCrewDefinitionLoader(
            new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);
        var loaded = await loader.LoadFromStringAsync(yaml, Ct);
        var config = loaded with
        {
            Tasks = [.. loaded.Tasks.Select(t => t.Decision is { } d ? t with { Decision = d.WithRelay(new DecisionRelay("ghost")) } : t)],
        };
        var unitOfWork = new NullUnitOfWork();
        var factory = new CrewFactory(
            loader, new MockToolRegistry(), NullLogger<CrewFactory>.Instance,
            new InMemoryCrewRepository(unitOfWork), new InMemoryAgentRepository(unitOfWork), new InMemoryTaskRepository(unitOfWork));

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateFromConfigAsync(config, Ct));

        // Assert
        Assert.Contains("Task 'triage' without.agent 'ghost' is not an agent of the crew", ex.Message, StringComparison.Ordinal);
    }
}
