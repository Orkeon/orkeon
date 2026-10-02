using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Orkeon.Infrastructure.Tests.Telemetry.HealthChecks;

/// <summary>
/// The <c>memory_provider</c> check probes the provider by reading a key that does not exist —
/// something all six providers serve, without walking the key space (GAP-30). It used to probe by
/// text search, which Pinecone refuses and ChromaDB does not have over HTTP.
/// </summary>
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
            .WithGetException(new InvalidOperationException("Connection lost"))
            .CreateHealthCheck();

        var result = await MemoryProviderHealthCheckTestsFixture.CheckHealthAsync(healthCheck);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("Connection lost", result.Description);
    }

    [Fact]
    public async Task ShouldReturnHealthy_WhenTheProviderHasNoTextSearch()
    {
        var fixture = new MemoryProviderHealthCheckTestsFixture();
        var healthCheck = fixture
            .WithSearchException(new NotSupportedException("Pinecone has no text search"))
            .CreateHealthCheck();

        var result = await MemoryProviderHealthCheckTestsFixture.CheckHealthAsync(healthCheck);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public void ShouldThrow_WhenProviderIsNull()
    {
        var action = () => new global::Orkeon.Infrastructure.Telemetry.HealthChecks.MemoryProviderHealthCheck(null!);
        Assert.Throws<ArgumentNullException>(action);
    }

    [Fact]
    public async Task ShouldProbeByReadingAnAbsentKey_WhenCheckingHealth()
    {
        var fixture = new MemoryProviderHealthCheckTestsFixture();
        var healthCheck = fixture.CreateHealthCheck();

        await MemoryProviderHealthCheckTestsFixture.CheckHealthAsync(healthCheck);

        Assert.Equal("__health_check__", fixture.GetMemoryProvider().LastGetKey);
        Assert.Equal(0, fixture.GetMemoryProvider().SearchCallCount);
    }
}
