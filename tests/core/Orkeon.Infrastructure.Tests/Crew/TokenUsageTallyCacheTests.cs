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
    private static TaskResult Result(int tokens, long cacheHit, long cacheMiss) =>
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
    public void An_unmeasured_cache_writes_no_key_at_all()
    {
        var tally = new TokenUsageTally();
        tally.Record(Result(100, cacheHit: 0, cacheMiss: 0));

        var metadata = tally.WriteTo(CrewMetadata.CreateBuilder()).Build();

        Assert.Equal(100, metadata.Get<int>(CrewMetadata.TotalTokensKey));
        Assert.False(metadata.Contains(CrewMetadata.CacheHitTokensKey));
        Assert.False(metadata.Contains(CrewMetadata.CacheMissTokensKey));
    }
}
