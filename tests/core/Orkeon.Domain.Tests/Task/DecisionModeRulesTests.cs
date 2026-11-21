using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Tests.Task;

/// <summary>#429: the load rules of a crew's <c>decision.mode</c>, and what the domain carries for #430's relay.</summary>
public sealed class DecisionModeRulesTests
{
    private static readonly SystemOneUses NoUses = new([], []);

    private static TaskDecision Decision(IReadOnlyDictionary<string, string>? fallback = null) =>
        TaskDecision.Create(
            [ChoiceQuestionSpec.Create("route", "Pick the route", new Dictionary<string, string> { ["bug"] = "a defect", ["other"] = "anything else" })],
            null, null, false, fallback);

    private static TaskDecision Relayed(string agent = "analyst") => Decision().WithRelay(new DecisionRelay(agent));

    private static DecisionTaskDeclaration DecisionTask(string name, TaskDecision decision) => new(name, decision, null, false);

    private static DecisionTaskDeclaration PlainTask(string name) => new(name, null, null, true);

    private static readonly Dictionary<string, string> BugFallback = new() { ["route"] = "bug" };

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldRefuseTheDecisionTask_WhenTheModeIsNone()
    {
        // Arrange
        var tasks = new[] { DecisionTask("triage", Decision(BugFallback)) };

        // Act
        var findings = DecisionTaskRules.CheckMode(CrewDecisionMode.None, ProcessType.Sequential, tasks, NoUses);

        // Assert
        Assert.Equal(["Task 'triage' is a decision task, which a crew with decision.mode: none refuses."], findings.Refusals);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldRefuseSystemOneDecide_WhenTheModeIsNone()
    {
        // Arrange
        var uses = new SystemOneUses(["agent 'reader'"], []);

        // Act
        var findings = DecisionTaskRules.CheckMode(CrewDecisionMode.None, ProcessType.Sequential, [PlainTask("read")], uses);

        // Assert
        Assert.Equal(["Agent 'reader' lists system_one_decide, which a crew with decision.mode: none refuses."], findings.Refusals);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldRefuseADecisionProfile_WhenTheModeIsNone()
    {
        // Arrange
        var uses = new SystemOneUses([], [new DecisionProfileUse("agent 'reader'", "triage-router")]);

        // Act
        var findings = DecisionTaskRules.CheckMode(CrewDecisionMode.None, ProcessType.Sequential, [PlainTask("read")], uses);

        // Assert
        Assert.Equal(
            ["Agent 'reader' names the decision profile 'triage-router', which a crew with decision.mode: none refuses."],
            findings.Refusals);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldRefuseTheAutoProfileSayingSystemOnePicksIt_WhenTheModeIsNone()
    {
        // Arrange
        var uses = new SystemOneUses([], [new DecisionProfileUse("agent 'coder'", "auto")]);

        // Act
        var findings = DecisionTaskRules.CheckMode(CrewDecisionMode.None, ProcessType.Sequential, [PlainTask("code")], uses);

        // Assert
        Assert.Equal(
            ["Agent 'coder' names the profile 'auto', which System One picks and a crew with decision.mode: none refuses."],
            findings.Refusals);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldRefuseTheEffortAutoSayingSystemOnePicksIt_WhenTheModeIsNone()
    {
        // Arrange
        var uses = new SystemOneUses([], []) { EffortAutoHolders = ["agent 'coder'"] };

        // Act
        var findings = DecisionTaskRules.CheckMode(CrewDecisionMode.None, ProcessType.Sequential, [PlainTask("code")], uses);

        // Assert
        Assert.Equal(
            ["Agent 'coder' declares thinking: { effort: auto }, which System One picks and a crew with decision.mode: none refuses: write a level instead."],
            findings.Refusals);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldAcceptTheCrew_WhenEveryDecisionTaskOfAnOptionalCrewHasARelay()
    {
        // Arrange
        var tasks = new[] { PlainTask("intake"), DecisionTask("triage", Relayed()) };

        // Act
        var findings = DecisionTaskRules.CheckMode(CrewDecisionMode.Optional, ProcessType.Sequential, tasks, NoUses, ["analyst"]);

        // Assert
        Assert.Empty(findings.Refusals);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldRefuseTheDecisionTask_WhenAnOptionalCrewDeclaresItWithoutARelay()
    {
        // Arrange
        var tasks = new[] { DecisionTask("triage", Decision()) };

        // Act
        var findings = DecisionTaskRules.CheckMode(CrewDecisionMode.Optional, ProcessType.Sequential, tasks, NoUses);

        // Assert
        Assert.Equal(
            ["Decision task 'triage' has no without: relay, which a crew with decision.mode: optional requires on every decision task."],
            findings.Refusals);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldRefuseTheFallback_WhenAnOptionalCrewDeclaresOne()
    {
        // Arrange
        var tasks = new[] { DecisionTask("triage", Decision(BugFallback).WithRelay(new DecisionRelay("analyst"))) };

        // Act
        var findings = DecisionTaskRules.CheckMode(CrewDecisionMode.Optional, ProcessType.Sequential, tasks, NoUses);

        // Assert
        Assert.StartsWith("Decision task 'triage' declares a fallback, which a crew with decision.mode: optional never uses", Assert.Single(findings.Refusals), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldRefuseTheCrew_WhenAnOptionalCrewHasNoDecisionTask()
    {
        // Arrange
        var tasks = new[] { PlainTask("read") };

        // Act
        var findings = DecisionTaskRules.CheckMode(CrewDecisionMode.Optional, ProcessType.Sequential, tasks, NoUses);

        // Assert
        Assert.Equal(
            ["A crew with decision.mode: optional declares at least one decision task, and this one has none."],
            findings.Refusals);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldRefuseTheCrew_WhenAnOptionalCrewRunsAProcessWithoutDecisionTasks()
    {
        // Arrange
        var tasks = new[] { PlainTask("read") };

        // Act
        var findings = DecisionTaskRules.CheckMode(CrewDecisionMode.Optional, ProcessType.Hierarchical, tasks, NoUses);

        // Assert
        Assert.Equal(
            ["A crew with decision.mode: optional needs a decision task, which the Hierarchical process does not support."],
            findings.Refusals);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldRefuseSystemOneDecide_WhenTheModeIsOptional()
    {
        // Arrange
        var uses = new SystemOneUses(["task 'answer'"], []);

        // Act
        var findings = DecisionTaskRules.CheckMode(
            CrewDecisionMode.Optional, ProcessType.Sequential, [DecisionTask("triage", Relayed())], uses);

        // Assert
        Assert.StartsWith("Task 'answer' lists system_one_decide, which a crew with decision.mode: optional refuses", Assert.Single(findings.Refusals), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldRefuseTheRelay_WhenItNamesAnAgentTheCrewDoesNotDeclare()
    {
        // Arrange
        var tasks = new[] { DecisionTask("triage", Relayed("ghost")) };

        // Act
        var findings = DecisionTaskRules.CheckMode(CrewDecisionMode.Optional, ProcessType.Sequential, tasks, NoUses, ["writer", "analyst"]);

        // Assert
        Assert.Equal(
            ["Task 'triage' without.agent 'ghost' is not an agent of the crew; its agents: analyst, writer."],
            findings.Refusals);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldRefuseTheRelay_WhenTheCrewDeclaresNoMode()
    {
        // Arrange
        var tasks = new[] { DecisionTask("triage", Relayed()) };

        // Act
        var findings = DecisionTaskRules.CheckMode(CrewDecisionMode.Unspecified, ProcessType.Sequential, tasks, NoUses);

        // Assert
        Assert.Equal(["Task 'triage' declares a without: relay, which needs decision.mode: optional on the crew."], findings.Refusals);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldWarnWithoutRefusing_WhenARequiredCrewDeclaresAFallback()
    {
        // Arrange
        var tasks = new[] { DecisionTask("triage", Decision(BugFallback)) };

        // Act
        var findings = DecisionTaskRules.CheckMode(CrewDecisionMode.Required, ProcessType.Sequential, tasks, NoUses);

        // Assert
        Assert.Empty(findings.Refusals);
        Assert.Equal(["Decision task 'triage' declares a fallback, which a crew with decision.mode: required never uses."], findings.Warnings);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldWarnThatItAsksNothing_WhenARequiredCrewUsesNoPartOfSystemOne()
    {
        // Arrange
        var tasks = new[] { PlainTask("read") };

        // Act
        var findings = DecisionTaskRules.CheckMode(CrewDecisionMode.Required, ProcessType.Sequential, tasks, NoUses);

        // Assert
        Assert.Equal(
            ["The crew requires System One but asks it nothing: it has no decision task, no system_one_decide, no decision profile, no skills: { auto: true }, no compaction: { prune: true } and no thinking: { effort: auto }."],
            findings.Warnings);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldNotWarnThatItAsksNothing_WhenTheDecisionProfilesAreUnknown()
    {
        // Arrange
        var tasks = new[] { PlainTask("read") };

        // Act
        var findings = DecisionTaskRules.CheckMode(CrewDecisionMode.Required, ProcessType.Sequential, tasks, SystemOneUses.Unknown);

        // Assert
        Assert.Empty(findings.Warnings);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldNotAskForAFallback_WhenTheDecisionTaskOfAnOptionalCrewHasARelay()
    {
        // Arrange
        var tasks = new[] { DecisionTask("triage", Relayed()) };

        // Act
        var messages = DecisionTaskRules.CheckFallbacks(CrewDecisionMode.Optional, tasks);

        // Assert
        Assert.Empty(messages);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldNotAskForAFallback_WhenTheCrewIsRequired()
    {
        // Arrange
        var tasks = new[] { DecisionTask("triage", Decision()) };

        // Act
        var messages = DecisionTaskRules.CheckFallbacks(CrewDecisionMode.Required, tasks);

        // Assert
        Assert.Empty(messages);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldRefuseTheRelay_WhenItNamesNoAgent()
    {
        // Arrange
        var agent = "  ";

        // Act
        var ex = Assert.Throws<ArgumentException>(() => new DecisionRelay(agent));

        // Assert
        Assert.Equal("agent", ex.ParamName);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldTellTwoDecisionsApart_WhenOnlyTheirRelayDiffers()
    {
        // Arrange
        var first = Relayed("analyst");

        // Act
        var second = Relayed("writer");

        // Assert
        Assert.NotEqual(first, second);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldCarryTheMode_WhenTheBuilderBuildsTheCrew()
    {
        // Arrange
        var builder = new CrewBuilder().Name("plain").Goal("Read").DecisionMode(CrewDecisionMode.Required);

        // Act
        var crew = builder.Build();

        // Assert
        Assert.Equal(CrewDecisionMode.Required, crew.DecisionMode);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldRefuseTheCrew_WhenTheBuilderGetsADecisionTaskInACrewWithModeNone()
    {
        // Arrange
        var task = new CrewTaskBuilder().Description("Decide").ExpectedOutput("An outcome").WithDecision(Decision(BugFallback)).Build();
        var builder = new CrewBuilder().Name("plain").Goal("Read").DecisionMode(CrewDecisionMode.None).WithTasks([task]);

        // Act
        var ex = Assert.Throws<BuilderValidationException>(() => builder.Build());

        // Assert
        Assert.Contains("which a crew with decision.mode: none refuses", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldListTheHoldersOfSystemOne_WhenTheConfigurationNamesTheToolAndADecisionProfile()
    {
        // Arrange
        var config = new CrewConfiguration
        {
            Agents =
            [
                new AgentConfiguration { Key = "reader", Role = "Reader", Tools = ["system_one_decide"] },
                new AgentConfiguration { Key = "router", Role = "Router", LlmConfig = LlmConfig.Create("m") with { Profile = "triage-router" } },
                new AgentConfiguration { Key = "writer", Role = "Writer", LlmConfig = LlmConfig.Create("m") with { Profile = "default" } },
            ],
        };

        // Act
        var uses = config.SystemOneUses(profile => profile == "triage-router");

        // Assert
        Assert.Equal(["agent 'reader'"], uses.DecideToolHolders);
        Assert.Equal([new DecisionProfileUse("agent 'router'", "triage-router")], uses.DecisionProfiles!);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldRefuseSkillsAuto_WhenTheModeIsNone()
    {
        // Arrange
        var uses = new SystemOneUses([], []) { SkillsAutoHolders = ["agent 'writer'"] };

        // Act
        var findings = DecisionTaskRules.CheckMode(CrewDecisionMode.None, ProcessType.Sequential, [PlainTask("write")], uses);

        // Assert
        Assert.Equal(
            ["Agent 'writer' declares skills: { auto: true }, which System One picks and a crew with decision.mode: none refuses: list its skills instead."],
            findings.Refusals);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldRefusePruning_WhenTheModeIsNone()
    {
        // Arrange
        var uses = new SystemOneUses([], []) { PruneHolders = ["agent 'reader'"] };

        // Act
        var findings = DecisionTaskRules.CheckMode(CrewDecisionMode.None, ProcessType.Sequential, [PlainTask("read")], uses);

        // Assert
        Assert.Equal(
            ["Agent 'reader' declares compaction: { prune: true }, which the decision model judges and a crew with decision.mode: none refuses."],
            findings.Refusals);
    }

    public static TheoryData<string, SystemOneUses> PickingUses => new()
    {
        { "skills: auto", new SystemOneUses([], []) { SkillsAutoHolders = ["agent 'writer'"] } },
        { "compaction.prune: true", new SystemOneUses([], []) { PruneHolders = ["agent 'reader'"] } },
    };

    [Theory]
    [MemberData(nameof(PickingUses))]
    [Trait("BR", "CH-429")]
    public void ShouldNotWarnThatItAsksNothing_WhenARequiredCrewHasTheModelPickSkillsOrJudgeResults(string name, SystemOneUses uses)
    {
        // Arrange
        var tasks = new[] { PlainTask("read") };

        // Act
        var findings = DecisionTaskRules.CheckMode(CrewDecisionMode.Required, ProcessType.Sequential, tasks, uses);

        // Assert
        Assert.True(findings.Warnings.Count == 0, $"{name}: {string.Join(" | ", findings.Warnings)}");
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldListTheAgentsOnSkillsAutoAndOnPruning_WhenTheConfigurationDeclaresThem()
    {
        // Arrange
        var config = new CrewConfiguration
        {
            Agents =
            [
                new AgentConfiguration { Key = "writer", Role = "Writer", Skills = new Orkeon.Domain.Agent.AgentSkills { Auto = true } },
                new AgentConfiguration { Key = "reader", Role = "Reader", Compaction = new Orkeon.Domain.Agent.AgentCompaction { Prune = true } },
                new AgentConfiguration { Key = "editor", Role = "Editor", Compaction = new Orkeon.Domain.Agent.AgentCompaction { Prune = false } },
            ],
        };

        // Act
        var uses = config.SystemOneUses();

        // Assert
        Assert.Equal("agent 'writer' / agent 'reader'", $"{string.Join(", ", uses.SkillsAutoHolders)} / {string.Join(", ", uses.PruneHolders)}");
    }
}
