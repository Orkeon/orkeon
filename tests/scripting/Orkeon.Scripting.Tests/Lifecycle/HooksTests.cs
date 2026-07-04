using Jint;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Lifecycle;

public sealed class HooksTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();
    private static T Eval<T>(Engine engine, string js) => (T)engine.Evaluate(js).ToObject()!;

    [Fact]
    public void onAgentStart_invoked_at_crew_add()
    {
        var engine = NewEngine();
        engine.SetValue("__started", false);
        engine.Evaluate("""
            const a = agentBuilder().name("A").role("R").goal("G")
                .onAgentStart((ctx) => { __started = true; })
                .build();
            crewBuilder().withAgent(a).build();
            """);

        Assert.True(engine.GetValue("__started").AsBoolean());
    }

    [Fact]
    public void onAgentStop_invoked_at_crew_remove()
    {
        var engine = NewEngine();
        engine.SetValue("__stopped", false);
        engine.Evaluate("""
            const a = agentBuilder().name("A").role("R").goal("G")
                .onAgentStop((ctx) => { __stopped = true; })
                .build();
            const c = crewBuilder().withAgent(a).build();
            c.remove(a);
            """);

        Assert.True(engine.GetValue("__stopped").AsBoolean());
    }

    [Fact]
    public async Task onCrewStart_invoked_before_first_task_body_runs()
    {
        var engine = NewEngine();
        engine.SetValue("__order", new List<object>());
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(() => { __order.push("body"); return "ok"; }).build();
            crewBuilder().withAgent(a)
                .onCrewStart(ctx => { __order.push("start"); })
                .build();
            """);

        await crew.RunAsync(null, CancellationToken.None);

        var order = (List<object>)engine.GetValue("__order").ToObject()!;
        Assert.Equal(new object[] { "start", "body" }, order);
    }

    [Fact]
    public async Task onCrewComplete_invoked_after_last_task_with_result()
    {
        var engine = NewEngine();
        engine.SetValue("__completed", false);
        engine.SetValue("__output", "");
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(() => "final").build();
            crewBuilder().withAgent(a)
                .onCrewComplete((ctx, result) => { __completed = true; __output = result.output; })
                .build();
            """);

        await crew.RunAsync(null, CancellationToken.None);

        Assert.True(engine.GetValue("__completed").AsBoolean());
        Assert.Equal("final", engine.GetValue("__output").AsString());
    }

    [Fact]
    public async Task onCrewError_invoked_when_uncaught_exception_propagates()
    {
        var engine = NewEngine();
        engine.SetValue("__errMsg", "");
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(() => { throw new Error("kaboom"); })
                .build();
            crewBuilder().withAgent(a)
                .onCrewError((ctx, msg) => { __errMsg = msg; })
                .build();
            """);

        await Assert.ThrowsAnyAsync<Exception>(() => crew.RunAsync(null, CancellationToken.None));

        Assert.Contains("kaboom", engine.GetValue("__errMsg").AsString());
    }

    [Fact]
    public async Task onCrewError_not_invoked_when_onError_handles_the_exception()
    {
        var engine = NewEngine();
        engine.SetValue("__errInvoked", false);
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(() => { throw new Error("recovered"); })
                .onError(err => ErrorAction.fallback("ok"))
                .build();
            crewBuilder().withAgent(a)
                .onCrewError(ctx => { __errInvoked = true; })
                .build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal("ok", result.tasks[0].output!.ToString());
        Assert.False(engine.GetValue("__errInvoked").AsBoolean());
    }
}
