using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Infrastructure.Crew.Strategies;

namespace Orkeon.Infrastructure.Tests.Crew;

/// <summary>
/// The cache dimension of the token tally (W-08): accumulated from the task results,
/// written to the canonical metadata keys only when actually measured.
/// </summary>
public sealed class TokenUsageTallyCacheTests
{
    private static TaskResult Result(int tokens, long? cacheHit, long? cacheMiss) =>
        new(Success: true, Output: "ok", StructuredOutput: null, ToolsUsed: [],
            ExecutionTime: TimeSpan.FromMilliseconds(5), TokensUsed: tokens)
        {
            PromptTokens = tokens - 10,
            CompletionTokens = 10,
            CacheHitTokens = cacheHit,
            CacheMissTokens = cacheMiss,
        };

    [Fact]
    public void Cache_counters_accumulate_and_reach_the_canonical_metadata_keys()
    {
        var tally = new TokenUsageTally();
        tally.Record(Result(100, cacheHit: 60, cacheMiss: 30));
        tally.Record(Result(50, cacheHit: 20, cacheMiss: 20));

        var metadata = tally.WriteTo(CrewMetadata.CreateBuilder()).Build();

        Assert.Equal(80L, metadata.Get<long>(CrewMetadata.CacheHitTokensKey));
        Assert.Equal(50L, metadata.Get<long>(CrewMetadata.CacheMissTokensKey));
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public void An_unmeasured_cache_writes_no_key_at_all()
    {
        var tally = new TokenUsageTally();
        tally.Record(Result(100, cacheHit: null, cacheMiss: null));

        var metadata = tally.WriteTo(CrewMetadata.CreateBuilder()).Build();

        Assert.Equal(100, metadata.Get<int>(CrewMetadata.TotalTokensKey));
        Assert.False(metadata.Contains(CrewMetadata.CacheHitTokensKey));
        Assert.False(metadata.Contains(CrewMetadata.CacheMissTokensKey));
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public void ShouldKeepCacheCountsNull_WhenNoTaskReportedThem()
    {
        var tally = new TokenUsageTally();
        tally.Record(Result(100, cacheHit: null, cacheMiss: null));

        Assert.Null(tally.CacheHitTokens);
        Assert.Null(tally.CacheMissTokens);
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public void ShouldWriteAReportedZero_WhenTheProviderMeasuredNoCacheHit()
    {
        var tally = new TokenUsageTally();
        tally.Record(Result(100, cacheHit: 0, cacheMiss: 90));
        tally.Record(Result(50, cacheHit: null, cacheMiss: null));

        var metadata = tally.WriteTo(CrewMetadata.CreateBuilder()).Build();

        Assert.Equal(0L, tally.CacheHitTokens);
        Assert.Equal(0L, metadata.Get<long>(CrewMetadata.CacheHitTokensKey));
        Assert.Equal(90L, metadata.Get<long>(CrewMetadata.CacheMissTokensKey));
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public void ShouldMeterNullCache_WhenNoResultSinceTheMarkReportedIt()
    {
        var tally = new TokenUsageTally();
        tally.Record(Result(100, cacheHit: 60, cacheMiss: 30));
        var mark = tally.Mark();
        tally.Record(Result(50, cacheHit: null, cacheMiss: null));

        var used = tally.Since(mark);

        Assert.Equal(50, used.Tokens);
        Assert.Null(used.CacheHit);
        Assert.Null(used.CacheMiss);
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public void ShouldMeterTheReportedDelta_WhenAResultSinceTheMarkReportedIt()
    {
        var tally = new TokenUsageTally();
        var mark = tally.Mark();
        tally.Record(Result(50, cacheHit: 0, cacheMiss: 40));

        var used = tally.Since(mark);

        Assert.Equal(0L, used.CacheHit);
        Assert.Equal(40L, used.CacheMiss);
    }
}
