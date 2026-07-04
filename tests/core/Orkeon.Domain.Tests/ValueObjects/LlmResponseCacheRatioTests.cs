using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

public class LlmResponseCacheRatioTests
{
    [Fact]
    public void CacheHitRatio_IsNull_WhenNoCacheTokensReported()
    {
        var response = new LlmResponse { Content = "hi" };
        Assert.Null(response.CacheHitRatio);
    }

    [Fact]
    public void CacheHitRatio_IsNull_WhenOnlyOneSideReported()
    {
        Assert.Null(new LlmResponse { CacheHitTokens = 100 }.CacheHitRatio);
        Assert.Null(new LlmResponse { CacheMissTokens = 100 }.CacheHitRatio);
    }

    [Fact]
    public void CacheHitRatio_IsNull_WhenBothZero()
    {
        var response = new LlmResponse { CacheHitTokens = 0, CacheMissTokens = 0 };
        Assert.Null(response.CacheHitRatio);
    }

    [Theory]
    [InlineData(9800, 2540, 0.7942)]   // DeepSeek §6.5 worked example (9800/12340)
    [InlineData(100, 0, 1.0)]          // full hit
    [InlineData(0, 100, 0.0)]          // full miss
    public void CacheHitRatio_ComputesHitOverHitPlusMiss(int hit, int miss, double expected)
    {
        var response = new LlmResponse { CacheHitTokens = hit, CacheMissTokens = miss };
        Assert.NotNull(response.CacheHitRatio);
        Assert.Equal(expected, response.CacheHitRatio!.Value, precision: 4);
    }
}
