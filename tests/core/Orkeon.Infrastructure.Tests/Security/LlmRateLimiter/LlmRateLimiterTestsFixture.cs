using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Infrastructure.Tests.Security;

public sealed class LlmRateLimiterTestsFixture : IDisposable
{
    private readonly ILogger<LlmRateLimiter> _logger = NullLogger<LlmRateLimiter>.Instance;
    private RateLimitingOptions _options = new();
    private LlmRateLimiter? _sut;

    // --- Fluent configuration ---

    public LlmRateLimiterTestsFixture WithGlobalRequestsPerMinute(int value)
    {
        _options.GlobalRequestsPerMinute = value;
        return this;
    }

    public LlmRateLimiterTestsFixture WithProviderRequestsPerMinute(int value)
    {
        _options.ProviderRequestsPerMinute = value;
        return this;
    }

    public LlmRateLimiterTestsFixture WithAgentRequestsPerMinute(int value)
    {
        _options.AgentRequestsPerMinute = value;
        return this;
    }

    public LlmRateLimiterTestsFixture WithQueueLimit(int value)
    {
        _options.QueueLimit = value;
        return this;
    }

    public LlmRateLimiterTestsFixture WithOptions(RateLimitingOptions options)
    {
        _options = options;
        return this;
    }

    // --- Build / Execution ---

    public LlmRateLimiter Build()
    {
        var opts = Options.Create(_options);
        _sut = new LlmRateLimiter(opts, _logger);
        return _sut;
    }

    public async Task<RateLimitAcquisition> AcquireAsync(string provider, string agentRole)
    {
        var limiter = _sut ?? Build();
        return await limiter.AcquireAsync(provider, agentRole);
    }

    // --- Inspection ---

    public LlmRateLimiter GetLimiter() => _sut ?? Build();

    // --- Cleanup ---

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _sut?.Dispose();
    }
}
