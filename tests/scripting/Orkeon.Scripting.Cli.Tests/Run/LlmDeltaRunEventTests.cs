using System.Text.Json;
using Orkeon.Hosting.Runs;
using Orkeon.Scripting.Cli.Events;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Cli.Tests.Doubles;
using Orkeon.Scripting.Cli.Tests.Forge;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// FWK-20: the <c>llm.delta</c> event says what it carries with <c>kind</c> (<c>content</c> or <c>tool_call</c>); a
/// tool-call fragment travels as <c>toolCall</c>; nothing flows unless <c>--stream</c> asked for it.
/// </summary>
public sealed class LlmDeltaRunEventTests
{
    private static List<JsonElement> Lines(StringWriter output) =>
        [.. output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement.Clone())];

    private static RunEventObserver Streaming(StringWriter output) =>
        new(new OrkeonEventWriter(output, new FakeOrkeonClock()), inner: null, stream: true);

    [Fact]
    [Trait("BR", "FWK-20")]
    public void ShouldTagATextDeltaAsContent()
    {
        using var output = new StringWriter();

        Streaming(output).OnDelta("hello");

        var line = Assert.Single(Lines(output));
        Assert.Equal("llm.delta", line.GetProperty("kind").GetString());
        Assert.Equal("hello", line.GetProperty("text").GetString());
        Assert.Equal("content", line.GetProperty("deltaKind").GetString());
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public void ShouldWriteAToolCallFragmentWithItsIndexIdNameAndArguments()
    {
        using var output = new StringWriter();

        Streaming(output).OnToolCallDelta(new ToolCallDelta(2, "call_1", "send_mail", "{\"to\":"));

        var line = Assert.Single(Lines(output));
        Assert.Equal("llm.delta", line.GetProperty("kind").GetString());
        Assert.Equal("tool_call", line.GetProperty("deltaKind").GetString());
        Assert.False(line.TryGetProperty("text", out _));
        var call = line.GetProperty("toolCall");
        Assert.Equal(2, call.GetProperty("index").GetInt32());
        Assert.Equal("call_1", call.GetProperty("id").GetString());
        Assert.Equal("send_mail", call.GetProperty("name").GetString());
        Assert.Equal("{\"to\":", call.GetProperty("argumentsDelta").GetString());
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public void ShouldLeaveOutTheIdAndTheName_WhenTheFragmentCarriesNone()
    {
        using var output = new StringWriter();

        Streaming(output).OnToolCallDelta(new ToolCallDelta(0, null, null, "1}"));

        var call = Assert.Single(Lines(output)).GetProperty("toolCall");
        Assert.False(call.TryGetProperty("id", out _));
        Assert.False(call.TryGetProperty("name", out _));
        Assert.Equal("1}", call.GetProperty("argumentsDelta").GetString());
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public void ShouldWriteNothing_WhenStreamWasNotAskedFor()
    {
        using var output = new StringWriter();
        var quiet = new RunEventObserver(new OrkeonEventWriter(output, new FakeOrkeonClock()), inner: null, stream: false);

        quiet.OnToolCallDelta(new ToolCallDelta(0, "call_1", "send_mail", "{}"));

        Assert.Empty(Lines(output));
    }
}
