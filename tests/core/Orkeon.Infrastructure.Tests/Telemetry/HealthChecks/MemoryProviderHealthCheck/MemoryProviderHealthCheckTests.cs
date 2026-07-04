using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Orkeon.Infrastructure.Tests.Telemetry.HealthChecks;

public class MemoryProviderHealthCheckTests
{
    [Fact]
    public async Task ShouldReturnHealthy_WhenMemoryProviderIsHealthy()
    {
        var fixture = new MemoryProviderHealthCheckTestsFixture();
        var healthCheck = fixture.CreateHealthCheck();

        var result = await MemoryProviderHealthCheckTestsFixture.CheckHealthAsync(healthCheck);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.True(result.Data.ContainsKey("provider"));
        Assert.True(result.Data.ContainsKey("latency_ms"));
    }

    [Fact]
    public async Task ShouldReturnDegraded_WhenMemoryProviderThrows()
    {
        var fixture = new MemoryProviderHealthCheckTestsFixture();
        var healthCheck = fixture
            .WithSearchException(new InvalidOperationException("Connection lost"))
            .CreateHealthCheck();

        var result = await MemoryProviderHealthCheckTestsFixture.CheckHealthAsync(healthCheck);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("Connection lost", result.Description);
    }

    [Fact]
    public void ShouldThrow_WhenProviderIsNull()
    {
        var action = () => new global::Orkeon.Infrastructure.Telemetry.HealthChecks.MemoryProviderHealthCheck(null!);
        Assert.Throws<ArgumentNullException>(action);
    }

    [Fact]
    public async Task ShouldUseHealthCheckQuery_WhenCheckingHealth()
    {
        var fixture = new MemoryProviderHealthCheckTestsFixture();
        var healthCheck = fixture.CreateHealthCheck();

        await MemoryProviderHealthCheckTestsFixture.CheckHealthAsync(healthCheck);

        Assert.Equal("__health_check__", fixture.GetMemoryProvider().LastSearchQuery);
    }
}
