using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>A crew file declares a decision task and the tasks that run on its outcome.</summary>
public sealed class DecisionTaskYamlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
    decision:
      questions:
        category:
          choice:
            bug: a defect
            billing: an invoice question
        urgency:
          score: [low, medium, high]
          instructions: How urgent is it
        refund_requested:
          noul: the customer asks for a refund
      min_confidence: 0.6
      state: [intake]
      long_context: true
      fallback:
        category: bug
        urgency: low
        refund_requested: "no"
  refund:
    description: Handle the refund
    expectedOutput: A refund
    agent: writer
    when:
      triage.category: [billing, bug]
      triage.urgency: ">= 0.7"
      triage.category.bug: ">= 0.25"
  other:
    description: Handle the rest
    expectedOutput: An answer
    agent: writer
    when:
      triage: otherwise
""";

    private static async Task<CrewConfiguration> LoadAsync()
    {
        var loader = new YamlCrewDefinitionLoader(new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);
        return await loader.LoadFromStringAsync(CrewYaml, Ct);
    }

    [Fact]
    [Trait("BR", "UC-01")]
    [Trait("BR", "UC-04")]
    public async Task ShouldMapDecisionTask_WhenYamlDeclaresQuestionsFloorStateLongContextAndFallback()
    {
        var config = await LoadAsync();

        var triage = config.Tasks.Single(t => t.Key == "triage");
        Assert.Null(triage.AssignedAgentId);
        var decision = Assert.IsType<TaskDecision>(triage.Decision);
        Assert.Equal(3, decision.Questions.Count);
        var category = Assert.IsType<ChoiceQuestionSpec>(decision.Question("category"));
        Assert.Equal("a defect", category.Options["bug"]);
        Assert.Equal("an invoice question", category.Options["billing"]);
        var urgency = Assert.IsType<ScoreQuestionSpec>(decision.Question("urgency"));
        Assert.Equal(["low", "medium", "high"], urgency.Levels);
        Assert.Equal("How urgent is it", urgency.Instructions);
        var refund = Assert.IsType<YesNoQuestionSpec>(decision.Question("refund_requested"));
        Assert.Equal("the customer asks for a refund", refund.Statement);
        Assert.Equal(0.6, decision.ConfidenceFloor);
        Assert.True(decision.LongContext);
        Assert.Equal(["intake"], decision.StateFrom);
        Assert.Equal("bug", decision.Fallback["category"]);
        Assert.Equal("low", decision.Fallback["urgency"]);
        Assert.Equal("no", decision.Fallback["refund_requested"]);
    }

    [Fact]
    [Trait("BR", "UC-02")]
    public async Task ShouldMapOptionThresholdAndOtherwiseConditions_WhenYamlDeclaresWhen()
    {
        var config = await LoadAsync();
        var decision = config.Tasks.Single(t => t.Key == "triage").Decision!;

        var condition = Assert.IsType<TaskCondition>(config.Tasks.Single(t => t.Key == "refund").Condition);
        Assert.Equal("triage", condition.DecisionTask);
        Assert.False(condition.IsOtherwise);
        var expected = TaskCondition.Create("triage",
        [
            ConditionClause.OneOf(decision, "category", ["billing", "bug"]),
            ConditionClause.Threshold(decision, "urgency", null, ConditionComparison.GreaterOrEqual, 0.7),
            ConditionClause.Threshold(decision, "category", "bug", ConditionComparison.GreaterOrEqual, 0.25),
        ]);
        Assert.Equal(expected, condition);

        var otherwise = Assert.IsType<TaskCondition>(config.Tasks.Single(t => t.Key == "other").Condition);
        Assert.True(otherwise.IsOtherwise);
        Assert.Equal("triage", otherwise.DecisionTask);
    }

    [Fact]
    [Trait("BR", "UC-02")]
    public async Task ShouldAddDecisionTaskToDependencies_WhenWhenNamesIt()
    {
        var config = await LoadAsync();

        var triage = config.Tasks.Single(t => t.Key == "triage");
        Assert.Contains(triage.Id, config.Tasks.Single(t => t.Key == "refund").Dependencies);
        Assert.Contains(triage.Id, config.Tasks.Single(t => t.Key == "other").Dependencies);
    }

    [Fact]
    [Trait("BR", "UC-01")]
    public async Task ShouldAddStateTasksToDependencies_WhenDecisionDeclaresState()
    {
        var config = await LoadAsync();

        var intake = config.Tasks.Single(t => t.Key == "intake");
        Assert.Contains(intake.Id, config.Tasks.Single(t => t.Key == "triage").Dependencies);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public async Task ShouldRefuseYaml_WhenWhenIsEmptyMapping()
    {
        var yaml = CrewYaml.Replace("    when:\n      triage: otherwise", "    when: {}", StringComparison.Ordinal);
        Assert.NotEqual(CrewYaml, yaml);
        var loader = new YamlCrewDefinitionLoader(new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => loader.LoadFromStringAsync(yaml, Ct));

        Assert.Contains("Task 'other' when: is empty; name a decision task and its answers, or remove the key.", ex.Message, StringComparison.Ordinal);
    }
}
