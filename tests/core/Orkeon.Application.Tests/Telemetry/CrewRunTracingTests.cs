using System.Diagnostics;
using Orkeon.Application.Telemetry;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Tests.Shared.Telemetry;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Application.Tests.Telemetry;

/// <summary>
/// CH-06 - <see cref="CrewRunTracing"/>: the task span of a crew run is made current only by
/// <c>EnterTask</c>, per flow; outside a run it does nothing.
/// </summary>
public sealed class CrewRunTracingTests
{
    private const string TestSourceName = "Orkeon.Tests.CrewRunTracing";

    private static ActivityRecorder Record() => new([.. OrkeonActivitySources.AllNames, TestSourceName]);

#pragma warning disable CA2000 // the test disposes the root it takes
    private static Activity StartRoot()
    {
        var root = new Activity("test.root");
        root.Start();
        return root;
    }

    [Fact]
    [Trait("BR", "CH-06")]
    public void ShouldDoNothing_WhenNoRunIsOpen()
    {
        using var recorder = Record();
        using var root = StartRoot();
        var id = TaskId.Create();

        CrewRunTracing.StartTask(id, "writer");
        CrewRunTracing.HandOver(id, "reviewer");
        using (CrewRunTracing.EnterTask(id))
            Assert.Same(root, Activity.Current);
        CrewRunTracing.EndTask(id, TaskSpanStatus.Completed, null, null);

        Assert.Same(root, Activity.Current);
        Assert.Empty(recorder.Snapshot(root.TraceId));
    }

    [Fact]
    [Trait("BR", "CH-06")]
    public void ShouldRestoreEnclosingActivity_WhenTaskSpanIsLeft()
    {
        using var recorder = Record();
        using var root = StartRoot();
        var id = TaskId.Create();

        using var run = CrewRunTracing.Begin(DomainCrew.Create("Write the notes"));
        var enclosing = Activity.Current;
        CrewRunTracing.StartTask(id, "writer");
        Assert.Same(enclosing, Activity.Current);

        using (CrewRunTracing.EnterTask(id))
        {
            Assert.NotNull(Activity.Current);
            Assert.Equal("task.run", Activity.Current.OperationName);
            Assert.Equal(id.ToString(), Activity.Current.GetTagItem("orkeon.task.id")?.ToString());
        }

        Assert.Same(enclosing, Activity.Current);
        CrewRunTracing.EndTask(id, TaskSpanStatus.Completed, null, null);
    }

    [Fact]
    [Trait("BR", "CH-06")]
    public async System.Threading.Tasks.Task ShouldKeepTwoFlowsApart_WhenEachEntersItsTask()
    {
        using var recorder = Record();
        using var root = StartRoot();
        using var source = new ActivitySource(TestSourceName);
        var first = TaskId.Create();
        var second = TaskId.Create();
        using var run = CrewRunTracing.Begin(DomainCrew.Create("Write the notes"));
        CrewRunTracing.StartTask(first, "writer");
        CrewRunTracing.StartTask(second, "reviewer");
        var entered = new CountdownEvent(2);
        var hangGuard = TestContext.Current.CancellationToken;

        async Task<ActivityContext> Flow(TaskId id)
        {
            using (CrewRunTracing.EnterTask(id))
            {
                entered.Signal();
                await System.Threading.Tasks.Task.Run(() => entered.Wait(hangGuard), hangGuard);
                using var child = source.StartActivity("child");
                Assert.NotNull(child);
                return child.Context;
            }
        }

        var a = System.Threading.Tasks.Task.Run(() => Flow(first), hangGuard);
        var b = System.Threading.Tasks.Task.Run(() => Flow(second), hangGuard);
        var childA = await a;
        var childB = await b;
        CrewRunTracing.EndTask(first, TaskSpanStatus.Completed, null, null);
        CrewRunTracing.EndTask(second, TaskSpanStatus.Completed, null, null);
        run.Dispose();

        var spans = recorder.Snapshot(root.TraceId);
        var taskA = Assert.Single(spans, s => s.OperationName == "task.run" && s.GetTagItem("orkeon.task.id")?.ToString() == first.ToString());
        var taskB = Assert.Single(spans, s => s.OperationName == "task.run" && s.GetTagItem("orkeon.task.id")?.ToString() == second.ToString());
        var recordedA = Assert.Single(spans, s => s.SpanId == childA.SpanId);
        var recordedB = Assert.Single(spans, s => s.SpanId == childB.SpanId);
        Assert.Equal(taskA.SpanId, recordedA.ParentSpanId);
        Assert.Equal(taskB.SpanId, recordedB.ParentSpanId);
    }
}
