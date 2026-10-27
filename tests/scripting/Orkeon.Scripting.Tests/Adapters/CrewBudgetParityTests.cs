using Jint;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Adapters;

/// <summary>
/// #218 - <c>crewBuilder().budget(...)</c> on a declarative autonomous crew bounds its run, as the YAML
/// <c>budget:</c> block does; any other process still says it ignores the budget.
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
    public void ShouldStillSayTheBudgetIsIgnored_WhenTheProcessIsNotAutonomous()
    {
        var crew = Crew("sequential", ".budget({ tokens: 300000 })");

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        Assert.Null(config.Budget);
        Assert.Contains(JsCrewConfigurationAdapter.CollectIgnoredFeatures(crew),
            line => line.Contains("budget", StringComparison.Ordinal));
    }
}
