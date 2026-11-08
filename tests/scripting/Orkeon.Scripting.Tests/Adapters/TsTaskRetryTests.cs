using Jint;
using Orkeon.Domain.Task;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Adapters;

/// <summary>
/// FWK-15: the <c>.ork.ts</c> twin of the YAML <c>retry:</c> block, <c>taskBuilder().retry({...})</c>:
/// it reaches the task's configuration, and a value the policy refuses fails the script at the call.
/// </summary>
public sealed class TsTaskRetryTests
{
    private static JsCrew Crew(string retryCall, string process = "sequential") =>
        (JsCrew)new JsEngineFactory().Create().Evaluate($$"""
            const analyst = agentBuilder().name("analyst").role("Analyst").goal("Analyze").build();
            const gather = taskBuilder()
                .name("gather").agent(analyst)
                .description("Gather the facts").expectedOutput("The facts")
                {{retryCall}}
                .build();
            crewBuilder().name("watch").goal("Watch").process("{{process}}")
                .withAgents([analyst]).withTasks([gather]).build();
            """).ToObject()!;

    [Fact]
    [Trait("BR", "FWK-15")]
    public void ShouldCarryTheRetryPolicyToTheTaskConfiguration()
    {
        var config = JsCrewConfigurationAdapter.ToConfiguration(
            Crew(".retry({ attempts: 3, backoffMs: 250, backoff: \"exponential\" })"));

        var retry = Assert.IsType<TaskRetryPolicy>(config.Tasks.Single().Retry);
        Assert.Equal(3, retry.Attempts);
        Assert.Equal(250, retry.BackoffMs);
        Assert.Equal(TaskRetryBackoff.Exponential, retry.Backoff);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public void ShouldDefaultTheBackoff()
    {
        var config = JsCrewConfigurationAdapter.ToConfiguration(Crew(".retry({ attempts: 2 })"));

        var retry = Assert.IsType<TaskRetryPolicy>(config.Tasks.Single().Retry);
        Assert.Equal(0, retry.BackoffMs);
        Assert.Equal(TaskRetryBackoff.Fixed, retry.Backoff);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public void ShouldLeaveRetryNull_WhenNotCalled()
    {
        var config = JsCrewConfigurationAdapter.ToConfiguration(Crew(""));

        Assert.Null(config.Tasks.Single().Retry);
    }

    [Theory]
    [InlineData(".retry({ attempts: 0 })", "attempts")]
    [InlineData(".retry({ backoffMs: 100 })", "attempts")]
    [InlineData(".retry({ attempts: 2, backoffMs: -1 })", "backoffMs")]
    [InlineData(".retry({ attempts: 2, backoff: \"linear\" })", "linear")]
    [InlineData(".retry(3)", "object")]
    [Trait("BR", "FWK-15")]
    public void ShouldRefuseABadPolicyAtTheCall(string call, string mentions)
    {
        var ex = Assert.ThrowsAny<Exception>(() => Crew(call));

        Assert.Contains(mentions, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public void ShouldRefuseARetryOnADecisionTask()
    {
        var ex = Assert.ThrowsAny<Exception>(() => new JsEngineFactory().Create().Evaluate("""
            taskBuilder().name("triage").retry({ attempts: 2 })
                .decision({ questions: { category: { choice: { bug: "a defect" } } }, fallback: { category: "bug" } })
                .build();
            """));

        Assert.Contains("retry", ex.ToString(), StringComparison.Ordinal);
    }
}
