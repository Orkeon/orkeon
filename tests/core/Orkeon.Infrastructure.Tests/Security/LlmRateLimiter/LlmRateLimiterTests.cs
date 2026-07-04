using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Orkeon.Infrastructure.Configuration;
using LlmRateLimiterSut = Orkeon.Infrastructure.Security.LlmRateLimiter;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.Security;

public sealed class LlmRateLimiterTests : IDisposable
{
    private readonly ILogger<LlmRateLimiterSut> _logger = NullLogger<LlmRateLimiterSut>.Instance;
    private LlmRateLimiterSut? _sut;

    private LlmRateLimiterSut CreateLimiter(RateLimitingOptions? options = null)
    {
        var opts = Options.Create(options ?? new RateLimitingOptions());
        _sut = new LlmRateLimiterSut(opts, _logger);
        return _sut;
    }

    [Fact]
    public async Task ShouldReturnAcquired_WhenUnderLimit()
    {
        var limiter = CreateLimiter(new RateLimitingOptions
        {
            GlobalRequestsPerMinute = 60,
            ProviderRequestsPerMinute = 30,
            AgentRequestsPerMinute = 20,
            QueueLimit = 0
        });

        var result = await limiter.AcquireAsync(ProviderOpenAI, "researcher", TestContext.Current.CancellationToken);

        Assert.True(result.IsAcquired);
        Assert.NotNull(result.Lease);
        Assert.Null(result.DenialReason);
        Assert.Null(result.RetryAfter);
        result.Lease!.Dispose();
    }

    [Fact]
    public async Task ShouldReturnDenied_WhenOverGlobalLimit()
    {
        var limiter = CreateLimiter(new RateLimitingOptions
        {
            GlobalRequestsPerMinute = 2,
            ProviderRequestsPerMinute = 100,
            AgentRequestsPerMinute = 100,
            QueueLimit = 0
        });

        // Exhaust global limit
        var lease1 = await limiter.AcquireAsync(ProviderOpenAI, "agent1", TestContext.Current.CancellationToken);
        var lease2 = await limiter.AcquireAsync(ProviderOpenAI, "agent2", TestContext.Current.CancellationToken);
        Assert.True(lease1.IsAcquired);
        Assert.True(lease2.IsAcquired);

        var result = await limiter.AcquireAsync(ProviderOpenAI, "agent3", TestContext.Current.CancellationToken);

        Assert.False(result.IsAcquired);
        Assert.Contains("Global", result.DenialReason);
        Assert.NotNull(result.RetryAfter);

        lease1.Lease!.Dispose();
        lease2.Lease!.Dispose();
    }

    [Fact]
    public async Task ShouldReturnDenied_WhenOverProviderLimit()
    {
        var limiter = CreateLimiter(new RateLimitingOptions
        {
            GlobalRequestsPerMinute = 100,
            ProviderRequestsPerMinute = 2,
            AgentRequestsPerMinute = 100,
            QueueLimit = 0
        });

        var lease1 = await limiter.AcquireAsync(ProviderOpenAI, "agent1", TestContext.Current.CancellationToken);
        var lease2 = await limiter.AcquireAsync(ProviderOpenAI, "agent2", TestContext.Current.CancellationToken);
        Assert.True(lease1.IsAcquired);
        Assert.True(lease2.IsAcquired);

        var result = await limiter.AcquireAsync(ProviderOpenAI, "agent3", TestContext.Current.CancellationToken);

        Assert.False(result.IsAcquired);
        Assert.Contains(ProviderOpenAI, result.DenialReason!);

        lease1.Lease!.Dispose();
        lease2.Lease!.Dispose();
    }

    [Fact]
    public async Task ShouldReturnDenied_WhenOverAgentLimit()
    {
        var limiter = CreateLimiter(new RateLimitingOptions
        {
            GlobalRequestsPerMinute = 100,
            ProviderRequestsPerMinute = 100,
            AgentRequestsPerMinute = 2,
            QueueLimit = 0
        });

        var lease1 = await limiter.AcquireAsync(ProviderOpenAI, "researcher", TestContext.Current.CancellationToken);
        var lease2 = await limiter.AcquireAsync(ProviderAnthropic, "researcher", TestContext.Current.CancellationToken);
        Assert.True(lease1.IsAcquired);
        Assert.True(lease2.IsAcquired);

        var result = await limiter.AcquireAsync(ProviderOpenAI, "researcher", TestContext.Current.CancellationToken);

        Assert.False(result.IsAcquired);
        Assert.Contains("researcher", result.DenialReason!);

        lease1.Lease!.Dispose();
        lease2.Lease!.Dispose();
    }

