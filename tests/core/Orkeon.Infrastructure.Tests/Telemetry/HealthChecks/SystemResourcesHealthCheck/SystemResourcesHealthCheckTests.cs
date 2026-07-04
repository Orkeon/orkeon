using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Orkeon.Infrastructure.Tests.Telemetry.HealthChecks;

public class SystemResourcesHealthCheckTests
{
    private readonly SystemResourcesHealthCheckTestsFixture _fixture = new();

    [Fact]
    public async Task ShouldReturnHealthy_WhenThresholdIsHigh()
    {
        var healthCheck = SystemResourcesHealthCheckTestsFixture.CreateHealthCheck(100_000);

        var result = await SystemResourcesHealthCheckTestsFixture.CheckHealthAsync(healthCheck);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.True(result.Data.ContainsKey("working_set_mb"));
        Assert.True(result.Data.ContainsKey("heap_size_mb"));
        Assert.True(result.Data.ContainsKey("thread_count"));
        Assert.True(result.Data.ContainsKey("gc_gen0_collections"));
        Assert.True(result.Data.ContainsKey("gc_gen1_collections"));
        Assert.True(result.Data.ContainsKey("gc_gen2_collections"));
        Assert.True(result.Data.ContainsKey("max_memory_mb"));
    }

    [Fact]
    public async Task ShouldReturnDegraded_WhenThresholdIsVeryLow()
    {
        var healthCheck = SystemResourcesHealthCheckTestsFixture.CreateHealthCheck(1);

        var result = await SystemResourcesHealthCheckTestsFixture.CheckHealthAsync(healthCheck);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("exceeds threshold", result.Description);
        Assert.Contains("1 MB", result.Description);
    }

    [Fact]
    public async Task ShouldIncludeResourceData_WhenCheckingHealth()
    {
        var healthCheck = SystemResourcesHealthCheckTestsFixture.CreateHealthCheck(100_000);

        var result = await SystemResourcesHealthCheckTestsFixture.CheckHealthAsync(healthCheck);

        var workingSet = (double)result.Data["working_set_mb"];
        Assert.True(workingSet > 0);

        var threadCount = (int)result.Data["thread_count"];
        Assert.True(threadCount > 0);
    }

    [Fact]
    public void ShouldThrow_WhenOptionsAreNull()
    {
        var action = () => new global::Orkeon.Infrastructure.Telemetry.HealthChecks.SystemResourcesHealthCheck(null!);
        Assert.Throws<ArgumentNullException>(action);
    }
}
