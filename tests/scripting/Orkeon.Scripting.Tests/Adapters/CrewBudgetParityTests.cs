using Jint;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Adapters;

/// <summary>
/// #218 - <c>crewBuilder().budget(...)</c> on a declarative autonomous crew bounds its run, as the YAML
/// <c>budget:</c> block does; any other process, and a limit that is not 1 or more, is refused as YAML refuses it.
/// </summary>
public sealed class CrewBudgetParityTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();

    private static JsCrew Crew(string process, string crewCall) =>
        (JsCrew)NewEngine().Evaluate($$"""
            const analyst = agentBuilder().name("analyst").role("Analyst").goal("Analyze").build();
            const work = taskBuilder().name("work").agent(analyst)
                .description("Do the work").expectedOutput("A result").build();

            crewBuilder()
                .name("bounded").goal("Analyze within bounds")
                .process("{{process}}")
                {{crewCall}}
                .withAgent(analyst)
                .withTask(work)
                .build();
            """).ToObject()!;

    [Fact]
    [Trait("BR", "CH-218")]
    public void ShouldCarryTheBudgetToTheConfiguration_WhenTheCrewIsAutonomous()
    {
        var crew = Crew("autonomous", """.budget({ tokens: 300000, wallTime: "20m", toolCalls: 60 })""");

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        Assert.NotNull(config.Budget);
        Assert.Equal(300_000, config.Budget.Tokens);
        Assert.Equal(TimeSpan.FromMinutes(20), config.Budget.WallTime);
        Assert.Equal(60, config.Budget.ToolCalls);
        Assert.Null(config.Budget.DelegationDepth);
        Assert.DoesNotContain(JsCrewConfigurationAdapter.CollectIgnoredFeatures(crew),
            line => line.Contains("budget", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "CH-218")]
    public void ShouldRefuseTheBudget_WhenTheProcessIsNotAutonomous()
    {
        var crew = Crew("sequential", ".budget({ tokens: 300000 })");

        var error = Assert.Throws<InvalidScriptException>(() => JsCrewConfigurationAdapter.ToConfiguration(crew));

        Assert.Contains("crewBuilder().budget", error.Message, StringComparison.Ordinal);
        Assert.Contains("sequential process spends no budget", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(JsCrewConfigurationAdapter.CollectIgnoredFeatures(crew),
            line => line.Contains("budget", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("BR", "CH-218")]
    [InlineData("{ tokens: 0 }", "budget.tokens: 0")]
    [InlineData("{ toolCalls: -1 }", "budget.toolCalls: -1")]
    [InlineData("{ delegationDepth: 1.5 }", "budget.delegationDepth: 1.5")]
    [InlineData("{ spawnedAgents: \"many\" }", "budget.spawnedAgents: \"many\"")]
    [InlineData("{ wallTime: 0 }", "budget.wallTime: 0")]
    [InlineData("{ wallTime: \"soon\" }", "budget.wallTime: \"soon\"")]
    public void ShouldRefuseTheBudget_WhenALimitIsInvalid(string spec, string named)
    {
        var crew = Crew("autonomous", $".budget({spec})");

        var error = Assert.Throws<InvalidScriptException>(() => JsCrewConfigurationAdapter.ToConfiguration(crew));

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
        Assert.Contains("every limit of a crew's budget is 1 or more", error.Message, StringComparison.Ordinal);
    }
}
