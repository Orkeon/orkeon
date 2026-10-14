using System.Diagnostics;
using Orkeon.Application.Telemetry;
using Orkeon.Tests.Shared.Telemetry;

namespace Orkeon.E2E.Tests;

public partial class OrchestrationModesOfflineTests
{
    /// <summary>
    /// CH-06: in every mode a two-task crew run is one <c>crew.run</c> under the caller's activity, one
    /// <c>task.run</c> per task, and every agent turn under the task it works on.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modes))]
    [Trait("BR", "CH-06")]
    public async Task ShouldNestAgentTurnsUnderTheirTask_WhenTwoTaskCrewRuns(string mode)
    {
        using var recorder = new ActivityRecorder(OrkeonActivitySources.AllNames);
        using var root = new Activity("test.root");
        root.Start();

        var (output, _, _, _) = await RunAsync(CrewYaml(mode), TestContext.Current.CancellationToken);

        Assert.NotNull(output);
        var spans = recorder.Snapshot(root.TraceId);
        var run = Assert.Single(spans, s => s.OperationName == "crew.run");
        Assert.Equal(root.SpanId, run.ParentSpanId);
        Assert.False(string.IsNullOrEmpty(run.GetTagItem("orkeon.crew.name")?.ToString()));
        Assert.False(string.IsNullOrEmpty(run.GetTagItem("orkeon.crew.id")?.ToString()));
        Assert.False(string.IsNullOrEmpty(run.GetTagItem("orkeon.crew.process")?.ToString()));

        var tasks = spans.Where(s => s.OperationName == "task.run").ToList();
        Assert.Equal(2, tasks.Count);
        Assert.All(tasks, t => Assert.Equal(run.SpanId, t.ParentSpanId));
        var taskIds = tasks.Select(t => t.GetTagItem("orkeon.task.id")?.ToString()).ToList();
        Assert.All(taskIds, id => Assert.False(string.IsNullOrEmpty(id)));
        Assert.Equal(2, taskIds.Distinct().Count());

        var turns = spans.Where(s => s.OperationName.StartsWith("invoke_agent", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(turns);
        var known = spans.Select(s => s.SpanId).ToHashSet();
        foreach (var turn in turns)
        {
            Assert.NotEqual(default, turn.ParentSpanId);
            Assert.Contains(turn.ParentSpanId, known);
            var taskId = turn.GetTagItem("orkeon.task.id")?.ToString();
            if (taskId is not null)
            {
                var owner = Assert.Single(tasks, t => t.GetTagItem("orkeon.task.id")?.ToString() == taskId);
                Assert.Equal(owner.SpanId, turn.ParentSpanId);
            }
        }

        Assert.All(taskIds, id => Assert.Contains(turns, t => t.GetTagItem("orkeon.task.id")?.ToString() == id));
    }
}
