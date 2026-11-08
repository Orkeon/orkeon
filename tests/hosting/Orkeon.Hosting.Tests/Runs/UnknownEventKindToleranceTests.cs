using System.Text.Json.Nodes;
using Orkeon.Application.Runs;
using Orkeon.Hosting.Runs;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Hosting.Tests.Runs;

/// <summary>
/// FWK-08, extending the tolerance rule of the plan (a new event <c>kind</c> is allowed, a new field is additive): the
/// file sink stores a kind this build does not know and the history reader reads it back untouched, so an older reader of
/// a newer history, or a newer kind written by a later lot, loses nothing and breaks nothing.
/// </summary>
public sealed class UnknownEventKindToleranceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-runs-unknown-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new();
    private readonly DiskBackedFileSystemService _fs;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public UnknownEventKindToleranceTests()
    {
        Directory.CreateDirectory(_root);
        _fs = new DiskBackedFileSystemService(_root, "/runs");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldStoreAndReadBackAKindNoBuildKnows_WithItsFields()
    {
        var options = new RunHistoryOptions { Enabled = true };
        var run = new RunEventPublisher([new JsonLinesFileRunEventSink(_fs, options, _time)], _time, runId: "run-x");

        run.Emit("run.started", new { target = "crew" });
        run.Emit("future.thing.happened", new RunEventScope { AgentId = "a1" }, new { shiny = true, depth = 3 });
        run.Emit("run.finished", new { success = true });

        var events = await new JsonLinesRunHistoryReader(_fs, options).ReadEventsAsync("run-x", 1, Ct);

        var future = Assert.Single(events, e => e.Kind == "future.thing.happened");
        Assert.Equal("a1", future.AgentId);
        Assert.True(future.Payload?["shiny"]?.GetValue<bool>());
        Assert.Equal(3, future.Payload?["depth"]?.GetValue<int>());
        Assert.Equal(3, events.Count);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldSkipALineThatIsNotAnEvent_AndKeepReadingTheRest()
    {
        var options = new RunHistoryOptions { Enabled = true };
        var run = new RunEventPublisher([new JsonLinesFileRunEventSink(_fs, options, _time)], _time, runId: "run-y");
        run.Emit("run.started", new { target = "crew" });
        await _fs.AppendAllTextAsync("/runs/run-y.jsonl", "{ this is a torn line\n{\"v\":9,\"seq\":2,\"kind\":\"later.kind\",\"extra\":[1,2]}\n", Ct);

        var events = await new JsonLinesRunHistoryReader(_fs, options).ReadEventsAsync("run-y", 1, Ct);

        Assert.Equal(["run.started", "later.kind"], events.Select(e => e.Kind));
        Assert.IsType<JsonArray>(events[1].Payload?["extra"]);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public void ShouldParseALineOfAnotherEnvelopeVersion()
    {
        var parsed = RunEventWire.TryParse("{\"v\":7,\"seq\":4,\"ts\":\"2030-01-02T03:04:05Z\",\"kind\":\"x.y\",\"crewId\":\"c\",\"n\":1}", out var runEvent);

        Assert.True(parsed);
        Assert.Equal(4, runEvent!.Seq);
        Assert.Equal("c", runEvent.CrewId);
        Assert.Equal(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero), runEvent.Timestamp);
        Assert.False(RunEventWire.TryParse("[1,2]", out _));
        Assert.False(RunEventWire.TryParse("{\"seq\":1}", out _));
    }
}
