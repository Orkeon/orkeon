using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Orkeon.Infrastructure.Tests.Telemetry.HealthChecks;

public class LlmProviderHealthCheckTests
{
    [Fact]
    public async Task ShouldReturnHealthy_WhenProviderIsAvailable()
    {
        var fixture = new LlmProviderHealthCheckTestsFixture();
        var healthCheck = fixture
            .WithName("TestProvider")
            .WithAvailability(true)
            .CreateHealthCheck();

        var result = await LlmProviderHealthCheckTestsFixture.CheckHealthAsync(healthCheck);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("TestProvider", result.Description);
        Assert.Contains("available", result.Description);
        Assert.True(result.Data.ContainsKey("provider"));
        Assert.True(result.Data.ContainsKey("latency_ms"));
    }

    [Fact]
    public async Task ShouldReturnDegraded_WhenProviderIsUnavailable()
    {
        var fixture = new LlmProviderHealthCheckTestsFixture();
        var healthCheck = fixture
            .WithName("TestProvider")
            .WithAvailability(false)
            .CreateHealthCheck();

        var result = await LlmProviderHealthCheckTestsFixture.CheckHealthAsync(healthCheck);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("unavailable", result.Description);
    }

    [Fact]
    public async Task ShouldReturnDegraded_WhenProviderThrows()
    {
        var fixture = new LlmProviderHealthCheckTestsFixture();
        var healthCheck = fixture
            .WithName("TestProvider")
            .WithAvailabilityException(new HttpRequestException("Connection refused"))
            .CreateHealthCheck();

        var result = await LlmProviderHealthCheckTestsFixture.CheckHealthAsync(healthCheck);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("Connection refused", result.Description);
        Assert.True(result.Data.ContainsKey("provider"));
    }

    [Fact]
    public async Task ShouldRecordLatency_WhenCheckingHealth()
    {
        var fixture = new LlmProviderHealthCheckTestsFixture();
        var healthCheck = fixture
            .WithName("TestProvider")
            .WithAvailabilityFunc(async () =>
            {
                await Task.Delay(10);
                return true;
            })
            .CreateHealthCheck();

        var result = await LlmProviderHealthCheckTestsFixture.CheckHealthAsync(healthCheck);

        Assert.True(result.Data.ContainsKey("latency_ms"));
        var latency = (long)result.Data["latency_ms"];
        Assert.True(latency >= 0);
    }

    [Fact]
    public void ShouldThrow_WhenProviderIsNull()
    {
        var action = () => new global::Orkeon.Infrastructure.Telemetry.HealthChecks.LlmProviderHealthCheck(null!);
        Assert.Throws<ArgumentNullException>(action);
    }
}
