using Jint;
using Orkeon.Scripting.ErrorPolicy;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using static Orkeon.Tests.Shared.Assertions.AssertEx;

namespace Orkeon.Scripting.Tests.ErrorPolicy;

public sealed class ErrorPolicyTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();
    private static T Eval<T>(Engine engine, string js) => (T)engine.Evaluate(js).ToObject()!;

    [Fact]
    public async Task onError_fail_propagates_the_exception()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body((input, ctx) => { throw new Error("boom"); })
                .onError((err, ctx) => ErrorAction.fail())
                .build();
            crewBuilder().withAgent(a).build();
            """);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => crew.RunAsync(null, CancellationToken.None));
        Assert.Contains("boom", ChainText(ex));
    }

    [Fact]
    public async Task onError_skip_returns_null_output()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(() => { throw new Error("boom"); })
                .onError((err, ctx) => ErrorAction.skip())
                .build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Null(result.tasks[0].output);
    }

    [Fact]
    public async Task onError_fallback_substitutes_value()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(() => { throw new Error("boom"); })
                .onError((err, ctx) => ErrorAction.fallback("default"))
                .build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal("default", result.tasks[0].output!.ToString());
    }

    [Fact]
    public async Task onError_retry_re_executes_until_max_then_propagates()
    {
        var engine = NewEngine();
        engine.SetValue("__attempts", 0);
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body((input, ctx) => { __attempts += 1; throw new Error("flaky"); })
                .onError((err, ctx) => ErrorAction.retry({ max: 3, delay: 1 }))
                .build();
            crewBuilder().withAgent(a).build();
            """);

        await Assert.ThrowsAnyAsync<Exception>(() => crew.RunAsync(null, CancellationToken.None));
        Assert.Equal(3d, engine.GetValue("__attempts").AsNumber());
    }

    [Fact]
    public async Task onError_retry_succeeds_on_nth_attempt()
    {
        var engine = NewEngine();
        engine.SetValue("__attempts", 0);
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body((input, ctx) => {
                    __attempts += 1;
                    if (__attempts < 3) throw new Error("flaky");
                    return "ok";
                })
                .onError((err, ctx) => ErrorAction.retry({ max: 5, delay: 1 }))
                .build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal("ok", result.tasks[0].output!.ToString());
        Assert.Equal(3d, engine.GetValue("__attempts").AsNumber());
    }

    [Fact]
    public void ErrorCodeMapper_maps_StateMutationOutsideWithException_to_state_mutation_code()
    {
        Assert.Equal(ErrorCodeMapper.CodeStateMutation,
            ErrorCodeMapper.MapToCode(new StateMutationOutsideWithException("counter")));
    }

    [Fact]
    public void ErrorCodeMapper_maps_AgentNotInCrewException_to_agent_not_in_crew_code()
    {
        Assert.Equal(ErrorCodeMapper.CodeAgentNotInCrew,
            ErrorCodeMapper.MapToCode(new AgentNotInCrewException("A")));
    }

    [Fact]
    public void ErrorCodeMapper_maps_TimeoutException_to_timeout_code()
    {
        Assert.Equal(ErrorCodeMapper.CodeTimeout,
            ErrorCodeMapper.MapToCode(new TimeoutException("slow")));
    }

    [Fact]
    public void ErrorCodeMapper_maps_ReceiveTimeoutException_to_receive_timeout_code()
    {
        Assert.Equal(ErrorCodeMapper.CodeReceiveTimeout,
            ErrorCodeMapper.MapToCode(new ReceiveTimeoutException(TimeSpan.FromSeconds(1))));
    }

    [Fact]
    public void ErrorCodeMapper_unmapped_exception_returns_unknown()
    {
        Assert.Equal(ErrorCodeMapper.CodeUnknown,
            ErrorCodeMapper.MapToCode(new InvalidOperationException("?")));
    }
}
