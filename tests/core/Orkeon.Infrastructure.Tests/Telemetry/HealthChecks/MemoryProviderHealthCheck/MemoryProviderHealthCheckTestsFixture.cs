using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Telemetry.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Orkeon.Infrastructure.Tests.Telemetry.HealthChecks;

public class MemoryProviderHealthCheckTestsFixture
{
    private readonly MockMemoryProvider _memoryProvider = new();

    public MemoryProviderHealthCheckTestsFixture WithSearchException(Exception exception)
    {
        _memoryProvider.SetSearchException(exception);
        return this;
    }

    public MemoryProviderHealthCheck CreateHealthCheck()
        => new(_memoryProvider);

    public static async Task<HealthCheckResult> CheckHealthAsync(MemoryProviderHealthCheck healthCheck)
    {
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("test", healthCheck, null, null)
        };
        return await healthCheck.CheckHealthAsync(context);
    }

    public MockMemoryProvider GetMemoryProvider() => _memoryProvider;
}
