using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Configuration;
using LlmRateLimiterSut = Orkeon.Infrastructure.Security.LlmRateLimiter;

namespace Orkeon.Infrastructure.Tests.CovSecurity;

/// <summary>
/// Additional coverage for <see cref="LlmRateLimiterSut"/> focusing on the
/// concurrency-limiter branch and disposed-object guard that the existing
/// suite does not exercise.
/// </summary>
public sealed class CovSecurity_LlmRateLimiterConcurrencyTests : IDisposable
{
    private readonly ILogger<LlmRateLimiterSut> _logger = NullLogger<LlmRateLimiterSut>.Instance;
    private LlmRateLimiterSut? _sut;

    private LlmRateLimiterSut Create(RateLimitingOptions options)
    {
        _sut = new LlmRateLimiterSut(Options.Create(options), _logger);
        return _sut;
    }

    [Fact]
    public async Task AcquireAsync_ShouldAcquire_WhenConcurrencyGateEnabledAndSlotsAvailable()
    {
        var limiter = Create(new RateLimitingOptions
        {
            GlobalRequestsPerMinute = 100,
            ProviderRequestsPerMinute = 100,
            AgentRequestsPerMinute = 100,
            MaxConcurrentRequests = 2,
            QueueLimit = 0
        });

        var result = await limiter.AcquireAsync("openai", "researcher", TestContext.Current.CancellationToken);

        Assert.True(result.IsAcquired);
        Assert.NotNull(result.Lease);
        result.Lease!.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_ShouldDeny_WhenConcurrencyLimitExceeded()
    {
        var limiter = Create(new RateLimitingOptions
        {
            GlobalRequestsPerMinute = 100,
            ProviderRequestsPerMinute = 100,
            AgentRequestsPerMinute = 100,
            MaxConcurrentRequests = 1,
            QueueLimit = 0
        });

        // First in-flight request takes the only concurrency slot and is held.
        var held = await limiter.AcquireAsync("openai", "agent1", TestContext.Current.CancellationToken);
        Assert.True(held.IsAcquired);

        // Second request cannot acquire the concurrency permit.
        var denied = await limiter.AcquireAsync("openai", "agent2", TestContext.Current.CancellationToken);

        Assert.False(denied.IsAcquired);
        Assert.NotNull(denied.DenialReason);
        Assert.Contains("Concurrency", denied.DenialReason!, StringComparison.Ordinal);
        Assert.NotNull(denied.RetryAfter);

        held.Lease!.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_ShouldReleaseConcurrencySlot_WhenLeaseDisposed()
    {
        var limiter = Create(new RateLimitingOptions
        {
            GlobalRequestsPerMinute = 100,
            ProviderRequestsPerMinute = 100,
            AgentRequestsPerMinute = 100,
            MaxConcurrentRequests = 1,
            QueueLimit = 0
        });

        var first = await limiter.AcquireAsync("openai", "agent1", TestContext.Current.CancellationToken);
        Assert.True(first.IsAcquired);
        first.Lease!.Dispose();

        // After releasing, a new request should succeed again.
        var second = await limiter.AcquireAsync("openai", "agent2", TestContext.Current.CancellationToken);
        Assert.True(second.IsAcquired);
        second.Lease!.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_ShouldThrowObjectDisposed_AfterDispose()
    {
        var limiter = Create(new RateLimitingOptions { QueueLimit = 0 });
        limiter.Dispose();
        _sut = null; // prevent double dispose

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => limiter.AcquireAsync("openai", "researcher", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Dispose_ShouldBeIdempotent()
    {
        var limiter = Create(new RateLimitingOptions { MaxConcurrentRequests = 2, QueueLimit = 0 });

        limiter.Dispose();
        var ex = Record.Exception(() => limiter.Dispose());

        Assert.Null(ex);
        _sut = null;
    }

    public void Dispose()
    {
        _sut?.Dispose();
    }
}
