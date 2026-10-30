using System.Collections.Immutable;
using Orkeon.Application.Crew;

namespace Orkeon.Application.Tests.Crew;

/// <summary>
/// Tests for the aggregate prompt-cache hit ratio surfaced on <see cref="CrewExecutionSnapshot"/>
/// (DeepSeek guideline §6.5).
/// </summary>
public class CrewExecutionSnapshotCacheRatioTests
{
    private static CrewExecutionSnapshot SnapshotWith(params (long? hit, long? miss)[] tasks)
    {
        var taskList = tasks
            .Select((t, i) => new TaskExecutionSnapshot
            {
                TaskId = $"t{i}",
                AgentRole = "agent",
                Success = true,
                Duration = TimeSpan.FromSeconds(1),
                CompletedAt = DateTimeOffset.UnixEpoch,
                CacheHitTokens = t.hit,
                CacheMissTokens = t.miss,
            })
            .ToImmutableList();

        return new CrewExecutionSnapshot
        {
            CrewId = "crew",
            StartedAt = DateTimeOffset.UnixEpoch,
            EndedAt = DateTimeOffset.UnixEpoch,
            Status = CrewHookStatus.Completed,
            Tasks = taskList,
        };
    }

    [Fact]
    public void TotalCacheHitRatio_IsNull_WhenNoCacheTokens()
    {
        Assert.Null(SnapshotWith((0, 0)).TotalCacheHitRatio);
    }

    [Fact]
    public void TotalCacheHitRatio_AggregatesAcrossTasks()
    {
        // (9800+200)/(9800+2540+200+1800) = 10000/14340 ≈ 0.6974
        var snapshot = SnapshotWith((9_800, 2_540), (200, 1_800));

        Assert.Equal(10_000L, snapshot.TotalCacheHitTokens);
        Assert.Equal(4_340L, snapshot.TotalCacheMissTokens);
        Assert.NotNull(snapshot.TotalCacheHitRatio);
        Assert.Equal(0.6974, snapshot.TotalCacheHitRatio!.Value, precision: 4);
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public void ShouldReadNullTotals_WhenNoTaskReportedTheCache()
    {
        var snapshot = SnapshotWith((null, null), (null, null));

        Assert.Null(snapshot.TotalCacheHitTokens);
        Assert.Null(snapshot.TotalCacheMissTokens);
        Assert.Null(snapshot.TotalCacheHitRatio);
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public void ShouldSumOnlyTheReportedCounts_WhenSomeTasksDidNotReport()
    {
        var snapshot = SnapshotWith((null, null), (0, 500));

        Assert.Equal(0L, snapshot.TotalCacheHitTokens);
        Assert.Equal(500L, snapshot.TotalCacheMissTokens);
        Assert.Equal(0.0, snapshot.TotalCacheHitRatio);
    }
}
