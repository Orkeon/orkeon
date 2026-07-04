using Jint;
using Jint.Native;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Runtime;

public sealed class CrewRunOptionsTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();

    [Fact]
    public void From_null_returns_no_signal_no_timeout()
    {
        var (signal, timeout) = CrewRunOptions.From(null);

        Assert.Null(signal);
        Assert.Null(timeout);
    }

    [Fact]
    public void From_undefined_returns_no_signal_no_timeout()
    {
        var (signal, timeout) = CrewRunOptions.From(JsValue.Undefined);

        Assert.Null(signal);
        Assert.Null(timeout);
    }

    [Fact]
    public void From_jsNull_returns_no_signal_no_timeout()
    {
        var (signal, timeout) = CrewRunOptions.From(JsValue.Null);

        Assert.Null(signal);
        Assert.Null(timeout);
    }

    [Fact]
    public void From_numeric_timeout_is_interpreted_as_milliseconds()
    {
        var engine = NewEngine();
        var opts = engine.Evaluate("({ timeout: 1500 })");

        var (signal, timeout) = CrewRunOptions.From(opts);

        Assert.Null(signal);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), timeout);
    }

    [Theory]
    [InlineData("250ms", 250)]
    [InlineData("2s", 2000)]
    [InlineData("3m", 180000)]
    [InlineData("1h", 3600000)]
    [InlineData("500", 500)]
    public void From_string_timeout_parses_units(string text, double expectedMs)
    {
        var engine = NewEngine();
        var opts = engine.Evaluate($"({{ timeout: '{text}' }})");

        var (_, timeout) = CrewRunOptions.From(opts);

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), timeout);
    }

    [Fact]
    public void From_object_without_timeout_or_signal_returns_nulls()
    {
        var engine = NewEngine();
        var opts = engine.Evaluate("({ inputs: { a: 1 } })");

        var (signal, timeout) = CrewRunOptions.From(opts);

        Assert.Null(signal);
        Assert.Null(timeout);
    }

    [Fact]
    public void From_signal_token_is_extracted()
    {
        var engine = NewEngine();
        using var cts = new CancellationTokenSource();
        engine.SetValue("__token", cts.Token);
        var opts = engine.Evaluate("({ signal: __token })");

        var (signal, _) = CrewRunOptions.From(opts);

        Assert.NotNull(signal);
        Assert.Equal(cts.Token, signal!.Value);
    }

    [Fact]
    public void From_invalid_duration_string_throws_FormatException()
    {
        var engine = NewEngine();
        var opts = engine.Evaluate("({ timeout: 'abc' })");

        Assert.Throws<FormatException>(() => CrewRunOptions.From(opts));
    }

    [Fact]
    public void From_unknown_unit_throws_FormatException()
    {
        var engine = NewEngine();
        var opts = engine.Evaluate("({ timeout: '10x' })");

        Assert.Throws<FormatException>(() => CrewRunOptions.From(opts));
    }

    [Fact]
    public void From_whitespace_string_yields_zero_timeout()
    {
        var engine = NewEngine();
        var opts = engine.Evaluate("({ timeout: '   ' })");

        var (_, timeout) = CrewRunOptions.From(opts);

        Assert.Equal(TimeSpan.Zero, timeout);
    }
}
