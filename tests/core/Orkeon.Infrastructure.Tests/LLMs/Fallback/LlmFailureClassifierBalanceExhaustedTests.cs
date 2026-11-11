using System.Net;
using Orkeon.Infrastructure.LLMs.Profiles;

namespace Orkeon.Infrastructure.Tests.LLMs.Fallback;

/// <summary>Issue #328: a 429 that names a spent balance is told apart from a rate limit.</summary>
public sealed class LlmFailureClassifierBalanceExhaustedTests
{
    [Theory]
    [Trait("BR", "CH-328")]
    [InlineData("{\"error\":{\"code\":\"insufficient_quota\"}}")]
    [InlineData("{\"error\":{\"type\":\"exceeded_current_quota_error\"}}")]
    [InlineData("{\"error\":{\"message\":\"Insufficient balance or no resource package.\"}}")]
    [InlineData("INSUFFICIENT_QUOTA")]
    [InlineData("Exceeded_Current_Quota_Error")]
    [InlineData("insufficient BALANCE")]
    public void ShouldRecogniseAnExhaustedBalance_WhenA429BodyCarriesAMarkerInAnyCase(string body)
    {
        Assert.True(LlmFailureClassifier.IsBalanceExhausted(HttpStatusCode.TooManyRequests, body));
        Assert.Equal(LlmFailureClassifier.BalanceExhaustedNote, LlmFailureClassifier.BalanceExhaustedHint(HttpStatusCode.TooManyRequests, body));
    }

    [Theory]
    [Trait("BR", "CH-328")]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.PaymentRequired)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void ShouldNotRecogniseAnExhaustedBalance_WhenTheStatusIsNot429(HttpStatusCode status)
    {
        Assert.False(LlmFailureClassifier.IsBalanceExhausted(status, "{\"error\":{\"code\":\"insufficient_quota\"}}"));
        Assert.Equal("", LlmFailureClassifier.BalanceExhaustedHint(status, "insufficient_quota"));
    }

    [Theory]
    [Trait("BR", "CH-328")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{\"error\":{\"code\":\"rate_limit_exceeded\"}}")]
    [InlineData("{\"error\":{\"message\":\"You exceeded your current quota, please check your plan\",\"status\":\"RESOURCE_EXHAUSTED\"}}")]
    public void ShouldNotRecogniseAnExhaustedBalance_WhenThe429BodyHasNoMarker(string? body)
    {
        Assert.False(LlmFailureClassifier.IsBalanceExhausted(HttpStatusCode.TooManyRequests, body));
        Assert.Equal("", LlmFailureClassifier.BalanceExhaustedHint(HttpStatusCode.TooManyRequests, body));
    }
}
