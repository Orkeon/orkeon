using System.Text.Json;
using RunEventKinds = Orkeon.Constants.Protocol.RunEventKinds;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Cli.Commands.Run;
using Orkeon.Scripting.Cli.Events;
using Orkeon.Scripting.Cli.Tests.Forge;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>The model calls on the run stream: llm.call.started / progress / finished.</summary>
public sealed class RunEventsLlmCallObserverTests : IDisposable
{
    public void Dispose() => _output.Dispose();

    private readonly StringWriter _output = new();
    private readonly ManualTimeProvider _time = new();

    private RunEventsLlmCallObserver Observer() => new(new OrkeonEventWriter(_output, new FakeOrkeonClock()), _time);

    private List<JsonElement> Lines() =>
    [
        .. _output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonDocument.Parse(l).RootElement.Clone()),
    ];

    [Fact]
    [Trait("BR", "BR-09")]
    public void ShouldWriteTheCallLinesOnTheRunStream()
    {
        var observer = Observer();

        observer.Started("c1", LlmCallMode.Stream, 42);
        observer.Progress("c1", 5);
        observer.Finished("c1", LlmCallMode.Buffer, fellBack: true, promptTokens: 40, completionTokens: null);

        var lines = Lines();
        Assert.Equal(
            [RunEventKinds.LlmCallStarted, RunEventKinds.LlmCallProgress, RunEventKinds.LlmCallFinished],
            lines.Select(l => l.GetProperty("kind").GetString()));
        Assert.Equal("c1", lines[0].GetProperty("callId").GetString());
        Assert.Equal(42, lines[0].GetProperty("estimatedPromptTokens").GetInt32());
        Assert.Equal("stream", lines[0].GetProperty("mode").GetString()!.ToLowerInvariant());
        Assert.Equal("c1", lines[1].GetProperty("callId").GetString());
        Assert.Equal(5, lines[1].GetProperty("estimatedCompletionTokens").GetInt32());
        Assert.Equal("c1", lines[2].GetProperty("callId").GetString());
        Assert.Equal("buffer", lines[2].GetProperty("mode").GetString()!.ToLowerInvariant());
        Assert.True(lines[2].GetProperty("fellBack").GetBoolean());
        Assert.Equal(40, lines[2].GetProperty("promptTokens").GetInt32());
        Assert.True(!lines[2].TryGetProperty("completionTokens", out var c) || c.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    [Trait("BR", "BR-08")]
    public void ShouldWriteAtMostOneProgressLinePerQuarterSecond_AndTheLastBeforeTheEnd()
    {
        var observer = Observer();
        observer.Started("c1", LlmCallMode.Stream, 3);

        observer.Progress("c1", 1);
        _time.Advance(TimeSpan.FromMilliseconds(100));
        observer.Progress("c1", 2);
        _time.Advance(TimeSpan.FromMilliseconds(100));
        observer.Progress("c1", 3);
        _time.Advance(RunEventsLlmCallObserver.ProgressInterval);
        observer.Progress("c1", 4);
        _time.Advance(TimeSpan.FromMilliseconds(10));
        observer.Progress("c1", 9);
        observer.Finished("c1", LlmCallMode.Stream, fellBack: false, promptTokens: 3, completionTokens: 9);

        var lines = Lines();
        var progress = lines.Where(l => l.GetProperty("kind").GetString() == RunEventKinds.LlmCallProgress).ToList();
        Assert.Equal([1, 4, 9], progress.Select(l => l.GetProperty("estimatedCompletionTokens").GetInt32()));
        Assert.Equal(RunEventKinds.LlmCallProgress, lines[^2].GetProperty("kind").GetString());
        Assert.Equal(RunEventKinds.LlmCallFinished, lines[^1].GetProperty("kind").GetString());
    }
}
