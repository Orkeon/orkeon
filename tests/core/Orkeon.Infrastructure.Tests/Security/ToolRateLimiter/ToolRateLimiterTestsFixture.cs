using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Infrastructure.Tests.Security;

public sealed class ToolRateLimiterTestsFixture : IDisposable
{
    private ToolRateLimitOptions _options = new();
    private ToolRateLimiter? _sut;

    // --- Fluent configuration ---

    public ToolRateLimiterTestsFixture WithGlobalToolRequestsPerMinute(int value)
    {
        _options.GlobalToolRequestsPerMinute = value;
        return this;
    }

    public ToolRateLimiterTestsFixture WithDefaultToolRequestsPerMinute(int value)
    {
        _options.DefaultToolRequestsPerMinute = value;
        return this;
    }

    public ToolRateLimiterTestsFixture WithToolSpecificLimit(string toolName, int limit)
    {
        _options.ToolSpecificLimits[toolName] = limit;
        return this;
    }

    public ToolRateLimiterTestsFixture WithToolSpecificLimits(Dictionary<string, int> limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        _options.ToolSpecificLimits.Clear();
        foreach (var (key, value) in limits)
            _options.ToolSpecificLimits[key] = value;
        return this;
    }

    public ToolRateLimiterTestsFixture WithOptions(ToolRateLimitOptions options)
    {
        _options = options;
        return this;
    }

    // --- Build / Execution ---

    public ToolRateLimiter Build()
    {
        var opts = Options.Create(_options);
        _sut = new ToolRateLimiter(opts, NullLogger<ToolRateLimiter>.Instance);
        return _sut;
    }

    public async Task<RateLimitAcquisition> AcquireAsync(string toolName, string agentRole)
    {
        var limiter = _sut ?? Build();
        return await limiter.AcquireAsync(toolName, agentRole);
    }

    // --- Inspection ---

    public ToolRateLimiter GetLimiter() => _sut ?? Build();

    // --- Cleanup ---

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _sut?.Dispose();
    }
}
