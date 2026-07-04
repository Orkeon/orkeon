using System.Net;
using Jint;
using Orkeon.Scripting.Bindings;
using Orkeon.Scripting.ErrorPolicy;
using Orkeon.Scripting.Exceptions;

namespace Orkeon.Scripting.Tests.ErrorPolicy;

public sealed class ErrorPolicyExtraTests
{
    private static Engine NewEngine()
    {
        var engine = new JsEngineFactory().Create();
        ErrorActionBinding.Register(engine);
        return engine;
    }

    private static T Eval<T>(Engine engine, string js) => (T)engine.Evaluate(js).ToObject()!;

    [Fact]
    public void ErrorActionBinding_Register_null_engine_throws()
    {
        Assert.Throws<ArgumentNullException>(() => ErrorActionBinding.Register(null!));
    }

    [Fact]
    public void ErrorAction_fail_builds_fail_kind()
    {
        var action = Eval<JsErrorAction>(NewEngine(), "ErrorAction.fail()");

        Assert.Equal(JsErrorActionKind.Fail, action.kind);
    }

    [Fact]
    public void ErrorAction_skip_builds_skip_kind()
    {
        var action = Eval<JsErrorAction>(NewEngine(), "ErrorAction.skip()");

        Assert.Equal(JsErrorActionKind.Skip, action.kind);
    }

    [Fact]
    public void ErrorAction_fallback_carries_value()
    {
        var action = Eval<JsErrorAction>(NewEngine(), "ErrorAction.fallback('safe')");

        Assert.Equal(JsErrorActionKind.Fallback, action.kind);
        Assert.NotNull(action.fallbackValue);
        Assert.Equal("safe", action.fallbackValue!.AsString());
    }

    [Fact]
    public void ErrorAction_retry_without_options_has_no_delay_or_max()
    {
        var action = Eval<JsErrorAction>(NewEngine(), "ErrorAction.retry()");

        Assert.Equal(JsErrorActionKind.Retry, action.kind);
        Assert.Null(action.delay);
        Assert.Null(action.max);
    }

    [Fact]
    public void ErrorAction_retry_with_numeric_delay_and_max()
    {
        var action = Eval<JsErrorAction>(NewEngine(), "ErrorAction.retry({ delay: 500, max: 3 })");

        Assert.Equal(JsErrorActionKind.Retry, action.kind);
        Assert.Equal(TimeSpan.FromMilliseconds(500), action.delay);
        Assert.Equal(3, action.max);
    }

    [Theory]
    [InlineData("250ms", 250)]
    [InlineData("2s", 2000)]
    [InlineData("1m", 60000)]
    [InlineData("1h", 3600000)]
    public void ErrorAction_retry_with_string_delay_units(string text, double expectedMs)
    {
        var action = Eval<JsErrorAction>(NewEngine(), $"ErrorAction.retry({{ delay: '{text}' }})");

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), action.delay);
    }

    [Fact]
    public void ErrorAction_retry_with_invalid_string_delay_yields_zero()
    {
        var action = Eval<JsErrorAction>(NewEngine(), "ErrorAction.retry({ delay: 'oops' })");

        Assert.Equal(TimeSpan.Zero, action.delay);
    }

    [Fact]
    public void ErrorCodeMapper_maps_rate_limit_http_status()
    {
        var http = new HttpRequestException("too many", null, HttpStatusCode.TooManyRequests);

        Assert.Equal(ErrorCodeMapper.CodeRateLimit, ErrorCodeMapper.MapToCode(http));
    }

    [Fact]
    public void ErrorCodeMapper_maps_generic_http_to_network()
    {
        var http = new HttpRequestException("offline");

        Assert.Equal(ErrorCodeMapper.CodeNetwork, ErrorCodeMapper.MapToCode(http));
    }

    [Fact]
    public void ErrorCodeMapper_maps_TaskCanceledException_to_timeout()
    {
        Assert.Equal(ErrorCodeMapper.CodeTimeout, ErrorCodeMapper.MapToCode(new TaskCanceledException()));
    }

    [Fact]
    public void ErrorCodeMapper_maps_ArgumentException_to_validation()
    {
        Assert.Equal(ErrorCodeMapper.CodeValidation,
            ErrorCodeMapper.MapToCode(new ArgumentException("bad")));
    }

    [Fact]
    public void ErrorCodeMapper_maps_InvalidScriptException_to_validation()
    {
        Assert.Equal(ErrorCodeMapper.CodeValidation,
            ErrorCodeMapper.MapToCode(new InvalidScriptException("bad script")));
    }

    [Fact]
    public void ErrorCodeMapper_unwraps_AggregateException_to_inner_code()
    {
        var agg = new AggregateException(new TimeoutException("slow"));

        Assert.Equal(ErrorCodeMapper.CodeTimeout, ErrorCodeMapper.MapToCode(agg));
    }

    [Fact]
    public void ErrorCodeMapper_null_argument_throws()
    {
        Assert.Throws<ArgumentNullException>(() => ErrorCodeMapper.MapToCode(null!));
    }

    [Fact]
    public void JsErrorContext_and_JsAgentRef_expose_supplied_fields()
    {
        var inner = new InvalidOperationException("boom");
        var agent = new JsAgentRef("id-1", "Alice");
        var ctx = new JsErrorContext("tool_error", "failed", inner, 2, agent);

        Assert.Equal("tool_error", ctx.code);
        Assert.Equal("failed", ctx.message);
        Assert.Same(inner, ctx.exception);
        Assert.Equal(2, ctx.attempt);
        Assert.Same(agent, ctx.agent);
        Assert.Equal("id-1", ctx.agent.id);
        Assert.Equal("Alice", ctx.agent.name);
    }
}