    [Fact]
    public async Task ShouldReturnRetryAfter_WhenRequestIsDenied()
    {
        var limiter = CreateLimiter(new RateLimitingOptions
        {
            GlobalRequestsPerMinute = 1,
            ProviderRequestsPerMinute = 100,
            AgentRequestsPerMinute = 100,
            QueueLimit = 0
        });

        var lease1 = await limiter.AcquireAsync(ProviderOpenAI, "agent1", TestContext.Current.CancellationToken);
        Assert.True(lease1.IsAcquired);

        var result = await limiter.AcquireAsync(ProviderOpenAI, "agent2", TestContext.Current.CancellationToken);

        Assert.False(result.IsAcquired);
        Assert.NotNull(result.RetryAfter);
        Assert.True(result.RetryAfter!.Value > TimeSpan.Zero);

        lease1.Lease!.Dispose();
    }

    [Fact]
    public async Task ShouldFreeResourcesWithoutException_WhenDisposed()
    {
        var limiter = CreateLimiter();

        // Acquire some leases to create provider/agent limiters
        var result = await limiter.AcquireAsync(ProviderOpenAI, "researcher", TestContext.Current.CancellationToken);
        result.Lease?.Dispose();

        // Should not throw
        var exception = Record.Exception(() => limiter.Dispose());
        Assert.Null(exception);
        _sut = null; // Prevent double dispose in test cleanup
    }

    [Fact]
    public async Task ShouldAcquireAll_WhenMultipleSequentialRequestsAreWithinLimit()
    {
        var limiter = CreateLimiter(new RateLimitingOptions
        {
            GlobalRequestsPerMinute = 10,
            ProviderRequestsPerMinute = 10,
            AgentRequestsPerMinute = 10,
            QueueLimit = 0
        });

        var leases = new List<IDisposable>();
        for (int i = 0; i < 5; i++)
        {
            var result = await limiter.AcquireAsync(ProviderOpenAI, "researcher", TestContext.Current.CancellationToken);
            Assert.True(result.IsAcquired, $"Request {i} should be acquired");
            leases.Add(result.Lease!);
        }

        foreach (var lease in leases)
            lease.Dispose();
    }

    [Fact]
    public async Task ShouldHandleLeaseDisposalIdempotently_WhenDisposedMultipleTimes()
    {
        var limiter = CreateLimiter(new RateLimitingOptions
        {
            GlobalRequestsPerMinute = 60,
            ProviderRequestsPerMinute = 30,
            AgentRequestsPerMinute = 20,
            QueueLimit = 0
        });

        var result = await limiter.AcquireAsync(ProviderOpenAI, "researcher", TestContext.Current.CancellationToken);
        Assert.True(result.IsAcquired);

        // Should not throw when disposed
        result.Lease!.Dispose();

        // Should not throw when disposed again (idempotent)
        result.Lease.Dispose();
    }

    [Fact]
    public async Task ShouldMaintainIndependentLimits_WhenDifferentProvidersUsed()
    {
        var limiter = CreateLimiter(new RateLimitingOptions
        {
            GlobalRequestsPerMinute = 100,
            ProviderRequestsPerMinute = 2,
            AgentRequestsPerMinute = 100,
            QueueLimit = 0
        });

        // Exhaust openai limit
        var l1 = await limiter.AcquireAsync(ProviderOpenAI, "agent1", TestContext.Current.CancellationToken);
        var l2 = await limiter.AcquireAsync(ProviderOpenAI, "agent2", TestContext.Current.CancellationToken);
        Assert.True(l1.IsAcquired);
        Assert.True(l2.IsAcquired);

        // openai should be denied
        var denied = await limiter.AcquireAsync(ProviderOpenAI, "agent3", TestContext.Current.CancellationToken);
        Assert.False(denied.IsAcquired);

        // anthropic should still work (independent provider limiter)
        var anthropic = await limiter.AcquireAsync(ProviderAnthropic, "agent1", TestContext.Current.CancellationToken);
        Assert.True(anthropic.IsAcquired);

        l1.Lease!.Dispose();
        l2.Lease!.Dispose();
        anthropic.Lease!.Dispose();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _sut?.Dispose();
    }
}
