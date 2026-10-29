using System.Net;
using Orkeon.Infrastructure.Resilience;

namespace Orkeon.Infrastructure.Tests.Resilience;

public sealed class RateLimitResetHeadersTests
{
    [Fact]
    [Trait("BR", "RT-13")]
    public void ShouldParseLongestReset_WhenSeveralRateLimitHeadersPresent()
    {
        var now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("Retry-After", "10");
        response.Headers.TryAddWithoutValidation("anthropic-ratelimit-tokens-reset", now.AddSeconds(90).ToString("o"));
        response.Headers.TryAddWithoutValidation("anthropic-ratelimit-requests-reset", now.AddSeconds(30).ToString("o"));
        response.Headers.TryAddWithoutValidation("x-ratelimit-reset-requests", "6m0s");
        response.Headers.TryAddWithoutValidation("x-ratelimit-reset-tokens", "1s");

        var parsed = RateLimitResetHeaders.Parse(response.Headers, now);

        Assert.Equal(TimeSpan.FromMinutes(6), parsed.Wait);
    }

    [Fact]
    public void ShouldReadRetryAfterMs_WhenItAsksForTheLongestWait()
    {
        var now = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("Retry-After", "1");
        response.Headers.TryAddWithoutValidation("retry-after-ms", "2500");

        var parsed = RateLimitResetHeaders.Parse(response.Headers, now);

        Assert.Equal(TimeSpan.FromMilliseconds(2500), parsed.Wait);
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("")]
    [InlineData("NaN")]
    public void ShouldIgnoreRetryAfterMs_WhenItIsNotANumber(string raw)
    {
        var now = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("retry-after-ms", raw);

        var parsed = RateLimitResetHeaders.Parse(response.Headers, now);

        Assert.Null(parsed.Wait);
    }

    [Fact]
    public void ShouldClampRetryAfterMsToZero_WhenItIsNegative()
    {
        var now = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("retry-after-ms", "-40");

        var parsed = RateLimitResetHeaders.Parse(response.Headers, now);

        Assert.Equal(TimeSpan.Zero, parsed.Wait);
    }
}
