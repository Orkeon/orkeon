using Jint;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Adapters;

/// <summary>
/// GAP-22 — the <c>.ork.ts</c> twin of the YAML rule: <c>taskBuilder().asyncExecution()</c> means
/// something in a sequential crew (the task runs alongside the tasks after it) and in a parallel one
/// (a wave already runs at once); in the four modes that order their tasks themselves, the crew is
/// refused when it is adapted for the run, naming the task. It used to be carried and ignored.
/// </summary>
public sealed class JsCrewAsyncExecutionAdapterTests
{
    private static JsCrew BuildCrew(string process, string asyncExecution)
    {
        var engine = new JsEngineFactory().Create();
        return (JsCrew)engine.Evaluate($$"""
            const boss = agentBuilder().name("boss").role("Boss").goal("Lead the work").build();
            const analyst = agentBuilder().name("analyst").role("Analyst").goal("Analyze").build();

            const gather = taskBuilder()
                .name("gather").agent(analyst)
                .description("Gather the facts").expectedOutput("The facts")
                .asyncExecution({{asyncExecution}})
                .build();

            const write = taskBuilder()
                .name("write").agent(analyst)
                .description("Write the report").expectedOutput("A report")
                .withContext(gather)
                .build();

            crewBuilder()
                .name("watch").goal("Watch the market")
                .process("{{process}}")
                .manager(boss)
                .withAgents([boss, analyst])
                .withTasks([gather, write])
                .build();
            """).ToObject()!;
    }

    [Theory]
    [InlineData("hierarchical")]
    [InlineData("consensual")]
    [InlineData("graph")]
    [InlineData("autonomous")]
    public void A_mode_that_orders_its_tasks_itself_refuses_asyncExecution_naming_the_task(string process)
    {
        var crew = BuildCrew(process, "true");

        var ex = Assert.Throws<InvalidOperationException>(() => JsCrewConfigurationAdapter.ToConfiguration(crew));

        Assert.Contains("'gather'", ex.Message, StringComparison.Ordinal);
        Assert.Contains(".asyncExecution()", ex.Message, StringComparison.Ordinal);
        Assert.Contains($".process(\"{process}\")", ex.Message, StringComparison.Ordinal);
        Assert.Contains(".process(\"sequential\")", ex.Message, StringComparison.Ordinal);
        Assert.Contains(".process(\"parallel\")", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'write'", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sequential")]
    [InlineData("parallel")]
    public void Sequential_and_parallel_carry_it_to_the_task(string process)
    {
        var config = JsCrewConfigurationAdapter.ToConfiguration(BuildCrew(process, "true"));

        Assert.True(config.Tasks.Single(t => t.Description == "Gather the facts").AsyncExecution);
    }

    [Theory]
    [InlineData("sequential")]
    [InlineData("parallel")]
    [InlineData("hierarchical")]
    [InlineData("consensual")]
    [InlineData("graph")]
    [InlineData("autonomous")]
    public void AsyncExecution_false_is_accepted_in_every_mode(string process)
    {
        var config = JsCrewConfigurationAdapter.ToConfiguration(BuildCrew(process, "false"));

        Assert.All(config.Tasks, t => Assert.False(t.AsyncExecution));
    }
}
