using Jint;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using static Orkeon.Tests.Shared.Assertions.AssertEx;

namespace Orkeon.Scripting.Tests.Adapters;

/// <summary>
/// CH-02 - YAML parity <c>maxExecutionTime:</c>: <c>agentBuilder().maxExecutionTime(seconds)</c>
/// reaches the agent as a TimeSpan; zero, negative, decimal or above the ceiling (4294967.295 s) is
/// refused at <c>build()</c>.
/// </summary>
public sealed class MaxExecutionTimeParityTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();

    private static JsCrew Crew(string agentCall) =>
        (JsCrew)NewEngine().Evaluate($$"""
            const analyst = agentBuilder().name("analyst").role("Analyst").goal("Analyze"){{agentCall}}.build();
            const work = taskBuilder().name("work").agent(analyst)
                .description("Do the work").expectedOutput("A result").build();

            crewBuilder()
                .name("timed").goal("Analyze within a limit")
                .withAgent(analyst)
                .withTask(work)
                .build();
            """).ToObject()!;

    [Fact]
    [Trait("BR", "CH-02")]
    public void ShouldReachConfiguration_WhenScriptCallsMaxExecutionTime()
    {
        var config = JsCrewConfigurationAdapter.ToConfiguration(Crew(".maxExecutionTime(90)"));

        Assert.Equal(TimeSpan.FromSeconds(90), Assert.Single(config.Agents).MaxExecutionTime);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("4294968")]
    [Trait("BR", "CH-02")]
    public void ShouldRefuseBuild_WhenScriptLimitIsNotPositive(string seconds)
    {
        var error = ThrowsContaining<InvalidScriptException>(() => Crew($".maxExecutionTime({seconds})"), ".maxExecutionTime");

        Assert.Contains("analyst", error.Message, StringComparison.Ordinal);
    }
}
