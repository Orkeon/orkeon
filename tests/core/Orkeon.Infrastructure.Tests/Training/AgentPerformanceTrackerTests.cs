using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
namespace Orkeon.Infrastructure.Tests.Training;

public class AgentPerformanceTrackerTests
{
    [Fact]
    public async Task RecordPerformanceAsync_StoresDataPoint()
    {
        var tracker = AgentPerformanceTrackerTestsFixture.CreateTracker();

        await tracker.RecordPerformanceAsync(AgentId1, TaskId1, 0.85, ct: TestContext.Current.CancellationToken);

        var trend = await tracker.GetPerformanceTrendAsync(AgentId1, ct: TestContext.Current.CancellationToken);

        Assert.Single(trend.DataPoints);
        Assert.Equal(TaskId1, trend.DataPoints[0].TaskId);
        Assert.Equal(0.85, trend.DataPoints[0].Score);
    }

    [Fact]
    public async Task GetPerformanceTrendAsync_ReturnsLastNPoints()
    {
        var tracker = AgentPerformanceTrackerTestsFixture.CreateTracker();

        for (var i = 1; i <= 15; i++)
        {
            await tracker.RecordPerformanceAsync(AgentId1, $"task-{i}", 0.5 + i * 0.02, ct: TestContext.Current.CancellationToken);
        }

        var trend = await tracker.GetPerformanceTrendAsync(AgentId1, lastN: 5, TestContext.Current.CancellationToken);

        Assert.Equal(5, trend.DataPoints.Count);
        Assert.Equal("task-11", trend.DataPoints[0].TaskId);
        Assert.Equal("task-15", trend.DataPoints[4].TaskId);
    }

    [Fact]
    public async Task GetPerformanceTrendAsync_ImprovementCalculationCorrect()
    {
        var tracker = AgentPerformanceTrackerTestsFixture.CreateTracker();

        await tracker.RecordPerformanceAsync(AgentId1, TaskId1, 0.4, ct: TestContext.Current.CancellationToken);
        await tracker.RecordPerformanceAsync(AgentId1, TaskId2, 0.6, ct: TestContext.Current.CancellationToken);
        await tracker.RecordPerformanceAsync(AgentId1, TaskId3, 0.9, ct: TestContext.Current.CancellationToken);

        var trend = await tracker.GetPerformanceTrendAsync(AgentId1, ct: TestContext.Current.CancellationToken);

        // Improvement = last score - first score = 0.9 - 0.4 = 0.5
        Assert.Equal(0.5, trend.Improvement, 4);
    }

    [Fact]
    public async Task GetPerformanceTrendAsync_EmptyHistory_ReturnsZeroTrend()
    {
        var tracker = AgentPerformanceTrackerTestsFixture.CreateTracker();

        var trend = await tracker.GetPerformanceTrendAsync("nonexistent-agent", ct: TestContext.Current.CancellationToken);

        Assert.Empty(trend.DataPoints);
        Assert.Equal(0.0, trend.AverageScore);
        Assert.Equal(0.0, trend.Improvement);
        Assert.Equal("nonexistent-agent", trend.AgentId);
    }

    [Fact]
    public async Task RecordPerformanceAsync_WithMetadata_StoresMetadata()
    {
        var tracker = AgentPerformanceTrackerTestsFixture.CreateTracker();
        var metadata = new Dictionary<string, object> { ["model"] = ModelGpt4, ["tokens"] = 150 };

        await tracker.RecordPerformanceAsync(AgentId1, TaskId1, 0.75, metadata, TestContext.Current.CancellationToken);

        var trend = await tracker.GetPerformanceTrendAsync(AgentId1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(trend.DataPoints[0].Metadata);
        Assert.Equal(ModelGpt4, trend.DataPoints[0].Metadata!["model"]);
    }

    [Fact]
    public async Task GetPerformanceTrendAsync_SinglePoint_ZeroImprovement()
    {
        var tracker = AgentPerformanceTrackerTestsFixture.CreateTracker();

        await tracker.RecordPerformanceAsync(AgentId1, TaskId1, 0.7, ct: TestContext.Current.CancellationToken);

        var trend = await tracker.GetPerformanceTrendAsync(AgentId1, ct: TestContext.Current.CancellationToken);

        Assert.Equal(0.0, trend.Improvement);
        Assert.Equal(0.7, trend.AverageScore, 4);
    }
}
