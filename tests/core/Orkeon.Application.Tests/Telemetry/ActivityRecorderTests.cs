using System.Diagnostics;
using Orkeon.Tests.Shared.Telemetry;

namespace Orkeon.Application.Tests.Telemetry;

/// <summary>
/// GAP-41 — the recorder a span test reads instead of a list a global listener fills. An
/// <see cref="ActivityListener"/> hears the whole process: the tests running alongside stop
/// activities on their own threads while this one reads, so it reads copies, and keeps to its
/// own trace. Each test listens to a source of its own name, which no other test emits on.
/// </summary>
public sealed class ActivityRecorderTests
{
    private static ActivitySource NewSource() => new($"Orkeon.Tests.ActivityRecorder.{Guid.NewGuid():N}");

    [Fact]
    public void Reading_a_snapshot_while_a_listened_activity_stops_does_not_throw()
    {
        using var source = NewSource();
        using var recorder = new ActivityRecorder(source.Name);
        source.StartActivity("first")!.Dispose();

        // An activity stops while the snapshot is read — what a neighbouring test does from its
        // own thread, done here on this one: a list that grew under its reader would throw at
        // the very next step, every time.
        var read = 0;
        foreach (var _ in recorder.Snapshot())
        {
            source.StartActivity("during")!.Dispose();
            read++;
        }

        Assert.Equal(1, read);
        Assert.Equal(["first", "during"], recorder.Snapshot().Select(activity => activity.OperationName));
    }

    [Fact]
    public void A_trace_snapshot_holds_the_activities_under_its_root_only()
    {
        using var source = NewSource();
        using var recorder = new ActivityRecorder(source.Name);

        var mine = StopAChildUnderANewRoot(source, "mine");
        StopAChildUnderANewRoot(source, "a neighbour's");

        var traced = Assert.Single(recorder.Snapshot(mine));
        Assert.Equal("mine", traced.OperationName);
        Assert.Equal(2, recorder.Snapshot().Count);
    }

    [Fact]
    public void Once_disposed_it_records_nothing()
    {
        using var source = NewSource();
        using var recorder = new ActivityRecorder(source.Name);
        using var startedBefore = source.StartActivity("started before")!;

        recorder.Dispose();
        startedBefore.Stop();
        source.StartActivity("started after")?.Dispose();

        Assert.Empty(recorder.Snapshot());
    }

    /// <summary>Starts a root, the way a test isolates its trace, and stops one child under it.</summary>
    private static ActivityTraceId StopAChildUnderANewRoot(ActivitySource source, string name)
    {
        using var root = new Activity($"{name} root");
        root.Start();
        source.StartActivity(name)!.Dispose();
        return root.TraceId;
    }
}
