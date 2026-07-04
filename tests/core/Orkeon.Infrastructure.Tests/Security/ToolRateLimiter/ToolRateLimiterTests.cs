using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Configuration;
using ToolRateLimiterSut = Orkeon.Infrastructure.Security.ToolRateLimiter;

namespace Orkeon.Infrastructure.Tests.Security;

public sealed class ToolRateLimiterTests : IDisposable
{
    private ToolRateLimiterSut? _sut;

    private ToolRateLimiterSut CreateLimiter(ToolRateLimitOptions? options = null)
    {
        var opts = Options.Create(options ?? new ToolRateLimitOptions());
        _sut = new ToolRateLimiterSut(opts, NullLogger<ToolRateLimiterSut>.Instance);
        return _sut;
    }

    [Fact]
    public async Task ShouldReturnAcquired_WhenUnderLimit()
    {
        var limiter = CreateLimiter(new ToolRateLimitOptions
        {
            GlobalToolRequestsPerMinute = 120,
            DefaultToolRequestsPerMinute = 30
        });

        var result = await limiter.AcquireAsync("FileReadTool", "researcher", TestContext.Current.CancellationToken);

        Assert.True(result.IsAcquired);
        Assert.NotNull(result.Lease);
        result.Lease!.Dispose();
    }

    [Fact]
    public async Task ShouldReturnDenied_WhenOverGlobalToolLimit()
    {
        var limiter = CreateLimiter(new ToolRateLimitOptions
        {
            GlobalToolRequestsPerMinute = 2,
            DefaultToolRequestsPerMinute = 100
        });

        var l1 = await limiter.AcquireAsync("FileReadTool", "agent1", TestContext.Current.CancellationToken);
        var l2 = await limiter.AcquireAsync("FileWriteTool", "agent2", TestContext.Current.CancellationToken);
        Assert.True(l1.IsAcquired);
        Assert.True(l2.IsAcquired);

        var result = await limiter.AcquireAsync("HttpApiTool", "agent3", TestContext.Current.CancellationToken);

        Assert.False(result.IsAcquired);
        Assert.Contains("Global", result.DenialReason!);

        l1.Lease!.Dispose();
        l2.Lease!.Dispose();
    }

    [Fact]
    public async Task ShouldRespectToolSpecificLimit_WhenToolLimitConfigured()
    {
        var limiter = CreateLimiter(new ToolRateLimitOptions
        {
            GlobalToolRequestsPerMinute = 100,
            DefaultToolRequestsPerMinute = 100,
            ToolSpecificLimits =
            {
                ["WebScrapeTool"] = 2
            }
        });

        var l1 = await limiter.AcquireAsync("WebScrapeTool", "agent1", TestContext.Current.CancellationToken);
        var l2 = await limiter.AcquireAsync("WebScrapeTool", "agent2", TestContext.Current.CancellationToken);
        Assert.True(l1.IsAcquired);
        Assert.True(l2.IsAcquired);

        var result = await limiter.AcquireAsync("WebScrapeTool", "agent3", TestContext.Current.CancellationToken);

        Assert.False(result.IsAcquired);
        Assert.Contains("WebScrapeTool", result.DenialReason!);

        l1.Lease!.Dispose();
        l2.Lease!.Dispose();
    }

    [Fact]
    public async Task ShouldUseDefaultLimit_WhenToolHasNoSpecificLimit()
    {
        var limiter = CreateLimiter(new ToolRateLimitOptions
        {
            GlobalToolRequestsPerMinute = 100,
            DefaultToolRequestsPerMinute = 2,
            ToolSpecificLimits =
            {
                ["WebScrapeTool"] = 10
            }
        });

        // UnknownTool has no specific limit -> uses default of 2
        var l1 = await limiter.AcquireAsync("UnknownTool", "agent1", TestContext.Current.CancellationToken);
        var l2 = await limiter.AcquireAsync("UnknownTool", "agent2", TestContext.Current.CancellationToken);
        Assert.True(l1.IsAcquired);
        Assert.True(l2.IsAcquired);

        var result = await limiter.AcquireAsync("UnknownTool", "agent3", TestContext.Current.CancellationToken);

        Assert.False(result.IsAcquired);
        Assert.Contains("UnknownTool", result.DenialReason!);

        l1.Lease!.Dispose();
        l2.Lease!.Dispose();
    }

    [Fact]
    public async Task ShouldMaintainIndependentLimits_WhenDifferentToolsUsed()
    {
        var options = new ToolRateLimitOptions
        {
            GlobalToolRequestsPerMinute = 100,
            DefaultToolRequestsPerMinute = 2,
        };
        options.ToolSpecificLimits.Clear();
        var limiter = CreateLimiter(options);

        // Exhaust ToolA
        var l1 = await limiter.AcquireAsync("ToolA", "agent1", TestContext.Current.CancellationToken);
        var l2 = await limiter.AcquireAsync("ToolA", "agent2", TestContext.Current.CancellationToken);
        var denied = await limiter.AcquireAsync("ToolA", "agent3", TestContext.Current.CancellationToken);
        Assert.False(denied.IsAcquired);

        // ToolB should still work
        var toolB = await limiter.AcquireAsync("ToolB", "agent1", TestContext.Current.CancellationToken);
        Assert.True(toolB.IsAcquired);

        l1.Lease!.Dispose();
        l2.Lease!.Dispose();
        toolB.Lease!.Dispose();
    }

    [Fact]
    public async Task ShouldFreeResourcesWithoutException_WhenDisposed()
    {
        var limiter = CreateLimiter();
        var result = await limiter.AcquireAsync("FileReadTool", "agent1", TestContext.Current.CancellationToken);
        result.Lease?.Dispose();

        var exception = Record.Exception(() => limiter.Dispose());
        Assert.Null(exception);
        _sut = null;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _sut?.Dispose();
    }
}
