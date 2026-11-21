using Jint;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Internal;

namespace Orkeon.Scripting.Tests.Adapters;

/// <summary>#429: <c>crewBuilder().decision({ mode })</c> and a decision task's <c>without</c> relay in a script.</summary>
public sealed class DecisionModeScriptTests
{
    private static JsCrew Crew(string crewCall, string decisionExtra = ", fallback: { route: \"bug\" }", string relayAgent = "analyst") =>
        (JsCrew)new JsEngineFactory().Create().Evaluate($$"""
            const analyst = agentBuilder().name("{{relayAgent}}").role("Analyst").goal("Analyze").build();
            const writer = agentBuilder().name("writer").role("Writer").goal("Write").build();
            const intake = taskBuilder().name("intake").agent(writer).description("Read").expectedOutput("A summary").build();
            const triage = taskBuilder().name("triage").decision({
                questions: { route: { choice: { bug: "a defect", other: "anything else" } } },
                state: ["intake"]{{decisionExtra}},
            }).build();

            crewBuilder().name("support").goal("Handle tickets"){{crewCall}}
                .withAgent(analyst).withAgent(writer)
                .withTask(intake).withTask(triage)
                .build();
            """).ToObject()!;

    private const string Relay = ", without: { agent: \"analyst\", instructions: \"Read the summary first.\" }";

    [Theory]
    [InlineData(".decision({ mode: \"required\" })", CrewDecisionMode.Required)]
    [InlineData("", CrewDecisionMode.Unspecified)]
    [Trait("BR", "CH-429")]
    public void ShouldReachTheConfiguration_WhenTheScriptDeclaresTheMode(string crewCall, CrewDecisionMode expected)
    {
        // Arrange
        var crew = Crew(crewCall);

        // Act
        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        // Assert
        Assert.Equal(expected, config.DecisionMode);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldMapTheRelay_WhenAnOptionalCrewDeclaresOne()
    {
        // Arrange
        var crew = Crew(".decision({ mode: \"optional\" })", Relay);

        // Act
        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        // Assert
        Assert.Equal(CrewDecisionMode.Optional, config.DecisionMode);
        Assert.Equal(new DecisionRelay("analyst", "Read the summary first."), config.Tasks.Single(t => t.Key == "triage").Decision!.Without);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldRefuseTheScript_WhenTheModeIsUnknown()
    {
        // Arrange
        var crewCall = ".decision({ mode: \"sometimes\" })";

        // Act
        var error = ScriptRefusal.Bridged<InvalidScriptException>(() => Crew(crewCall), ".decision");

        // Assert
        Assert.Contains("sometimes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public void ShouldRefuseTheCrew_WhenTheRelayNamesAnAgentTheCrewDoesNotDeclare()
    {
        // Arrange
        var crew = Crew(".decision({ mode: \"optional\" })", Relay, relayAgent: "reviewer");

        // Act
        var error = Assert.ThrowsAny<Exception>(() => JsCrewConfigurationAdapter.ToConfiguration(crew));

        // Assert
        Assert.Contains("without.agent 'analyst' is not an agent of the crew", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public void ShouldKeyEachAgentByItsName_WhenItsRoleDiffers()
    {
        // Arrange
        var crew = Crew(".decision({ mode: \"optional\" })", Relay);

        // Act
        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        // Assert
        Assert.Equal(
            [("analyst", "Analyst"), ("writer", "Writer")],
            config.Agents.Select(a => (a.Key, a.Role)).ToList());
    }
}
