using Jint;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using static Orkeon.Tests.Shared.Assertions.AssertEx;

namespace Orkeon.Scripting.Tests.Builders;

public sealed class TaskBuilderTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();

    private static T Eval<T>(Engine engine, string js)
        => (T)engine.Evaluate(js).ToObject()!;

    [Fact]
    public void TaskBuilder_minimum_required_fields_builds()
    {
        var task = Eval<JsTask>(NewEngine(), """
            const a = agentBuilder().name("A").role("R").goal("G").build();
            taskBuilder()
              .description("Investigate quantum computing")
              .agent(a)
              .expectedOutput("3-bullet summary")
              .build();
            """);

        Assert.Equal("Investigate quantum computing", task.description);
        Assert.Equal("3-bullet summary", task.expectedOutput);
        Assert.Equal(26, task.id.Length);
    }

    [Fact]
    public void TaskBuilder_missing_agent_throws_InvalidScriptException()
    {
        var ex = ThrowsContaining<InvalidScriptException>(
            () => NewEngine().Evaluate("""
                taskBuilder().description("x").expectedOutput("y").build();
                """),
            "agent");

        Assert.NotNull(ex);
    }

    [Fact]
    public void TaskBuilder_missing_description_throws_InvalidScriptException()
    {
        var ex = ThrowsContaining<InvalidScriptException>(
            () => NewEngine().Evaluate("""
                const a = agentBuilder().name("A").role("R").goal("G").build();
                taskBuilder().agent(a).expectedOutput("y").build();
                """),
            "description");

        Assert.NotNull(ex);
    }

    [Fact]
    public void TaskBuilder_withContext_chains_tasks()
    {
        var task = Eval<JsTask>(NewEngine(), """
            const a = agentBuilder().name("A").role("R").goal("G").build();
            const t1 = taskBuilder().description("first").agent(a).expectedOutput("o1").build();
            const t2 = taskBuilder().description("second").agent(a).expectedOutput("o2").withContext(t1).build();
            t2;
            """);

        Assert.Single(task.Context);
        Assert.Equal("first", task.Context[0].description);
    }

    [Fact]
    public void TaskBuilder_withContexts_accepts_array()
    {
        var task = Eval<JsTask>(NewEngine(), """
            const a = agentBuilder().name("A").role("R").goal("G").build();
            const t1 = taskBuilder().description("first").agent(a).expectedOutput("o1").build();
            const t2 = taskBuilder().description("second").agent(a).expectedOutput("o2").build();
            taskBuilder().description("third").agent(a).expectedOutput("o3").withContexts([t1, t2]).build();
            """);

        Assert.Equal(2, task.Context.Count);
    }
}
