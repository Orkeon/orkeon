using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Crew;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>#429: a crew file declares whether it needs System One (<c>decision: { mode }</c>), and an optional crew's relays.</summary>
public sealed class DecisionModeYamlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Crew(string decisionBlock, string triageExtra = "", string fallback = "") => $$"""
name: support
goal: Handle tickets
process: sequential
{{decisionBlock}}
agents:
  writer: { role: Writer, goal: Write the answer }
  analyst: { role: Analyst, goal: Read the ticket }
tasks:
  intake:
    description: Read the ticket
    expectedOutput: A summary
    agent: writer
  triage:
    decision:
      questions:
        route:
          choice:
            bug: a defect
            other: anything else
      state: [intake]
{{fallback}}{{triageExtra}}
  answer:
    description: Answer the ticket
    expectedOutput: An answer
    agent: writer
    dependencies: [triage]
""";

    private const string Relay = """
      without:
        agent: analyst
        instructions: Read the summary first.
""";

    private const string Fallback = """
      fallback:
        route: bug

""";

    private static YamlCrewDefinitionLoader NewLoader(FakeFileSystemService? fs = null) =>
        new(new YamlDotNetSerializer(), fs ?? new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);

    [Theory]
    [InlineData("none", CrewDecisionMode.None)]
    [InlineData("required", CrewDecisionMode.Required)]
    [InlineData("Required", CrewDecisionMode.Required)]
    [Trait("BR", "CH-429")]
    public async Task ShouldReachTheConfiguration_WhenTheCrewDeclaresAMode(string written, CrewDecisionMode expected)
    {
        // Arrange
        var yaml = expected == CrewDecisionMode.None
            ? "name: plain\ngoal: Read\ndecision:\n  mode: " + written + "\nagents:\n  r: { role: R, goal: G }\ntasks:\n  t: { description: D, expectedOutput: O, agent: r }"
            : Crew("decision:\n  mode: " + written);

        // Act
        var config = await NewLoader().LoadFromStringAsync(yaml, Ct);

        // Assert
        Assert.Equal(expected, config.DecisionMode);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldLeaveTheModeUnspecified_WhenTheCrewDeclaresNone()
    {
        // Arrange
        var yaml = Crew(string.Empty, fallback: Fallback);

        // Act
        var config = await NewLoader().LoadFromStringAsync(yaml, Ct);

        // Assert
        Assert.Equal(CrewDecisionMode.Unspecified, config.DecisionMode);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldMapTheRelay_WhenAnOptionalCrewDeclaresOne()
    {
        // Arrange
        var yaml = Crew("decision:\n  mode: optional", Relay);

        // Act
        var config = await NewLoader().LoadFromStringAsync(yaml, Ct);

        // Assert
        var relay = config.Tasks.Single(t => t.Key == "triage").Decision!.Without;
        Assert.Equal(new Orkeon.Domain.Task.ValueObjects.DecisionRelay("analyst", "Read the summary first."), relay);
    }

    [Theory]
    [InlineData("decision:\n  mode: sometimes", "decision.mode: 'sometimes' is not a mode; expected none, optional or required.")]
    [InlineData("decision: {}", "decision: the block names a mode, decision.mode: none, optional or required.")]
    [Trait("BR", "CH-429")]
    public async Task ShouldRefuseTheLoad_WhenTheModeIsMissingOrUnknown(string block, string expected)
    {
        // Arrange
        var yaml = Crew(block, fallback: Fallback);

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => NewLoader().LoadFromStringAsync(yaml, Ct));

        // Assert
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldRefuseTheLoad_WhenAnOptionalCrewHasADecisionTaskWithoutARelay()
    {
        // Arrange
        var yaml = Crew("decision:\n  mode: optional");

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => NewLoader().LoadFromStringAsync(yaml, Ct));

        // Assert
        Assert.Contains("Decision task 'triage' has no without: relay", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldRefuseTheLoad_WhenTheRelayNamesAnAgentTheCrewDoesNotDeclare()
    {
        // Arrange
        var yaml = Crew("decision:\n  mode: optional", Relay.Replace("agent: analyst", "agent: ghost", StringComparison.Ordinal));

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => NewLoader().LoadFromStringAsync(yaml, Ct));

        // Assert
        Assert.Contains("Task 'triage' without.agent 'ghost' is not an agent of the crew; its agents: analyst, writer.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldRefuseTheLoad_WhenACrewWithModeNoneListsSystemOneDecide()
    {
        // Arrange
        var yaml = "name: plain\ngoal: Read\ndecision: { mode: none }\nagents:\n  reader: { role: Reader, goal: G, tools: [system_one_decide] }\n" +
            "tasks:\n  t: { description: D, expectedOutput: O, agent: reader }";

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => NewLoader().LoadFromStringAsync(yaml, Ct));

        // Assert
        Assert.Contains("Agent 'reader' lists system_one_decide, which a crew with decision.mode: none refuses.", ex.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> PickingAgents => new()
    {
        { "skills: { auto: true }", "Agent 'reader' declares skills: { auto: true }, which System One picks and a crew with decision.mode: none refuses" },
        { "compaction: { prune: true }", "Agent 'reader' declares compaction: { prune: true }, which the decision model judges and a crew with decision.mode: none refuses." },
    };

    [Theory]
    [MemberData(nameof(PickingAgents))]
    [Trait("BR", "CH-429")]
    public async Task ShouldRefuseTheLoad_WhenACrewWithModeNoneHasTheModelPickSkillsOrJudgeResults(string agentKey, string expected)
    {
        // Arrange
        var yaml = "name: plain\ngoal: Read\ndecision: { mode: none }\nagents:\n  reader: { role: Reader, goal: G, " + agentKey + " }\n" +
            "tasks:\n  t: { description: D, expectedOutput: O, agent: reader }";

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => NewLoader().LoadFromStringAsync(yaml, Ct));

        // Assert
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldOpenTheRefusalWithTheOneDecisionModePrefix_WhenTheLoaderRefusesTheMode()
    {
        // Arrange
        var yaml = "name: plain\ngoal: Read\ndecision: { mode: none }\nagents:\n  reader: { role: Reader, goal: G, tools: [system_one_decide] }\n" +
            "tasks:\n  t: { description: D, expectedOutput: O, agent: reader }";

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => NewLoader().LoadFromStringAsync(yaml, Ct));

        // Assert
        Assert.StartsWith(Orkeon.Domain.Task.DecisionTaskRules.ModeRefusal([]), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldReadTheMode_WhenTheCrewIsSplitIntoFiles()
    {
        // Arrange
        var fs = new FakeFileSystemService();
        fs.AddFile("/crews/c/config.yaml", "name: c\ngoal: G\nprocess: sequential\ndecision:\n  mode: required");
        fs.AddFile("/crews/c/agents/a.yaml", "role: R\ngoal: G");
        fs.AddFile("/crews/c/tasks/collect.yaml", "description: D\nexpectedOutput: O\nagent: a");

        // Act
        var config = await NewLoader(fs).LoadFromDirectoryAsync("/crews/c", Ct);

        // Assert
        Assert.Equal(CrewDecisionMode.Required, config.DecisionMode);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldKeepTheModeAndTheRelay_WhenTheCrewIsExported()
    {
        // Arrange
        var original = await NewLoader().LoadFromStringAsync(Crew("decision:\n  mode: optional", Relay), Ct);
        var exporter = new YamlCrewExporter(new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewExporter>.Instance);

        // Act
        var reloaded = await NewLoader().LoadFromStringAsync(exporter.ExportToString(original), Ct);

        // Assert
        Assert.Equal(CrewDecisionMode.Optional, reloaded.DecisionMode);
        Assert.Equal(original.Tasks.Single(t => t.Key == "triage").Decision, reloaded.Tasks.Single(t => t.Key == "triage").Decision);
    }
}
